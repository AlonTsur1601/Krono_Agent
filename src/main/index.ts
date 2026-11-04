import { setupProjectHandlers } from './ipc/projects'
import { setupApprovalHandlers } from './approvals'
import { setupBrowserHandlers, closeBrowser } from './ipc/browser'
import { setupTerminalHandlers, closeAllTerminals } from './ipc/terminal'
import { app, BrowserWindow, ipcMain, Menu, nativeImage } from 'electron'
import path from 'path'
import { setupAuthHandlers } from './ipc/auth'
import { setupChatHandlers, cancelAllRuns } from './ipc/chat'
import { setupSettingsHandlers } from './ipc/settings'

let mainWindow: BrowserWindow | null = null

function createWindow() {
  mainWindow = new BrowserWindow({
    titleBarStyle: 'hidden',
    frame: process.platform === 'darwin',
    title: 'Krono Agent',
    icon: nativeImage.createFromPath(path.join(app.getAppPath(), 'assets/logo/icon.png')),
    width: 1280,
    height: 800,
    minWidth: 1024,
    minHeight: 768,
    webPreferences: {
      preload: path.join(__dirname, '../preload/index.js'),
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: true,
    },
    show: false,
  })

  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
  mainWindow.webContents.on('will-navigate', (event) => { event.preventDefault() })

  // Load the app
  if (process.env.NODE_ENV === 'development') {
    mainWindow.loadURL('http://localhost:5173')
    mainWindow.webContents.openDevTools()
  } else {
    mainWindow.loadFile(path.join(__dirname, '../renderer/index.html'))
  }

  mainWindow.once('ready-to-show', () => {
    mainWindow?.show()
  })

  mainWindow.on('closed', () => {
    mainWindow = null
  })
}

app.whenReady().then(() => {
  Menu.setApplicationMenu(null)
  ipcMain.handle('window:action', (event, action) => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win) return
    if (action === 'minimize') win.minimize()
    else if (action === 'maximize') { if (win.isMaximized()) win.unmaximize(); else win.maximize() }
    else if (action === 'close') win.close()
    else if (action === 'menu') Menu.buildFromTemplate([
      { label: 'File', submenu: [{ role: 'close' }, { role: 'quit' }] },
      { label: 'Edit', submenu: [{ role: 'undo' }, { role: 'redo' }, { type: 'separator' }, { role: 'cut' }, { role: 'copy' }, { role: 'paste' }, { role: 'selectAll' }] },
      { label: 'View', submenu: [{ role: 'reload' }, { role: 'toggleDevTools' }, { role: 'resetZoom' }, { role: 'zoomIn' }, { role: 'zoomOut' }, { role: 'togglefullscreen' }] },
    ]).popup({ window: win })
  })
  // Setup IPC handlers
  setupAuthHandlers()
  setupChatHandlers()
  setupSettingsHandlers()
  setupTerminalHandlers()
  setupBrowserHandlers()
  setupApprovalHandlers()
  setupProjectHandlers()
  
  createWindow()

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow()
    }
  })
})

app.on('before-quit', () => { cancelAllRuns(); closeAllTerminals(); closeBrowser() })
app.on('window-all-closed', () => {
  closeAllTerminals()
  if (process.platform !== 'darwin') {
    app.quit()
  }
})
