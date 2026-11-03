import type {
  ProviderAdapter,
  CompletionRequest,
  CompletionChunk,
  ToolDefinition,
  Message,
} from './types'
import { OpenAIProvider } from './providers/openai'
import { FILE_TOOLS } from './tools/files'

interface AgentConfig {
  mode: 'chat' | 'plan' | 'agent' | 'goal'
  permissions: 'ask' | 'project' | 'full'
  modelId: string
  providerId: string
  projectPath?: string
}

export class AgentRuntime {
  private providers: Map<string, ProviderAdapter> = new Map()
  private tools: Map<string, ToolDefinition> = new Map()

  constructor() {
    // Register default provider
    this.registerProvider(new OpenAIProvider())

    // Register tools
    this.registerTools(FILE_TOOLS)
  }

  registerProvider(provider: ProviderAdapter): void {
    this.providers.set(provider.id, provider)
  }

  registerTools(tools: ToolDefinition[]): void {
    for (const tool of tools) {
      this.tools.set(tool.name, tool)
    }
  }

  async configureProvider(providerId: string, apiKey: string): Promise<void> {
    const provider = this.providers.get(providerId)
    if (!provider) {
      throw new Error(`Provider ${providerId} not found`)
    }

    await provider.configure({ apiKey })
  }

  async *execute(
    messages: Message[],
    config: AgentConfig
  ): AsyncIterable<CompletionChunk> {
    const provider = this.providers.get(config.providerId)
    if (!provider) {
      throw new Error(`Provider ${config.providerId} not found`)
    }

    // Get available tools based on mode
    const availableTools = this.getToolsForMode(config.mode, config.permissions)

    // Build system prompt
    const systemPrompt = this.buildSystemPrompt(config)

    // Prepare messages
    const allMessages: Message[] = [
      { role: 'system', content: systemPrompt },
      ...messages,
    ]

    // Create completion request
    const request: CompletionRequest = {
      model: config.modelId,
      messages: allMessages,
      tools: availableTools.length > 0 ? this.convertToolsForProvider(availableTools) : undefined,
      stream: true,
    }

    // Stream response
    for await (const chunk of provider.createCompletion(request)) {
      yield chunk
    }
  }

  private buildSystemPrompt(config: AgentConfig): string {
    let prompt = 'You are Krono Agent, a helpful AI assistant.'

    switch (config.mode) {
      case 'chat':
        prompt += ' You can answer questions but cannot use any tools or perform actions.'
        break
      case 'plan':
        prompt +=
          ' You can read files and analyze code but cannot make any modifications.'
        break
      case 'agent':
        prompt +=
          ' You can use tools to read and write files, run commands, and perform actions within the authorized permission scope.'
        break
      case 'goal':
        prompt +=
          ' You are working on a persistent multi-step goal. Break down complex tasks and track progress.'
        break
    }

    switch (config.permissions) {
      case 'ask':
        prompt += ' Always ask for permission before taking any action.'
        break
      case 'project':
        prompt += ` You have permission to work within the project directory${
          config.projectPath ? ` at ${config.projectPath}` : ''
        }.`
        break
      case 'full':
        prompt += ' You have full computer access. Use it responsibly.'
        break
    }

    return prompt
  }

  private getToolsForMode(
    mode: AgentConfig['mode'],
    _permissions: AgentConfig['permissions']
  ): ToolDefinition[] {
    switch (mode) {
      case 'chat':
        // No tools in chat mode
        return []

      case 'plan':
        // Read-only tools
        return Array.from(this.tools.values()).filter(
          (tool) =>
            tool.name === 'read_file' ||
            tool.name === 'list_files'
        )

      case 'agent':
      case 'goal':
        // All tools available
        return Array.from(this.tools.values())

      default:
        return []
    }
  }

  private convertToolsForProvider(tools: ToolDefinition[]): Array<{
    name: string
    description: string
    inputSchema: Record<string, unknown>
  }> {
    return tools.map((tool) => ({
      name: tool.name,
      description: tool.description,
      inputSchema: {}, // Simplified for now
    }))
  }
}

// Singleton instance
export const agentRuntime = new AgentRuntime()
