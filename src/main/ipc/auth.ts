import { closeBrowser } from './browser'
import { closeAllTerminals } from './terminal'
import { app, safeStorage } from 'electron'
import { readFileSync, existsSync, writeFileSync, unlinkSync } from 'fs'
import path from 'path'
import { parseAccountConfig } from '../account-config'
import { cancelAllRuns } from './chat'
import { ipcMain } from 'electron'
import { createClient, SupabaseClient, User } from '@supabase/supabase-js'
import { AuthLoginSchema, AuthRegisterSchema } from '../../shared/schemas'
import { dbManager } from '../db'

let client: SupabaseClient | undefined
function authClient() {
  const configFile = path.join(app.getPath('userData'), 'account-service.json')
  let input: unknown
  if (process.env.SUPABASE_URL && process.env.SUPABASE_ANON_KEY) {
    input = { url: process.env.SUPABASE_URL, anonKey: process.env.SUPABASE_ANON_KEY }
  } else if (existsSync(configFile)) {
    try { input = JSON.parse(readFileSync(configFile, 'utf8')) }
    catch { throw new Error('Cannot read account-service.json. Check the account setup guide.') }
  } else throw new Error('Account service is not configured. Follow the Supabase setup guide to create account-service.json or set SUPABASE_URL and SUPABASE_ANON_KEY.')
  const config = parseAccountConfig(input)
  client ??= createClient(config.url, config.anonKey, { auth: { persistSession: true, autoRefreshToken: true, storage: {
    getItem: () => {
      const file = path.join(app.getPath('userData'), 'session.bin')
      if (!existsSync(file)) return null
      try { return safeStorage.decryptString(readFileSync(file)) } catch { return null }
    },
    setItem: (_key: string, value: string) => {
      if (!safeStorage.isEncryptionAvailable() || (process.platform === 'linux' && safeStorage.getSelectedStorageBackend() === 'basic_text')) throw new Error('OS encryption is required to remember sign-in.')
      writeFileSync(path.join(app.getPath('userData'), 'session.bin'), safeStorage.encryptString(value), { mode: 0o600 })
    },
    removeItem: () => { const file = path.join(app.getPath('userData'), 'session.bin'); if (existsSync(file)) unlinkSync(file) },
  } } })
  return client
}
function openAccount(user: User) {
  const db = dbManager.open(user.id)
  db.prepare('INSERT INTO accounts (id, email, created_at) VALUES (?, ?, ?) ON CONFLICT(id) DO UPDATE SET email = excluded.email')
    .run(user.id, user.email, Date.now())
  return { user: { id: user.id, email: user.email } }
}
export function setupAuthHandlers() {
  ipcMain.handle('auth:restore', async () => {
    try {
      const auth = authClient().auth
      const { data: session } = await auth.getSession()
      if (!session.session) return { user: null }
      const { data, error } = await auth.getUser()
      if (error || !data.user) return { user: null }
      return openAccount(data.user)
    } catch { return { user: null } }
  })
  ipcMain.handle('auth:login', async (_event, input) => {
    const credentials = AuthLoginSchema.parse(input)
    const { data, error } = await authClient().auth.signInWithPassword(credentials)
    if (error) throw new Error(error.message)
    if (!data.user || !data.session) throw new Error('Sign-in did not establish an authenticated session.')
    return openAccount(data.user)
  })
  ipcMain.handle('auth:register', async (_event, input) => {
    const credentials = AuthRegisterSchema.parse(input)
    const { data, error } = await authClient().auth.signUp({ ...credentials, options: { emailRedirectTo: 'https://alontsur1601.github.io/Krono_Agent/' } })
    if (error) throw new Error(error.message)
    if (!data.user || !data.session) throw new Error('Check your email to confirm your account, then sign in.')
    return openAccount(data.user)
  })
  ipcMain.handle('auth:logout', async () => {
    cancelAllRuns()
    closeAllTerminals()
    closeBrowser()
    try { await client?.auth.signOut({ scope: 'local' }) }
    finally { const file = path.join(app.getPath('userData'), 'session.bin'); if (existsSync(file)) unlinkSync(file); dbManager.close() }
    return { success: true }
  })
}
