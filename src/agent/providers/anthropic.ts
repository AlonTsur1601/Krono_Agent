import Anthropic from '@anthropic-ai/sdk'
import type { MessageParam, ContentBlockParam } from '@anthropic-ai/sdk/resources/messages'
import type { ProviderAdapter, ProviderConfig, ConnectionTestResult, Model, ModelCapabilities, CompletionRequest, CompletionChunk, ToolCall } from '../types'

export class AnthropicProvider implements ProviderAdapter {
  id = 'anthropic'
  name = 'Anthropic'
  private client: Anthropic | null = null
  private capabilities = new Map<string, ModelCapabilities>()
  async configure(config: ProviderConfig) { this.client = new Anthropic({ apiKey: config.apiKey, ...(config.endpoint ? { baseURL: config.endpoint } : {}) }) }
  async testConnection(): Promise<ConnectionTestResult> {
    const start = Date.now()
    try { await this.listModels(); return { success: true, latencyMs: Date.now() - start } }
    catch { return { success: false, error: 'Could not authenticate or list Anthropic models.' } }
  }
  async listModels(): Promise<Model[]> {
    if (!this.client) throw new Error('Not configured')
    const models: Model[] = []
    for await (const info of this.client.models.list()) {
      if (info.lifecycle === 'retired') continue
      const capabilities: ModelCapabilities = { supportsTools: true, supportsImages: info.capabilities?.image_input.supported ?? false, supportsReasoningEffort: false, supportsStructuredOutput: info.capabilities?.structured_outputs.supported ?? false, contextWindow: info.max_input_tokens ?? 0, maxOutputTokens: info.max_tokens ?? 4096, streamingSupported: true }
      this.capabilities.set(info.id, capabilities)
      models.push({ id: info.id, name: info.display_name, providerId: this.id, capabilities })
    }
    return models
  }
  getModelCapabilities(modelId: string): ModelCapabilities { return this.capabilities.get(modelId) ?? { supportsTools: true, supportsImages: false, supportsReasoningEffort: false, supportsStructuredOutput: false, contextWindow: 0, maxOutputTokens: 4096, streamingSupported: true } }
  async *createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk> {
    if (!this.client) throw new Error('Not configured')
    const messages: MessageParam[] = []
    for (const message of request.messages) {
      if (message.role === 'system') continue
      const content: ContentBlockParam[] = message.role === 'tool'
        ? [{ type: 'tool_result', tool_use_id: message.toolCallId!, content: message.content }]
        : [...(message.content ? [{ type: 'text' as const, text: message.content }] : []), ...(message.toolCalls ?? []).map(call => ({ type: 'tool_use' as const, id: call.id, name: call.name, input: call.arguments }))]
      const role = message.role === 'assistant' ? 'assistant' : 'user'
      const previous = messages.at(-1)
      if (previous?.role === role && Array.isArray(previous.content)) previous.content.push(...content)
      else messages.push({ role, content })
    }
    const stream = await this.client.messages.create({ model: request.model, max_tokens: request.maxTokens ?? 4096,
      system: request.messages.filter(message => message.role === 'system').map(message => message.content).join('\n'), messages,
      ...(request.tools?.length ? { tools: request.tools.map(tool => ({ name: tool.name, description: tool.description, input_schema: { ...tool.inputSchema, type: 'object' as const } })) } : {}), stream: true,
    }, { signal: request.signal })
    const pending = new Map<number, { call: ToolCall; json: string }>()
    let inputTokens = 0, outputTokens = 0
    for await (const event of stream) {
      if (event.type === 'message_start') { inputTokens = event.message.usage.input_tokens; outputTokens = event.message.usage.output_tokens }
      if (event.type === 'content_block_start' && event.content_block.type === 'tool_use') pending.set(event.index, { call: { id: event.content_block.id, name: event.content_block.name, arguments: event.content_block.input as Record<string, unknown> }, json: '' })
      if (event.type === 'content_block_delta') {
        if (event.delta.type === 'text_delta') yield { type: 'text', content: event.delta.text }
        if (event.delta.type === 'input_json_delta') { const part = pending.get(event.index); if (part) part.json += event.delta.partial_json }
      }
      if (event.type === 'content_block_stop') {
        const part = pending.get(event.index)
        if (part) { if (part.json) part.call.arguments = JSON.parse(part.json); yield { type: 'tool_call', toolCall: part.call }; pending.delete(event.index) }
      }
      if (event.type === 'message_delta') outputTokens = event.usage.output_tokens
    }
    yield { type: 'done', usage: { promptTokens: inputTokens, completionTokens: outputTokens, totalTokens: inputTokens + outputTokens } }
  }
  async cancelCompletion(): Promise<void> { throw new Error('Cancel this request using its AbortSignal.') }
}
