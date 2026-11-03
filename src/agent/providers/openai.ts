import OpenAI from 'openai'
import type {
  ProviderAdapter,
  ProviderConfig,
  ConnectionTestResult,
  Model,
  ModelCapabilities,
  CompletionRequest,
  CompletionChunk,
} from '../types'

export class OpenAIProvider implements ProviderAdapter {
  id = 'openai'
  name = 'OpenAI'
  private client: OpenAI | null = null
  private apiKey: string | null = null

  async configure(config: ProviderConfig): Promise<void> {
    this.apiKey = config.apiKey as string
    this.client = new OpenAI({
      apiKey: this.apiKey,
      dangerouslyAllowBrowser: false, // Only for Node.js (main process)
    })
  }

  async testConnection(): Promise<ConnectionTestResult> {
    if (!this.client) {
      return { success: false, error: 'Not configured' }
    }

    try {
      const start = Date.now()
      await this.client.models.list()
      const latencyMs = Date.now() - start
      return { success: true, latencyMs }
    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Connection failed',
      }
    }
  }

  async listModels(): Promise<Model[]> {
    if (!this.client) throw new Error('Not configured')

    try {
      const response = await this.client.models.list()
      return response.data
        .filter((m) => m.id.startsWith('gpt-'))
        .map((m) => ({
          id: m.id,
          providerId: this.id,
          name: m.id,
          capabilities: this.getCapabilitiesForModel(m.id),
        }))
    } catch (error) {
      console.error('Failed to list models:', error)
      return []
    }
  }

  getModelCapabilities(modelId: string): ModelCapabilities {
    return this.getCapabilitiesForModel(modelId)
  }

  private getCapabilitiesForModel(modelId: string): ModelCapabilities {
    // GPT-4 and GPT-4 Turbo
    if (modelId.includes('gpt-4')) {
      return {
        supportsTools: true,
        supportsImages: modelId.includes('vision'),
        supportsReasoningEffort: false,
        supportsStructuredOutput: true,
        contextWindow: modelId.includes('turbo') ? 128000 : 8192,
        maxOutputTokens: 4096,
        streamingSupported: true,
      }
    }

    // GPT-3.5
    if (modelId.includes('gpt-3.5')) {
      return {
        supportsTools: true,
        supportsImages: false,
        supportsReasoningEffort: false,
        supportsStructuredOutput: false,
        contextWindow: 16385,
        maxOutputTokens: 4096,
        streamingSupported: true,
      }
    }

    // o1 models
    if (modelId.includes('o1')) {
      return {
        supportsTools: false,
        supportsImages: false,
        supportsReasoningEffort: true,
        supportsStructuredOutput: false,
        contextWindow: 128000,
        maxOutputTokens: 32768,
        streamingSupported: false,
      }
    }

    // Default capabilities
    return {
      supportsTools: false,
      supportsImages: false,
      supportsReasoningEffort: false,
      supportsStructuredOutput: false,
      contextWindow: 4096,
      maxOutputTokens: 2048,
      streamingSupported: true,
    }
  }

  async *createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk> {
    if (!this.client) throw new Error('Not configured')

    const input = request.messages.flatMap((message): any[] => {
      if (message.role === 'tool') return [{ type: 'function_call_output', call_id: message.toolCallId, output: message.content }]
      if (message.toolCalls) return message.toolCalls.map(call => ({ type: 'function_call', call_id: call.id, name: call.name, arguments: JSON.stringify(call.arguments) }))
      return [{ role: message.role, content: message.content }]
    })
    const stream = await this.client.responses.create({
      model: request.model,
      input,
      tools: request.tools?.map(tool => ({ type: 'function' as const, name: tool.name, description: tool.description, parameters: tool.inputSchema, strict: false })),
      ...(request.reasoningEffort ? { reasoning: { effort: request.reasoningEffort } } : {}),
      ...(request.maxTokens ? { max_output_tokens: request.maxTokens } : {}),
      stream: true,
      store: false,
    }, { signal: request.signal })
    for await (const event of stream) {
      if (event.type === 'response.output_text.delta') yield { type: 'text', content: event.delta }
      if (event.type === 'response.output_item.done' && event.item.type === 'function_call') {
        yield { type: 'tool_call', toolCall: { id: event.item.call_id, name: event.item.name, arguments: JSON.parse(event.item.arguments) } }
      }
      if (event.type === 'response.completed') {
        const usage = event.response.usage
        yield { type: 'done', usage: usage ? { promptTokens: usage.input_tokens, completionTokens: usage.output_tokens, totalTokens: usage.total_tokens } : undefined }
      }
      if (event.type === 'response.failed' || event.type === 'response.incomplete') throw new Error('Provider could not complete the response')
      if (event.type === 'error') throw new Error(event.message)
    }
  }

  async cancelCompletion(_requestId: string): Promise<void> {
    // OpenAI doesn't support cancellation via API
    // Would need to track AbortController per request
    throw new Error('Cancellation not implemented')
  }

  private convertTools(tools: any[]): any[] {
    return tools.map((tool) => ({
      type: 'function',
      function: {
        name: tool.name,
        description: tool.description,
        parameters: tool.inputSchema,
      },
    }))
  }
}
