import { ipcMain, WebContents } from 'electron'
import { randomUUID } from 'crypto'
import { homedir } from 'os'
import { z } from 'zod'
import * as pty from 'node-pty'
import { dbManager } from '../db'

const sessions = new Map<string, { process: pty.IPty; owner: WebContents }>()
const IdSchema = z.string().uuid()
export function closeAllTerminals() {
  for (const session of sessions.values()) session.process.kill()
  sessions.clear()
}
function owned(id: string, sender: WebContents) {
  const session = sessions.get(IdSchema.parse(id))
  if (!session || session.owner !== sender) throw new Error('Terminal session not found')
  return session.process
}
export function setupTerminalHandlers() {
  ipcMain.handle('terminal:create', (event) => {
    dbManager.getDb() // User terminals require an authenticated account.
    if (sessions.size >= 8) throw new Error('Close a terminal before creating another one')
    const id = randomUUID()
    const shell = process.platform === 'win32' ? 'powershell.exe' : '/bin/sh'
    const child = pty.spawn(shell, process.platform === 'win32' ? ['-NoLogo'] : [], {
      name: 'xterm-256color', cols: 100, rows: 20, cwd: homedir(), env: process.env,
    })
    sessions.set(id, { process: child, owner: event.sender })
    child.onData(data => {
      if (!event.sender.isDestroyed()) event.sender.send('terminal:data', { id, data })
    })
    child.onExit(({ exitCode }) => {
      sessions.delete(id)
      if (!event.sender.isDestroyed()) event.sender.send('terminal:data', { id, data: `\r\nProcess exited (${exitCode})\r\n`, exited: true })
    })
    event.sender.once('destroyed', () => { if (sessions.has(id)) { child.kill(); sessions.delete(id) } })
    return { id }
  })
  ipcMain.handle('terminal:write', (event, input) => {
    const data = z.object({ id: IdSchema, data: z.string().max(65536) }).parse(input)
    owned(data.id, event.sender).write(data.data)
  })
  ipcMain.handle('terminal:resize', (event, input) => {
    const data = z.object({ id: IdSchema, cols: z.number().int().min(10).max(500), rows: z.number().int().min(2).max(200) }).parse(input)
    owned(data.id, event.sender).resize(data.cols, data.rows)
  })
  ipcMain.handle('terminal:close', (event, id) => { owned(id, event.sender).kill(); sessions.delete(id) })
}
