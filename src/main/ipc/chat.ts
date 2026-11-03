import { ipcMain } from 'electron'
import { SendMessageSchema, CreateChatSchema } from '../../shared/schemas'
import { dbManager } from '../db'
import { randomBytes } from 'crypto'

export function setupChatHandlers() {
  ipcMain.handle('chat:create', async (_event, data) => {
    try {
      const { projectId, name } = CreateChatSchema.parse(data)
      const db = dbManager.getDb()

      const chatId = randomBytes(16).toString('hex')
      const now = Date.now()

      // Get current account
      const account = db.prepare('SELECT id FROM accounts LIMIT 1').get() as any

      db.prepare(
        'INSERT INTO chats (id, account_id, project_id, name, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
      ).run(chatId, account.id, projectId || null, name || 'New Chat', now, now)

      return {
        id: chatId,
        name: name || 'New Chat',
        createdAt: now,
      }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to create chat')
    }
  })

  ipcMain.handle('chat:list', async () => {
    try {
      const db = dbManager.getDb()
      const chats = db
        .prepare('SELECT * FROM chats WHERE deleted_at IS NULL ORDER BY updated_at DESC')
        .all()

      return chats
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to list chats')
    }
  })

  ipcMain.handle('chat:get', async (_event, chatId: string) => {
    try {
      const db = dbManager.getDb()
      const chat = db.prepare('SELECT * FROM chats WHERE id = ?').get(chatId)

      if (!chat) {
        throw new Error('Chat not found')
      }

      const messages = db
        .prepare('SELECT * FROM messages WHERE chat_id = ? ORDER BY created_at ASC')
        .all(chatId)

      return {
        ...chat,
        messages,
      }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to get chat')
    }
  })

  ipcMain.handle('chat:send-message', async (_event, data) => {
    try {
      const { chatId, content, mode, modelId, permissions } = SendMessageSchema.parse(data)
      const db = dbManager.getDb()

      const messageId = randomBytes(16).toString('hex')
      const now = Date.now()

      // Store user message
      db.prepare(
        'INSERT INTO messages (id, chat_id, role, content, created_at) VALUES (?, ?, ?, ?, ?)'
      ).run(messageId, chatId, 'user', content, now)

      // Update chat timestamp
      db.prepare('UPDATE chats SET updated_at = ? WHERE id = ?').run(now, chatId)

      // TODO: Integrate with agent runtime
      // For now, return a placeholder response
      const assistantMessageId = randomBytes(16).toString('hex')
      const assistantContent = `[Agent response in ${mode} mode with ${permissions} permissions using ${modelId}]\n\nThis is a placeholder. Agent integration coming soon.`

      db.prepare(
        'INSERT INTO messages (id, chat_id, role, content, created_at) VALUES (?, ?, ?, ?, ?)'
      ).run(assistantMessageId, chatId, 'assistant', assistantContent, now + 1000)

      return {
        userMessage: {
          id: messageId,
          role: 'user',
          content,
          createdAt: now,
        },
        assistantMessage: {
          id: assistantMessageId,
          role: 'assistant',
          content: assistantContent,
          createdAt: now + 1000,
        },
      }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to send message')
    }
  })

  ipcMain.handle('chat:delete', async (_event, chatId: string) => {
    try {
      const db = dbManager.getDb()
      const now = Date.now()

      db.prepare('UPDATE chats SET deleted_at = ? WHERE id = ?').run(now, chatId)

      return { success: true }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to delete chat')
    }
  })
}
