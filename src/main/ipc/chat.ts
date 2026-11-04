import { projectContext } from './projects'
import { requestApproval } from '../approvals'
import { loadCredential } from '../credentials'
import { ipcMain, BrowserWindow } from 'electron'
import { SendMessageSchema, CreateChatSchema } from '../../shared/schemas'
import { dbManager } from '../db'
import { randomBytes } from 'crypto'
import { agentRuntime } from '../../agent/runtime'

const activeRuns = new Map<string, AbortController>()
export function cancelAllRuns() { for (const run of activeRuns.values()) run.abort(); activeRuns.clear() }

export function setupChatHandlers() {
  ipcMain.handle('chat:cancel', (_event, id) => { activeRuns.get(String(id))?.abort(); return { success: true } })
  ipcMain.handle('chat:create', async (_event, data) => {
    try {
      const { projectId, name } = CreateChatSchema.parse(data)
      const db = dbManager.getDb()

      if (projectId && !db.prepare('SELECT id FROM projects WHERE id = ? AND deleted_at IS NULL').get(projectId)) throw new Error('Project not found')
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
        projectId: projectId ?? null,
        createdAt: now,
        updatedAt: now,
      }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to create chat')
    }
  })

  ipcMain.handle('chat:list', async () => {
    try {
      const db = dbManager.getDb()
      const chats = db
        .prepare('SELECT id,name,project_id AS projectId,created_at AS createdAt,updated_at AS updatedAt FROM chats WHERE deleted_at IS NULL ORDER BY updated_at DESC')
        .all()

      return chats
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to list chats')
    }
  })

  ipcMain.handle('chat:get', async (_event, chatId: string) => {
    try {
      const db = dbManager.getDb()
      const chat = db.prepare('SELECT id,name,project_id AS projectId,created_at AS createdAt,updated_at AS updatedAt FROM chats WHERE id = ? AND deleted_at IS NULL').get(chatId)

      if (!chat) {
        throw new Error('Chat not found')
      }

      const messages = db
        .prepare('SELECT id,role,content,created_at AS createdAt FROM messages WHERE chat_id = ? AND deleted_at IS NULL ORDER BY created_at ASC')
        .all(chatId)

      return {
        ...chat,
        messages,
      }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to get chat')
    }
  })

  ipcMain.handle('chat:send-message', async (event, data) => {
    try {
      const { chatId, content, mode, modelId, permissions, providerId } = SendMessageSchema.parse(data)
      const db = dbManager.getDb()
      if (activeRuns.has(chatId)) throw new Error('This chat already has an active run')
      const chat = db.prepare('SELECT id,project_id FROM chats WHERE id = ? AND deleted_at IS NULL').get(chatId) as { id: string; project_id: string | null } | undefined
      if (!chat) throw new Error('Chat not found')

      const messageId = randomBytes(16).toString('hex')
      const now = Date.now()

      // Store user message
      db.prepare(
        'INSERT INTO messages (id, chat_id, role, content, created_at) VALUES (?, ?, ?, ?, ?)'
      ).run(messageId, chatId, 'user', content, now)

      // Update chat timestamp
      db.prepare('UPDATE chats SET updated_at = ? WHERE id = ?').run(now, chatId)

      // Get chat history
      const messages = db
        .prepare('SELECT role, content FROM messages WHERE chat_id = ? ORDER BY created_at ASC')
        .all(chatId) as Array<{ role: string; content: string }>

      // Configure OpenAI provider (in production, get API key from settings)
      const apiKey = loadCredential(providerId) || ''
      if (!apiKey) throw new Error('Configure this provider in Settings before sending a message.')
      if (apiKey) {
        await agentRuntime.configureProvider(providerId, apiKey)
      }

      const controller = new AbortController()
      activeRuns.set(chatId, controller)
      const abort = () => controller.abort()
      event.sender.once('destroyed', abort)
      const deadline = setTimeout(abort, 30 * 60 * 1000)
      // Start streaming response
      const assistantMessageId = randomBytes(16).toString('hex')
      let fullContent = ''

      try {
        // Execute agent
        for await (const chunk of agentRuntime.execute(messages as any, {
          mode,
          projectDirs: projectContext(chat.project_id).directories,
          instructions: projectContext(chat.project_id).instructions,
          memories: projectContext(chat.project_id).memories,
          permissions,
          modelId,
          providerId,
          signal: controller.signal,
          requestApproval: request => requestApproval(event.sender, chatId, request, controller.signal),
        })) {
          // Send chunk to renderer
          const window = BrowserWindow.fromWebContents(event.sender)
          if (window) {
            window.webContents.send('chat:stream-chunk', {
              chatId,
              messageId: assistantMessageId,
              chunk,
            })
          }

          // Accumulate text content
          if (chunk.type === 'text') {
            fullContent += chunk.content
          }
        }

        // Store complete assistant message
        db.prepare(
          'INSERT INTO messages (id, chat_id, role, content, created_at) VALUES (?, ?, ?, ?, ?)'
        ).run(assistantMessageId, chatId, 'assistant', fullContent, Date.now())

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
            content: fullContent,
            createdAt: Date.now(),
          },
        }
      } catch (error) {
        if (controller.signal.aborted) throw new Error('Run cancelled')
        const errorContent = `Error: ${error instanceof Error ? error.message : 'Unknown error'}`
        
        // Store error message
        db.prepare(
          'INSERT INTO messages (id, chat_id, role, content, created_at) VALUES (?, ?, ?, ?, ?)'
        ).run(assistantMessageId, chatId, 'assistant', errorContent, Date.now())

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
            content: errorContent,
            createdAt: Date.now(),
          },
        }
      } finally { clearTimeout(deadline); event.sender.removeListener('destroyed', abort); activeRuns.delete(chatId) }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to send message')
    }
  })

  ipcMain.handle('chat:delete', async (_event, chatId: string) => {
    try {
      const db = dbManager.getDb()
      const now = Date.now()

      activeRuns.get(chatId)?.abort()
      db.prepare('UPDATE chats SET deleted_at = ? WHERE id = ?').run(now, chatId)

      return { success: true }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Failed to delete chat')
    }
  })
}
