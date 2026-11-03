// Agent execution types

export interface ProviderAdapter {
  id: string
  name: string
  
  // Configuration
  configure(config: ProviderConfig): Promise<void>
  testConnection(): Promise<ConnectionTestResult>
  
  // Models
  listModels(): Promise<Model[]>
  getModelCapabilities(modelId: string): ModelCapabilities
  
  // Inference
  createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk>
  cancelCompletion(requestId: string): Promise<void>
}

export interface ProviderConfig {
  apiKey?: string
  endpoint?: string
  [key: string]: unknown
}

export interface ConnectionTestResult {
  success: boolean
  error?: string
  latencyMs?: number
}

export interface Model {
  id: string
  providerId: string
  name: string
  capabilities: ModelCapabilities
}

export interface ModelCapabilities {
  supportsTools: boolean
  supportsImages: boolean
  supportsReasoningEffort: boolean
  supportsStructuredOutput: boolean
  contextWindow: number
  maxOutputTokens: number
  streamingSupported: boolean
}

export interface CompletionRequest {
  model: string
  messages: Message[]
  tools?: Tool[]
  temperature?: number
  maxTokens?: number
  reasoningEffort?: 'low' | 'medium' | 'high'
  signal?: AbortSignal
  stream?: boolean
}

export interface Message {
  role: 'user' | 'assistant' | 'system' | 'tool'
  content: string
  toolCallId?: string
  toolCalls?: ToolCall[]
}

export interface Tool {
  name: string
  description: string
  inputSchema: Record<string, unknown>
}

export interface CompletionChunk {
  type: 'text' | 'tool_call' | 'done' | 'error'
  content?: string
  toolCall?: ToolCall
  usage?: Usage
  error?: Error
}

export interface ToolCall {
  id: string
  name: string
  arguments: Record<string, unknown>
}

export interface Usage {
  promptTokens: number
  completionTokens: number
  totalTokens: number
}

export interface ToolDefinition {
  name: string
  description: string
  inputSchema: unknown // Zod schema
  capabilities: ToolCapabilities
  
  execute(input: unknown, context: ExecutionContext): Promise<ToolResult>
  cancel?(executionId: string): Promise<void>
}

export interface ToolCapabilities {
  requiresPermission: 'none' | 'read' | 'write' | 'execute' | 'desktop'
  canCancel: boolean
  longRunning: boolean
}

export interface ExecutionContext {
  projectDirs: string[]
  permissions: 'ask' | 'project' | 'full'
  requestApproval(action: ApprovalRequest): Promise<ApprovalResult>
  emitProgress(progress: Progress): void
}

export interface ApprovalRequest {
  id: string
  action: string
  target: string
  details: Record<string, unknown>
}

export interface ApprovalResult {
  approved: boolean
  applyToSimilar?: boolean
}

export interface Progress {
  message: string
  percentage?: number
}

export interface ToolResult {
  success: boolean
  output?: string
  error?: string
  artifacts?: Artifact[]
}

export interface Artifact {
  type: 'file' | 'image' | 'data'
  name: string
  content: string
  mimeType?: string
}
