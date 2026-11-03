import { ipcMain } from 'electron'
import { AuthLoginSchema, AuthRegisterSchema } from '../../shared/schemas'
import { dbManager } from '../db'
import { randomBytes } from 'crypto'

export function setupAuthHandlers() {
  ipcMain.handle('auth:login', async (_event, data) => {
    try {
      const { email, password } = AuthLoginSchema.parse(data)
      
      // TODO: Implement actual Supabase authentication
      // For now, create a mock user for development
      const userId = randomBytes(16).toString('hex')
      
      // Open database for this user
      const db = dbManager.open(userId)
      
      // Check if account exists
      const existingAccount = db.prepare('SELECT * FROM accounts WHERE email = ?').get(email)
      
      if (!existingAccount) {
        throw new Error('Account not found')
      }
      
      return {
        user: {
          id: (existingAccount as any).id,
          email: (existingAccount as any).email,
        },
      }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Login failed')
    }
  })

  ipcMain.handle('auth:register', async (_event, data) => {
    try {
      const { email, password } = AuthRegisterSchema.parse(data)
      
      // TODO: Implement actual Supabase authentication
      // For now, create a mock user for development
      const userId = randomBytes(16).toString('hex')
      
      // Open database for this user
      const db = dbManager.open(userId)
      
      // Create account
      const now = Date.now()
      db.prepare('INSERT INTO accounts (id, email, created_at) VALUES (?, ?, ?)')
        .run(userId, email, now)
      
      return {
        user: {
          id: userId,
          email,
        },
      }
    } catch (error) {
      throw new Error(error instanceof Error ? error.message : 'Registration failed')
    }
  })

  ipcMain.handle('auth:logout', async () => {
    dbManager.close()
    return { success: true }
  })
}
