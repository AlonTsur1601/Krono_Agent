import { z } from 'zod'

// IPC Message Schemas
export const AuthLoginSchema = z.object({
  email: z.string().email(),
  password: z.string().min(8),
})

export const AuthRegisterSchema = z.object({
  email: z.string().email(),
  password: z.string().min(8),
})

export const CreateChatSchema = z.object({
  projectId: z.string().optional(),
  name: z.string().optional(),
})

export const SendMessageSchema = z.object({
  chatId: z.string(),
  content: z.string(),
  mode: z.enum(['chat', 'plan', 'agent', 'goal']),
  modelId: z.string(),
  permissions: z.enum(['ask', 'project', 'full']),
})

export const FileOperationSchema = z.object({
  operation: z.enum(['read', 'write', 'delete', 'move', 'list']),
  path: z.string(),
  content: z.string().optional(),
  newPath: z.string().optional(),
})

export const TerminalCommandSchema = z.object({
  command: z.string(),
  cwd: z.string().optional(),
})

export const GameActionSchema = z.object({
  gameId: z.string(),
  action: z.enum(['launch', 'pause', 'resume', 'restart', 'save', 'load']),
  data: z.record(z.unknown()).optional(),
})

// Type exports
export type AuthLogin = z.infer<typeof AuthLoginSchema>
export type AuthRegister = z.infer<typeof AuthRegisterSchema>
export type CreateChat = z.infer<typeof CreateChatSchema>
export type SendMessage = z.infer<typeof SendMessageSchema>
export type FileOperation = z.infer<typeof FileOperationSchema>
export type TerminalCommand = z.infer<typeof TerminalCommandSchema>
export type GameAction = z.infer<typeof GameActionSchema>
