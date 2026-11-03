// Shared types used across main, renderer, and agent processes

export type Theme = 'dark' | 'light' | 'halloween' | 'system'

export type ExecutionMode = 'chat' | 'plan' | 'agent' | 'goal'

export type PermissionProfile = 'ask' | 'project' | 'full'

export interface User {
  id: string
  email: string
  createdAt: number
}

export interface Project {
  id: string
  accountId: string
  name: string
  icon?: string
  color?: string
  description?: string
  directories: ProjectDirectory[]
  createdAt: number
  updatedAt: number
}

export interface ProjectDirectory {
  id: string
  projectId: string
  deviceId: string
  path: string
}

export interface Chat {
  id: string
  accountId: string
  projectId?: string
  name: string
  pinned: boolean
  archived: boolean
  createdAt: number
  updatedAt: number
}

export interface Message {
  id: string
  chatId: string
  role: 'user' | 'assistant' | 'system'
  content: string
  createdAt: number
}

export interface ModelProvider {
  id: string
  name: string
  type: 'openai' | 'anthropic' | 'google' | 'azure' | 'xai' | 'openrouter' | 'groq' | 'mistral' | 'ollama' | 'lmstudio' | 'custom' | 'local'
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
  stream?: boolean
}

export interface Tool {
  name: string
  description: string
  inputSchema: Record<string, unknown>
}

export interface ToolCall {
  id: string
  name: string
  arguments: Record<string, unknown>
}

export interface ToolResult {
  id: string
  success: boolean
  output?: string
  error?: string
}

export interface GameState {
  gameId: string
  timestamp: number
  score: number
  data: Record<string, unknown>
}

export interface GameSettings {
  gameId: string
  soundEnabled: boolean
  difficulty?: 'easy' | 'medium' | 'hard'
  customSettings: Record<string, unknown>
}

export interface Automation {
  id: string
  accountId: string
  name: string
  deviceId: string
  type: 'one-time' | 'recurring'
  schedule: string
  timezone: string
  prompt: string
  modelId: string
  permissions: PermissionProfile
  projectId?: string
  enabled: boolean
  lastRun?: number
  nextRun: number
}
