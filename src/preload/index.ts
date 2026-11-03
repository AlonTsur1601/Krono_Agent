import { contextBridge, ipcRenderer } from 'electron'

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('api', {
  // IPC communication
  send: (channel: string, data: unknown) => {
    ipcRenderer.send(channel, data)
  },
  receive: (channel: string, func: (...args: unknown[]) => void) => {
    ipcRenderer.on(channel, (_event, ...args) => func(...args))
  },
  invoke: (channel: string, data?: unknown) => {
    return ipcRenderer.invoke(channel, data)
  },
})

// Type definitions for window.api
declare global {
  interface Window {
    api: {
      send: (channel: string, data: unknown) => void
      receive: (channel: string, func: (...args: unknown[]) => void) => void
      invoke: (channel: string, data?: unknown) => Promise<unknown>
    }
  }
}
