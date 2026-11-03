import { setupTerminalHandlers, closeAllTerminals } from './ipc/terminal'
import { app, BrowserWindow } from 'electron'
import path from 'path'
import { setupAuthHandlers } from './ipc/auth'
import { setupChatHandlers } from './ipc/chat'
import { setupSettingsHandlers } from './ipc/settings'

let mainWindow: BrowserWindow | null = null

function createWindow() {
  mainWindow = new BrowserWindow({
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
  // Setup IPC handlers
  setupAuthHandlers()
  setupChatHandlers()
  setupSettingsHandlers()
  setupTerminalHandlers()
  
  createWindow()

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow()
    }
  })
})

app.on('before-quit', closeAllTerminals)
app.on('window-all-closed', () => {
  closeAllTerminals()
  if (process.platform !== 'darwin') {
    app.quit()
  }
})
