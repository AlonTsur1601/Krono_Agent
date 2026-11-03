import { BrowserWindow, ipcMain, WebContentsView, WebContents, dialog, Session } from 'electron'
import { randomUUID } from 'crypto'
import { z } from 'zod'
import { dbManager } from '../db'

interface Tab { id: string; view: WebContentsView; privateMode: boolean }
interface Entry { url: string; title: string; at: number }
const sessions = new WeakSet<Session>()
let downloads: { url: string; name: string; state: string; received: number; total: number }[] = []
function library(key: 'bookmarks' | 'history'): Entry[] {
  const row = dbManager.getDb().prepare('SELECT value FROM settings WHERE key = ?').get('browser.' + key) as { value: string } | undefined
  return row ? JSON.parse(row.value) : []
}
function saveLibrary(key: 'bookmarks' | 'history', entries: Entry[]) {
  dbManager.getDb().prepare('INSERT INTO settings(key,value) VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value').run('browser.' + key, JSON.stringify(entries.slice(0,200)))
}
let tabs: Tab[] = [], activeId: string | undefined, owner: WebContents | undefined
let shown = false
let bounds = { x: 248, y: 60, width: 800, height: 600 }
function state() {
  return { activeId, downloads, tabs: tabs.map(({ id, view, privateMode }) => ({ id, privateMode, title: view.webContents.getTitle() || 'New tab', url: view.webContents.getURL(), loading: view.webContents.isLoading(), canGoBack: view.webContents.navigationHistory.canGoBack(), canGoForward: view.webContents.navigationHistory.canGoForward() })) }
}
function publish() { if (owner && !owner.isDestroyed()) owner.send('browser:state', state()) }
function render() {
  const window = owner && BrowserWindow.fromWebContents(owner)
  if (!window) return
  for (const tab of tabs) {
    window.contentView.removeChildView(tab.view)
    if (shown && tab.id === activeId) { window.contentView.addChildView(tab.view); tab.view.setBounds(bounds) }
  }
  publish()
}
export function closeBrowser() {
  shown = false
  for (const tab of tabs) {
    const window = owner && !owner.isDestroyed() && BrowserWindow.fromWebContents(owner)
    if (window) window.contentView.removeChildView(tab.view)
    tab.view.webContents.close()
  }
  tabs = []; downloads = []; activeId = undefined; owner = undefined
}
function authorize(sender: WebContents) {
  dbManager.getDb()
  if (owner && owner !== sender) throw new Error('Browser belongs to another window')
  if (!owner) { owner = sender; sender.once('destroyed', closeBrowser) }
}
function allowedUrl(value: string) {
  const url = new URL(value)
  if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password) throw new Error('Enter an HTTP or HTTPS URL')
  return url.href
}
function createTab(privateMode = false) {
  if (tabs.length >= 12) throw new Error('Close a tab before opening another')
  const account = dbManager.getDb().prepare('SELECT id FROM accounts LIMIT 1').get() as { id: string }
  const view = new WebContentsView({ webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false, partition: privateMode ? `private-${randomUUID()}` : `persist:browser-${account.id}` } })
  const tab = { id: randomUUID(), view, privateMode }; tabs.push(tab); activeId = tab.id
  const session = view.webContents.session
  if (!sessions.has(session)) {
    sessions.add(session)
    session.setPermissionRequestHandler((wc, permission, callback) => {
      const window = owner && !owner.isDestroyed() && BrowserWindow.fromWebContents(owner)
      if (!window || !tabs.some(tab => tab.view.webContents === wc)) { callback(false); return }
      const origin = new URL(wc.getURL()).origin
      void dialog.showMessageBox(window, { type: 'question', title: 'Website permission', message: `${origin} requests ${permission}`, buttons: ['Deny', 'Allow once'], defaultId: 0, cancelId: 0 }).then(result => callback(result.response === 1)).catch(() => callback(false))
    })
    session.on('will-download', (_event, item) => {
      const entry = { url: item.getURL(), name: item.getFilename(), state: 'choosing destination', received: 0, total: item.getTotalBytes() }
      downloads = [entry, ...downloads].slice(0,20)
      item.on('updated', (_event, status) => { entry.state = status; entry.received = item.getReceivedBytes(); publish() })
      item.once('done', (_event, status) => { entry.state = status; publish() })
      publish()
    })
  }
  view.webContents.setWindowOpenHandler(({ url }) => {
    try { const target = allowedUrl(url); const popup = createTab(privateMode); void popup.view.webContents.loadURL(target).catch(() => publish()) } catch { /* unsupported navigation */ }
    return { action: 'deny' }
  })
  view.webContents.on('will-navigate', (event, url) => { try { allowedUrl(url) } catch { event.preventDefault() } })
  view.webContents.on('did-navigate', () => {
    if (!privateMode) {
      const url = view.webContents.getURL()
      if (/^https?:/.test(url)) saveLibrary('history', [{ url, title: view.webContents.getTitle() || url, at: Date.now() }, ...library('history').filter(entry => entry.url !== url)])
    }
    publish()
  })
  view.webContents.on('did-navigate-in-page', publish)
  view.webContents.on('page-title-updated', publish)
  view.webContents.on('did-start-loading', publish)
  view.webContents.on('did-stop-loading', publish)
  render(); return tab
}
export function setupBrowserHandlers() {
  ipcMain.handle('browser:show', (event, visible) => {
    authorize(event.sender); shown = z.boolean().parse(visible)
    if (shown && !tabs.length) createTab()
    render(); return state()
  })
  ipcMain.handle('browser:bounds', (event, input) => {
    authorize(event.sender)
    const rect = z.object({ x: z.number().int().min(0), y: z.number().int().min(0), width: z.number().int().min(1), height: z.number().int().min(1) }).parse(input)
    const size = BrowserWindow.fromWebContents(event.sender)!.getContentBounds()
    bounds = { x: Math.min(rect.x, size.width - 1), y: Math.min(rect.y, size.height - 1), width: Math.max(1, Math.min(rect.width, size.width - rect.x)), height: Math.max(1, Math.min(rect.height, size.height - rect.y)) }
    render()
  })
  ipcMain.handle('browser:action', async (event, input) => {
    authorize(event.sender)
    const action = z.object({ action: z.enum(['new','select','close','navigate','back','forward','reload','stop','zoom','find','bookmark','library','clear-history']), id: z.string().optional(), url: z.string().optional(), privateMode: z.boolean().optional(), text: z.string().max(500).optional(), zoom: z.number().min(0.5).max(2).optional() }).parse(input)
    if (action.action === 'library') return { bookmarks: library('bookmarks'), history: library('history') }
    if (action.action === 'clear-history') { saveLibrary('history', []); return state() }
    if (action.action === 'new') { createTab(action.privateMode); return state() }
    const tab = tabs.find(tab => tab.id === (action.id ?? activeId))
    if (!tab) throw new Error('Tab not found')
    switch (action.action) {
      case 'find': if (action.text) tab.view.webContents.findInPage(action.text); else tab.view.webContents.stopFindInPage('clearSelection'); break
      case 'bookmark': {
        const url = allowedUrl(tab.view.webContents.getURL())
        saveLibrary('bookmarks', [{ url, title: tab.view.webContents.getTitle() || url, at: Date.now() }, ...library('bookmarks').filter(entry => entry.url !== url)]); break
      }
      case 'select': activeId = tab.id; break
      case 'close': {
        BrowserWindow.fromWebContents(event.sender)?.contentView.removeChildView(tab.view)
        tabs = tabs.filter(candidate => candidate !== tab); tab.view.webContents.close()
        if (activeId === tab.id) activeId = tabs.at(-1)?.id
        if (!tabs.length) createTab()
        break
      }
      case 'navigate':
        try { await tab.view.webContents.loadURL(allowedUrl(action.url ?? '')) }
        catch (error) { if ((error as { code?: string }).code !== 'ERR_ABORTED' && !downloads.some(item => item.url === action.url)) throw error }
        break
      case 'back': if (tab.view.webContents.navigationHistory.canGoBack()) tab.view.webContents.navigationHistory.goBack(); break
      case 'forward': if (tab.view.webContents.navigationHistory.canGoForward()) tab.view.webContents.navigationHistory.goForward(); break
      case 'reload': tab.view.webContents.reload(); break
      case 'stop': tab.view.webContents.stop(); break
      case 'zoom': tab.view.webContents.setZoomFactor(action.zoom ?? 1); break
    }
    render(); return state()
  })
}
