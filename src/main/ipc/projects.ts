import { ipcMain, dialog, BrowserWindow } from 'electron'
import { randomUUID } from 'crypto'
import { realpathSync, statSync } from 'fs'
import { z } from 'zod'
import { dbManager } from '../db'

const ProjectSchema = z.object({ id: z.string().optional(), name: z.string().trim().min(1).max(100), directories: z.array(z.string().min(1)).max(12), instructions: z.string().max(50000), memory: z.string().max(50000), permissions: z.enum(['ask','project','full']) })
export function projectContext(projectId: string | null) {
  const db = dbManager.getDb()
  const settings = projectId ? db.prepare('SELECT value FROM settings WHERE key = ?').get(`project.${projectId}`) as { value: string } | undefined : undefined
  const config = settings ? JSON.parse(settings.value) : { instructions: '', permissions: 'ask' }
  const directories = projectId ? (db.prepare('SELECT path FROM project_directories WHERE project_id = ?').all(projectId) as { path: string }[]).map(row => row.path) : []
  const memories = (db.prepare('SELECT content FROM memories WHERE deleted_at IS NULL AND (project_id IS NULL OR project_id = ?)').all(projectId) as { content: string }[]).map(row => row.content)
  return { ...config, directories, memories }
}
export function setupProjectHandlers() {
  ipcMain.handle('projects:list', () => {
    const db = dbManager.getDb()
    return (db.prepare('SELECT id,name FROM projects WHERE deleted_at IS NULL ORDER BY name').all() as { id: string; name: string }[]).map(project => {
      const context = projectContext(project.id)
      const memory = db.prepare('SELECT content FROM memories WHERE id = ?').get(`project:${project.id}`) as { content: string } | undefined
      return { ...project, ...context, memory: memory?.content ?? '' }
    })
  })
  ipcMain.handle('projects:pick-folders', async event => {
    dbManager.getDb()
    const window = BrowserWindow.fromWebContents(event.sender)
    if (!window) throw new Error('Window not found')
    const result = await dialog.showOpenDialog(window, { title: 'Project folders', properties: ['openDirectory','multiSelections'] })
    return result.canceled ? [] : result.filePaths
  })
  ipcMain.handle('projects:save', (_event, input) => {
    const data = ProjectSchema.parse(input), db = dbManager.getDb()
    const directories = [...new Set(data.directories.map(directory => { const canonical = realpathSync(directory); if (!statSync(canonical).isDirectory()) throw new Error('Project path must be a directory'); return canonical }))]
    const account = db.prepare('SELECT id FROM accounts LIMIT 1').get() as { id: string }
    const id = data.id ?? randomUUID(), now = Date.now()
    if (data.id && !db.prepare('SELECT id FROM projects WHERE id = ? AND deleted_at IS NULL').get(id)) throw new Error('Project not found')
    db.transaction(() => {
      db.prepare('INSERT INTO projects(id,account_id,name,created_at,updated_at) VALUES (?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET name=excluded.name,updated_at=excluded.updated_at,version=projects.version+1').run(id,account.id,data.name,now,now)
      db.prepare('DELETE FROM project_directories WHERE project_id = ?').run(id)
      for (const directory of directories) db.prepare('INSERT INTO project_directories(id,project_id,device_id,path) VALUES (?,?,?,?)').run(randomUUID(),id,'local',directory)
      db.prepare('INSERT INTO settings(key,value) VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value').run(`project.${id}`,JSON.stringify({ instructions: data.instructions, permissions: data.permissions }))
      db.prepare('INSERT INTO memories(id,account_id,project_id,content,created_at,updated_at) VALUES (?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET content=excluded.content,updated_at=excluded.updated_at,version=memories.version+1').run(`project:${id}`,account.id,id,data.memory,now,now)
    })()
    return { id }
  })
  ipcMain.handle('projects:move-chat', (_event, input) => {
    const data = z.object({ chatId: z.string(), projectId: z.string().nullable() }).parse(input), db = dbManager.getDb()
    if (data.projectId && !db.prepare('SELECT id FROM projects WHERE id = ? AND deleted_at IS NULL').get(data.projectId)) throw new Error('Project not found')
    db.prepare('UPDATE chats SET project_id=?,updated_at=?,version=version+1 WHERE id=? AND deleted_at IS NULL').run(data.projectId,Date.now(),data.chatId)
  })
  ipcMain.handle('memory:get-global', () => (dbManager.getDb().prepare("SELECT content FROM memories WHERE id='global'").get() as { content: string } | undefined)?.content ?? '')
  ipcMain.handle('memory:set-global', (_event, input) => {
    const content = z.string().max(50000).parse(input), db = dbManager.getDb()
    const account = db.prepare('SELECT id FROM accounts LIMIT 1').get() as { id: string }
    db.prepare("INSERT INTO memories(id,account_id,content,created_at,updated_at) VALUES ('global',?,?,?,?) ON CONFLICT(id) DO UPDATE SET content=excluded.content,updated_at=excluded.updated_at,version=memories.version+1").run(account.id,content,Date.now(),Date.now())
  })
}
