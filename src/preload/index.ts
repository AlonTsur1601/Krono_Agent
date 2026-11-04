import { contextBridge, ipcRenderer } from 'electron'
const channels = new Set([
  'projects:list', 'projects:save', 'projects:pick-folders', 'projects:move-chat', 'memory:get-global', 'memory:set-global',
  'chat:cancel',
  'chat:approve',
  'browser:show', 'browser:bounds', 'browser:action',
  'terminal:create', 'terminal:write', 'terminal:resize', 'terminal:close',
  'window:action', 'auth:restore', 'auth:login', 'auth:register', 'auth:logout',
  'chat:create', 'chat:list', 'chat:get', 'chat:send-message', 'chat:delete',
  'settings:get-providers', 'settings:configure-provider', 'settings:get-models',
])
type Listener = (...args: any[]) => void
const listeners = new Map<Listener, (...args: any[]) => void>()
contextBridge.exposeInMainWorld('api', {
  invoke: (channel: string, data?: unknown) => {
    if (!channels.has(channel)) return Promise.reject(new Error('Unsupported IPC channel'))
    return ipcRenderer.invoke(channel, data)
  },
  on: (channel: string, callback: Listener) => {
    if (!['chat:stream-chunk', 'terminal:data', 'browser:state', 'chat:approval'].includes(channel)) throw new Error('Unsupported event')
    const listener = (_event: unknown, ...args: unknown[]) => callback(...args)
    listeners.set(callback, listener)
    ipcRenderer.on(channel, listener)
  },
  off: (channel: string, callback: Listener) => {
    const listener = listeners.get(callback)
    if (listener) ipcRenderer.off(channel, listener)
    listeners.delete(callback)
  },
})
declare global {
  interface Window {
    api: {
      invoke: (channel: string, data?: unknown) => Promise<any>
      on: (channel: string, callback: Listener) => void
      off: (channel: string, callback: Listener) => void
    }
  }
}
