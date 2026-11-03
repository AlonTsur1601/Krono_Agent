import Anthropic from '@anthropic-ai/sdk'
import type {
  ProviderAdapter,
  ProviderConfig,
  ConnectionTestResult,
  Model,
  ModelCapabilities,
  CompletionRequest,
  CompletionChunk,
} from '../types'

export class AnthropicProvider implements ProviderAdapter {
  id = 'anthropic'
  name = 'Anthropic'
  private client: Anthropic | null = null
  private apiKey: string | null = null

  async configure(config: ProviderConfig): Promise<void> {
    this.apiKey = config.apiKey as string
    this.client = new Anthropic({
      apiKey: this.apiKey,
    })
  }

  async testConnection(): Promise<ConnectionTestResult> {
    if (!this.client) {
      return { success: false, error: 'Not configured' }
    }

    try {
      const start = Date.now()
      // Test with a minimal completion
      await this.client.messages.create({
        model: 'claude-3-haiku-20240307',
        max_tokens: 1,
        messages: [{ role: 'user', content: 'Hi' }],
      })
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
    // Anthropic doesn't have a models list endpoint, return known models
    return [
      {
        id: 'claude-3-5-sonnet-20241022',
        providerId: this.id,
        name: 'Claude 3.5 Sonnet',
        capabilities: {
          supportsTools: true,
          supportsImages: true,
          supportsReasoningEffort: false,
          supportsStructuredOutput: false,
          contextWindow: 200000,
          maxOutputTokens: 8192,
          streamingSupported: true,
        },
      },
      {
        id: 'claude-3-5-haiku-20241022',
        providerId: this.id,
        name: 'Claude 3.5 Haiku',
        capabilities: {
          supportsTools: true,
          supportsImages: false,
          supportsReasoningEffort: false,
          supportsStructuredOutput: false,
          contextWindow: 200000,
          maxOutputTokens: 8192,
          streamingSupported: true,
        },
      },
      {
        id: 'claude-3-opus-20240229',
        providerId: this.id,
        name: 'Claude 3 Opus',
        capabilities: {
          supportsTools: true,
          supportsImages: true,
          supportsReasoningEffort: false,
          supportsStructuredOutput: false,
          contextWindow: 200000,
          maxOutputTokens: 4096,
          streamingSupported: true,
        },
      },
      {
        id: 'claude-3-sonnet-20240229',
        providerId: this.id,
        name: 'Claude 3 Sonnet',
        capabilities: {
          supportsTools: true,
          supportsImages: true,
          supportsReasoningEffort: false,
          supportsStructuredOutput: false,
          contextWindow: 200000,
          maxOutputTokens: 4096,
          streamingSupported: true,
        },
      },
      {
        id: 'claude-3-haiku-20240307',
        providerId: this.id,
        name: 'Claude 3 Haiku',
        capabilities: {
          supportsTools: true,
          supportsImages: true,
          supportsReasoningEffort: false,
          supportsStructuredOutput: false,
          contextWindow: 200000,
          maxOutputTokens: 4096,
          streamingSupported: true,
        },
      },
    ]
  }

  getModelCapabilities(modelId: string): ModelCapabilities {
    const model = this.listModels().then((models) => models.find((m) => m.id === modelId))
    // Default capabilities
    return {
      supportsTools: true,
      supportsImages: true,
      supportsReasoningEffort: false,
      supportsStructuredOutput: false,
      contextWindow: 200000,
      maxOutputTokens: 4096,
      streamingSupported: true,
    }
  }

  async *createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk> {
    if (!this.client) throw new Error('Not configured')

    try {
      // Extract system message
      const systemMessage = request.messages.find((m) => m.role === 'system')?.content
      const messages = request.messages
        .filter((m) => m.role !== 'system')
        .map((m) => ({
          role: m.role as 'user' | 'assistant',
          content: m.content,
        }))

      const stream = await this.client.messages.create({
        model: request.model,
        max_tokens: request.maxTokens || 4096,
        temperature: request.temperature,
        system: systemMessage,
        messages,
        ...(request.tools ? { tools: this.convertTools(request.tools) } : {}),
        stream: true,
      } as any)

      if (!request.stream || !('on' in stream)) {
        // Non-streaming response
        const response = stream as any
        yield {
          type: 'text',
          content: response.content[0]?.text || '',
        }
        yield {
          type: 'done',
          usage: {
            promptTokens: response.usage?.input_tokens || 0,
            completionTokens: response.usage?.output_tokens || 0,
            totalTokens: (response.usage?.input_tokens || 0) + (response.usage?.output_tokens || 0),
          },
        }
        return
      }

      // Streaming response
      for await (const event of stream as any) {
        if (event.type === 'content_block_delta') {
          if (event.delta?.text) {
            yield {
              type: 'text',
              content: event.delta.text,
            }
          }
        } else if (event.type === 'message_delta') {
          if (event.delta?.stop_reason) {
            yield {
              type: 'done',
              usage: event.usage
                ? {
                    promptTokens: 0, // Not available in stream
                    completionTokens: event.usage.output_tokens || 0,
                    totalTokens: event.usage.output_tokens || 0,
                  }
                : undefined,
            }
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
    throw new Error('Cancellation not implemented')
  }

  private convertTools(tools: any[]): any[] {
    return tools.map((tool) => ({
      name: tool.name,
      description: tool.description,
      input_schema: {
        type: 'object',
        properties: tool.inputSchema,
      },
    }))
  }
}
