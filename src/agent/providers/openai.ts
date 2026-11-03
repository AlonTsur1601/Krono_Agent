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

    try {
      const stream = await this.client.chat.completions.create({
        model: request.model,
        messages: request.messages as any,
        tools: request.tools ? this.convertTools(request.tools) : undefined,
        temperature: request.temperature,
        max_tokens: request.maxTokens,
        stream: request.stream !== false,
      })

      if (request.stream === false) {
        // Non-streaming response
        const response = stream as any
        yield {
          type: 'text',
          content: response.choices[0]?.message?.content || '',
        }
        yield {
          type: 'done',
          usage: {
            promptTokens: response.usage?.prompt_tokens || 0,
            completionTokens: response.usage?.completion_tokens || 0,
            totalTokens: response.usage?.total_tokens || 0,
          },
        }
        return
      }

      // Streaming response
      for await (const chunk of stream as any) {
        const delta = chunk.choices[0]?.delta

        if (delta?.content) {
          yield {
            type: 'text',
            content: delta.content,
          }
        }

        if (delta?.tool_calls) {
          for (const toolCall of delta.tool_calls) {
            if (toolCall.function) {
              yield {
                type: 'tool_call',
                toolCall: {
                  id: toolCall.id || '',
                  name: toolCall.function.name || '',
                  arguments: JSON.parse(toolCall.function.arguments || '{}'),
                },
              }
            }
          }
        }

        if (chunk.choices[0]?.finish_reason) {
          yield {
            type: 'done',
            usage: chunk.usage
              ? {
                  promptTokens: chunk.usage.prompt_tokens || 0,
                  completionTokens: chunk.usage.completion_tokens || 0,
                  totalTokens: chunk.usage.total_tokens || 0,
                }
              : undefined,
          }
        }
      }
    } catch (error) {
      yield {
        type: 'error',
        error: error instanceof Error ? error : new Error('Unknown error'),
      }
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
