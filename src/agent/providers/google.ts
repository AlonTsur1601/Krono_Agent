import { GoogleGenAI, Content, Part, GenerateContentResponse } from '@google/genai'
import { randomUUID } from 'crypto'
import type { ProviderAdapter, ProviderConfig, ConnectionTestResult, Model, ModelCapabilities, CompletionRequest, CompletionChunk } from '../types'

export class GoogleProvider implements ProviderAdapter {
  id = 'google'
  name = 'Google (Gemini)'
  private client: GoogleGenAI | null = null
  private capabilities = new Map<string, ModelCapabilities>()
  async configure(config: ProviderConfig) { this.client = new GoogleGenAI({ apiKey: config.apiKey }) }
  async testConnection(): Promise<ConnectionTestResult> {
    const start = Date.now()
    try { await this.listModels(); return { success: true, latencyMs: Date.now() - start } }
    catch { return { success: false, error: 'Could not authenticate or list Gemini models.' } }
  }
  async listModels(): Promise<Model[]> {
    if (!this.client) throw new Error('Not configured')
    const models: Model[] = []
    for await (const info of await this.client.models.list()) {
      if (!info.name || !info.supportedActions?.includes('generateContent')) continue
      const id = info.name.replace(/^models\//, '')
      const capabilities: ModelCapabilities = { supportsTools: true, supportsImages: false, supportsReasoningEffort: false, supportsStructuredOutput: false, contextWindow: info.inputTokenLimit ?? 0, maxOutputTokens: info.outputTokenLimit ?? 0, streamingSupported: true }
      this.capabilities.set(id, capabilities); models.push({ id, name: info.displayName ?? id, providerId: this.id, capabilities })
    }
    return models
  }
  getModelCapabilities(modelId: string): ModelCapabilities { return this.capabilities.get(modelId) ?? { supportsTools: true, supportsImages: false, supportsReasoningEffort: false, supportsStructuredOutput: false, contextWindow: 0, maxOutputTokens: 0, streamingSupported: true } }
  async *createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk> {
    if (!this.client) throw new Error('Not configured')
    const calls = new Map<string, { name: string; id?: string }>()
    const contents: Content[] = []
    for (const message of request.messages) {
      if (message.role === 'system') continue
      let parts: Part[]
      if (message.toolCalls) {
        const original = message.providerState?.googleParts as Part[] | undefined
        const functions = original?.filter(part => part.functionCall)
        message.toolCalls.forEach((call, index) => calls.set(call.id, { name: call.name, id: functions?.[index]?.functionCall?.id }))
        parts = original ?? message.toolCalls.map(call => ({ functionCall: { name: call.name, args: call.arguments } }))
      } else if (message.role === 'tool') {
        const call = calls.get(message.toolCallId ?? '')
        if (!call) throw new Error('Gemini tool result has no matching function call')
        parts = [{ functionResponse: { name: call.name, ...(call.id ? { id: call.id } : {}), response: { output: message.content } } }]
      } else parts = [{ text: message.content }]
      const role = message.role === 'assistant' ? 'model' : 'user'
      const previous = contents.at(-1)
      if (previous?.role === role) previous.parts!.push(...parts)
      else contents.push({ role, parts })
    }
    const config = {
      systemInstruction: request.messages.filter(message => message.role === 'system').map(message => message.content).join('\n'),
      abortSignal: request.signal,
      ...(request.maxTokens ? { maxOutputTokens: request.maxTokens } : {}),
      ...(request.tools?.length ? { tools: [{ functionDeclarations: request.tools.map(tool => ({ name: tool.name, description: tool.description, parametersJsonSchema: tool.inputSchema })) }] } : {}),
    }
    const responses = request.stream === false
      ? [await this.client.models.generateContent({ model: request.model, contents, config })]
      : await this.client.models.generateContentStream({ model: request.model, contents, config })
    const parts: Part[] = []
    let usage: GenerateContentResponse['usageMetadata']
    for await (const response of responses) {
      if (response.promptFeedback?.blockReason) throw new Error('Gemini blocked the request before generating a response')
      usage = response.usageMetadata ?? usage
      const candidate = response.candidates?.[0]
      if (candidate?.finishReason && !['STOP','MAX_TOKENS'].includes(candidate.finishReason)) throw new Error('Gemini could not complete the response')
      for (const part of candidate?.content?.parts ?? []) {
        parts.push(part)
        if (part.text && !part.thought) yield { type: 'text', content: part.text }
        if (part.functionCall?.name) yield { type: 'tool_call', toolCall: { id: part.functionCall.id ?? randomUUID(), name: part.functionCall.name, arguments: part.functionCall.args ?? {} } }
      }
    }
    // Keep opaque signatures and model-only parts in the trusted runtime, never UI.
    yield { type: 'provider_state', providerState: { googleParts: parts } }
    yield { type: 'done', usage: usage ? { promptTokens: usage.promptTokenCount ?? 0, completionTokens: usage.candidatesTokenCount ?? 0, totalTokens: usage.totalTokenCount ?? 0 } : undefined }
  }
  async cancelCompletion(): Promise<void> { throw new Error('Cancel this request using its AbortSignal.') }
}
