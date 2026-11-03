import { app, safeStorage } from 'electron'
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'fs'
import path from 'path'
import { dbManager } from './db'

function location(providerId: string) {
  if (!/^[a-z0-9-]+$/.test(providerId)) throw new Error('Invalid provider')
  const account = dbManager.getDb().prepare('SELECT id FROM accounts LIMIT 1').get() as { id: string }
  const dir = path.join(app.getPath('userData'), 'credentials', account.id)
  mkdirSync(dir, { recursive: true })
  return path.join(dir, `${providerId}.bin`)
}
export function saveCredential(providerId: string, secret: string) {
  if (!safeStorage.isEncryptionAvailable() || (process.platform === 'linux' && safeStorage.getSelectedStorageBackend() === 'basic_text')) {
    throw new Error('An OS-protected credential store is required.')
  }
  writeFileSync(location(providerId), safeStorage.encryptString(secret), { mode: 0o600 })
}
export function loadCredential(providerId: string): string | undefined {
  const file = location(providerId)
  if (!existsSync(file)) return undefined
  return safeStorage.decryptString(readFileSync(file))
}
