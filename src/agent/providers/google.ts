import { GoogleGenerativeAI } from '@google/generative-ai'
import type {
  ProviderAdapter,
  ProviderConfig,
  ConnectionTestResult,
  Model,
  ModelCapabilities,
  CompletionRequest,
  CompletionChunk,
} from '../types'

export class GoogleProvider implements ProviderAdapter {
  id = 'google'
  name = 'Google (Gemini)'
  private client: GoogleGenerativeAI | null = null
  private apiKey: string | null = null

  async configure(config: ProviderConfig): Promise<void> {
    this.apiKey = config.apiKey as string
    this.client = new GoogleGenerativeAI(this.apiKey)
  }

  async testConnection(): Promise<ConnectionTestResult> {
    if (!this.client) {
      return { success: false, error: 'Not configured' }
    }

    try {
      const start = Date.now()
      const model = this.client.getGenerativeModel({ model: 'gemini-pro' })
      await model.generateContent('test')
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
    return [
      {
        id: 'gemini-2.0-flash-exp',
        providerId: this.id,
        name: 'Gemini 2.0 Flash (Experimental)',
        capabilities: {
          supportsTools: true,
          supportsImages: true,
          supportsReasoningEffort: false,
          supportsStructuredOutput: true,
          contextWindow: 1000000,
          maxOutputTokens: 8192,
          streamingSupported: true,
        },
      },
      {
        id: 'gemini-1.5-pro',
        providerId: this.id,
        name: 'Gemini 1.5 Pro',
        capabilities: {
          supportsTools: true,
          supportsImages: true,
          supportsReasoningEffort: false,
          supportsStructuredOutput: true,
          contextWindow: 2000000,
          maxOutputTokens: 8192,
          streamingSupported: true,
        },
      },
      {
        id: 'gemini-1.5-flash',
        providerId: this.id,
        name: 'Gemini 1.5 Flash',
        capabilities: {
          supportsTools: true,
          supportsImages: true,
          supportsReasoningEffort: false,
          supportsStructuredOutput: true,
          contextWindow: 1000000,
          maxOutputTokens: 8192,
          streamingSupported: true,
        },
      },
      {
        id: 'gemini-pro',
        providerId: this.id,
        name: 'Gemini Pro',
        capabilities: {
          supportsTools: true,
          supportsImages: false,
          supportsReasoningEffort: false,
          supportsStructuredOutput: false,
          contextWindow: 30720,
          maxOutputTokens: 2048,
          streamingSupported: true,
        },
      },
    ]
  }

  getModelCapabilities(modelId: string): ModelCapabilities {
    const model = this.listModels().then((models) => models.find((m) => m.id === modelId))
    return {
      supportsTools: true,
      supportsImages: true,
      supportsReasoningEffort: false,
      supportsStructuredOutput: true,
      contextWindow: 1000000,
      maxOutputTokens: 8192,
      streamingSupported: true,
    }
  }

  async *createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk> {
    if (!this.client) throw new Error('Not configured')

    try {
      const model = this.client.getGenerativeModel({
        model: request.model,
        generationConfig: {
          temperature: request.temperature,
          maxOutputTokens: request.maxTokens,
        },
      })

      // Convert messages to Gemini format
      const contents = request.messages
        .filter((m) => m.role !== 'system')
        .map((m) => ({
          role: m.role === 'assistant' ? 'model' : 'user',
          parts: [{ text: m.content }],
        }))

      // Add system instruction if present
      const systemMessage = request.messages.find((m) => m.role === 'system')
      const systemInstruction = systemMessage
        ? { parts: [{ text: systemMessage.content }] }
        : undefined

      if (request.stream !== false) {
        const chat = model.startChat({
          history: contents.slice(0, -1),
        })

        const result = await chat.sendMessageStream(
          contents[contents.length - 1].parts[0].text
        )

        for await (const chunk of result.stream) {
          const text = chunk.text()
          if (text) {
            yield {
              type: 'text',
              content: text,
            }
          }
        }

        yield {
          type: 'done',
        }
      } else {
        const result = await model.generateContent(contents as any)

        const response = result.response
        yield {
          type: 'text',
          content: response.text(),
        }
        yield {
          type: 'done',
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
}
