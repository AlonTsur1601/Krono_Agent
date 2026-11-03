import { z } from 'zod'
import type { ZodTypeAny } from 'zod'
import type { ExecutionContext, ToolCall } from './types'
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
  signal?: AbortSignal
  maxToolCalls?: number
  requestApproval?: ExecutionContext['requestApproval']
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

  getProvider(id: string): ProviderAdapter | undefined { return this.providers.get(id) }

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

    let calls = 0
    const failures = new Map<string, number>()
    const deadline = Date.now() + 30 * 60 * 1000
    while (true) {
      if (config.signal?.aborted) throw new Error('Run cancelled')
      if (Date.now() > deadline) throw new Error('Run time budget exceeded')
      const pending: ToolCall[] = []
      for await (const chunk of provider.createCompletion({ ...request, signal: config.signal })) {
        if (config.signal?.aborted) throw new Error('Run cancelled')
        if (chunk.type === 'error') throw chunk.error || new Error('Provider request failed')
        if (chunk.type === 'tool_call' && chunk.toolCall) pending.push(chunk.toolCall)
        else if (chunk.type !== 'done') yield chunk
      }
      if (!pending.length) { yield { type: 'done' }; return }
      for (const call of pending) {
        if (++calls > (config.maxToolCalls ?? 100)) throw new Error('Tool-call budget exceeded')
        if (config.signal?.aborted) throw new Error('Run cancelled')
        const tool = availableTools.find(candidate => candidate.name === call.name)
        if (!tool) throw new Error('Model requested a tool unavailable in the current mode')
        const parsed = (tool.inputSchema as ZodTypeAny).parse(call.arguments)
        const context: ExecutionContext = {
          projectDirs: config.projectPath ? [config.projectPath] : [],
          permissions: config.permissions,
          requestApproval: config.requestApproval ?? (async () => ({ approved: false })),
          emitProgress: () => {},
        }
        const permission = tool.capabilities.requiresPermission
        if (permission !== 'none' && permission !== 'read' && config.permissions === 'ask') {
          const approval = await context.requestApproval({ id: call.id, action: call.name, target: String(call.arguments.path ?? ''), details: call.arguments })
          if (!approval.approved) throw new Error('Tool execution requires user approval')
          // Approval is bound to this one action, not future calls.
          context.requestApproval = async () => ({ approved: true })
        }
        yield { type: 'tool_call', toolCall: call }
        const result = await tool.execute(parsed, context)
        const step = call.name + JSON.stringify(call.arguments, Object.keys(call.arguments).sort())
        if (!result.success) {
          const count = (failures.get(step) ?? 0) + 1; failures.set(step, count)
          if (count >= 3) throw new Error('Stopped after three failures of the same tool action')
        } else failures.delete(step)
        request.messages.push({ role: 'assistant', content: '', toolCalls: [call] })
        request.messages.push({ role: 'tool', content: JSON.stringify(result), toolCallId: call.id })
      }
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
      inputSchema: z.toJSONSchema(tool.inputSchema as ZodTypeAny, { target: 'draft-7' })
    }))
  }
}

// Singleton instance
export const agentRuntime = new AgentRuntime()
