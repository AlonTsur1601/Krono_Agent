import OpenAI from 'openai'
import profiles from './catalog.json'
import type { CompletionChunk, CompletionRequest, ConnectionTestResult, Model, ModelCapabilities, ProviderAdapter, ProviderConfig } from '../types'

export const COMPATIBLE_PROFILES = profiles
export type CompatibleId = keyof typeof profiles
export function isCompatibleProvider(id: string): id is CompatibleId { return Object.hasOwn(profiles, id) }

// Keys must never be placed in URLs, redirects, command lines or error messages.
export function compatibleEndpoint(value: string): URL {
  let url: URL
  try { url = new URL(value) } catch { throw new Error('Enter a complete endpoint URL, including /v1.') }
  const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && loopback)) || url.username || url.password || url.search || url.hash) {
    throw new Error('Use HTTPS, or HTTP on loopback only. Credentials, query parameters and fragments are not allowed in endpoint URLs.')
  }
  return url
}

export class CompatibleProvider implements ProviderAdapter {
  readonly name: string
  private client: OpenAI | null = null
  private toolsEnabled = false
  constructor(public readonly id: CompatibleId) { this.name = profiles[id].name }

  async configure(config: ProviderConfig): Promise<void> {
    const url = compatibleEndpoint(config.endpoint || profiles[this.id].endpoint)
    const key = config.apiKey?.trim()
    if (!key && (profiles[this.id].requiresKey || !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname))) throw new Error('This provider or remote endpoint requires an API key.')
    this.toolsEnabled = config.toolsEnabled === true
    this.client = new OpenAI({ apiKey: key || 'local', baseURL: url.href.replace(/\/$/, ''), maxRetries: 0, timeout: 120_000,
      // Refuse redirects so credentials cannot follow an endpoint to another host.
      fetchOptions: { redirect: 'error' }, dangerouslyAllowBrowser: false })
  }

  async testConnection(): Promise<ConnectionTestResult> {
    try { const start = Date.now(); await this.listModels(); return { success: true, latencyMs: Date.now() - start } }
    catch { return { success: false, error: 'Connection failed. Check the endpoint, server and credentials; you can also enter a model ID manually.' } }
  }

  async listModels(): Promise<Model[]> {
    if (!this.client) throw new Error('Provider not configured')
    try { const result = await this.client.models.list({ timeout: 15_000 }); return result.data.map(model => ({ id: model.id, name: model.id, providerId: this.id, capabilities: this.getModelCapabilities(model.id) })) }
    catch { throw new Error('Could not discover models. Check the server and credentials, or enter a model ID manually.') }
  }

  getModelCapabilities(_modelId: string): ModelCapabilities {
    return { supportsTools: this.toolsEnabled, supportsImages: false, supportsReasoningEffort: false, supportsStructuredOutput: false, contextWindow: 0, maxOutputTokens: 0, streamingSupported: true }
  }

  async *createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk> {
    if (!this.client) throw new Error('Provider not configured')
    if (request.tools?.length && !this.toolsEnabled) throw new Error('Tool calling is disabled for this endpoint/model.')
    const messages: OpenAI.Chat.Completions.ChatCompletionMessageParam[] = request.messages.map(message => {
      if (message.role === 'tool') return { role: 'tool', content: message.content, tool_call_id: message.toolCallId! }
      if (message.toolCalls?.length) return { role: 'assistant', content: message.content || null, tool_calls: message.toolCalls.map(call => ({ id: call.id, type: 'function', function: { name: call.name, arguments: JSON.stringify(call.arguments) } })) }
      return { role: message.role, content: message.content }
    })
    const calls = new Map<number, { id: string; name: string; arguments: string }>()
    try {
      const stream = await this.client.chat.completions.create({ model: request.model, messages, stream: true,
        ...(request.tools?.length ? { tools: request.tools.map(tool => ({ type: 'function' as const, function: { name: tool.name, description: tool.description, parameters: tool.inputSchema } })) } : {}),
        ...(request.maxTokens ? { max_tokens: request.maxTokens } : {}),
      }, { signal: request.signal, timeout: 30 * 60 * 1000 })
      let finished = false
      for await (const chunk of stream) {
        const choice = chunk.choices[0]
        if (!choice) continue
        if (choice.delta.content) yield { type: 'text', content: choice.delta.content }
        for (const delta of choice.delta.tool_calls || []) {
          const call = calls.get(delta.index) || { id: '', name: '', arguments: '' }
          if (delta.id) call.id = delta.id
          call.name += delta.function?.name || ''; call.arguments += delta.function?.arguments || ''; calls.set(delta.index, call)
        }
        if (choice.finish_reason) {
          if (!['stop', 'tool_calls'].includes(choice.finish_reason)) throw new Error('Incomplete response')
          finished = true
        }
      }
      if (!finished) throw new Error('Stream ended before completion')
      for (const call of calls.values()) {
        const args: unknown = JSON.parse(call.arguments || '{}')
        if (!call.id || !call.name || !args || typeof args !== 'object' || Array.isArray(args)) throw new Error('Invalid tool call')
        yield { type: 'tool_call', toolCall: { id: call.id, name: call.name, arguments: args as Record<string, unknown> } }
      }
      yield { type: 'done' }
    } catch (error) {
      if (request.signal?.aborted) throw new Error('Run cancelled')
      if (error instanceof OpenAI.APIError) {
        if (error.status === 401 || error.status === 403) throw new Error('Provider authentication or model access denied. Check the key and model permissions.')
        if (error.status === 429) {
          const retry = error.headers?.get('retry-after') || ''
          const seconds = /^\d+$/.test(retry) ? Number(retry) : (Date.parse(retry) - Date.now()) / 1000
          throw new Error('Provider rate limit or quota reached.' + (Number.isFinite(seconds) && seconds > 0 ? ` Retry in ${Math.min(86400, Math.ceil(seconds))} seconds.` : ' Check your provider quota before retrying.'))
        }
        if (error.status && error.status >= 500) throw new Error('Provider is temporarily unavailable. Try again later.')
      }
      throw new Error('Endpoint response failed or was incomplete. Check the server, model ID, model capabilities and credentials.')
    }
  }

  async cancelCompletion(_requestId: string): Promise<void> { /* Requests use AbortSignal. */ }
}
