// Private stdio worker. No TCP listener and no renderer or Electron dependency.
import readline from 'node:readline'
import { randomUUID } from 'node:crypto'
import { join } from 'node:path'
import { AgentRuntime } from '../agent/runtime'
import { agentBrowserTool } from '../agent/tools/browser'
import type { ToolResult } from '../agent/types'
import { AnthropicProvider } from '../agent/providers/anthropic'
import { GoogleProvider } from '../agent/providers/google'
import { CompatibleProvider, COMPATIBLE_PROFILES, isCompatibleProvider, type CompatibleId } from '../agent/providers/compatible'
import { LocalProvider, stopLocalRuntime } from '../agent/providers/local'
import { PluginConfig, PluginConnect, connectPlugin, pluginGuidance, pluginTools, testPlugin } from '../agent/tools/plugins'
import { z } from 'zod'
import * as pty from 'node-pty'

function createRuntime() {
  const runtime = new AgentRuntime()
  runtime.registerProvider(new AnthropicProvider())
  runtime.registerProvider(new GoogleProvider())
  for (const id of Object.keys(COMPATIBLE_PROFILES) as CompatibleId[])
    runtime.registerProvider(id === 'local' ? new LocalProvider() : new CompatibleProvider(id))
  return runtime
}
const active = new Map<string, AbortController>()
const approvals = new Map<string, (approved: boolean) => void>()
const browserRequests = new Map<string, { run: string; finish: (result: ToolResult) => void }>()
const terminals = new Map<string, pty.IPty>()
const send = (data: unknown) => process.stdout.write(JSON.stringify(data) + '\n')
const Request = z.object({ id: z.string(), method: z.enum(['models','test','run','cancel','approve','plugin:connect','plugin:test','browser:result','terminal:create','terminal:write','terminal:resize','terminal:close']), data: z.any() })
readline.createInterface({ input: process.stdin }).on('line', line => {
  void (async () => {
    let id = ''
    let credential = ''
    let cleanupRun: (() => void) | undefined
    let text = ''
    let textTimer: ReturnType<typeof setTimeout> | undefined
    const flushText = () => {
      clearTimeout(textTimer); textTimer = undefined
      if (!text) return
      send({ id, event: 'chunk', data: { type: 'text', content: text } })
      text = ''
    }
    try {
      const request = Request.parse(JSON.parse(line)); id = request.id
      const { method, data } = request
      credential = typeof data?.apiKey === 'string' ? data.apiKey : ''
      const savePlugin = (pluginId: string, grantId: string, connection: unknown) =>
        send({ id, event: 'plugin:credentials', data: { pluginId, grantId, connection } })
      if (method === 'plugin:connect') {
        const plugin = PluginConnect.safeParse(data)
        if (!plugin.success) { send({ id, result: { success: false, message: 'This plugin does not have a supported sign-in.' } }); return }
        const connection = new AbortController()
        active.set(id, connection)
        const deadline = setTimeout(() => connection.abort(), 5 * 60_000)
        cleanupRun = () => { clearTimeout(deadline); connection.abort(); active.delete(id) }
        const result = await connectPlugin(plugin.data, connection.signal,
          url => send({ id, event: 'plugin:authorize', data: url }),
          text => send({ id, event: 'plugin:status', data: text }))
        send({ id, result }); return
      }
      if (method === 'plugin:test') {
        const plugin = PluginConfig.safeParse(data)
        send({ id, result: plugin.success ? await testPlugin(plugin.data, undefined, savePlugin) : { success: false, message: 'Connect this plugin before checking it.' } })
        return
      }
      if (method === 'terminal:create') {
        const terminal = pty.spawn(join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe'), ['-NoLogo'], { cols: 100, rows: 25, cwd: data.cwd || process.env.USERPROFILE, env: process.env })
        terminals.set(id, terminal)
        terminal.onData(content => send({ id, event: 'terminal', data: content }))
        terminal.onExit(() => { terminals.delete(id); send({ id, event: 'terminal-exit', data: true }) })
        send({ id, result: true }); return
      }
      if (method.startsWith('terminal:')) {
        const terminal = terminals.get(String(data.id)); if (!terminal) throw new Error('Terminal closed')
        if (method === 'terminal:write') terminal.write(z.string().parse(data.content))
        if (method === 'terminal:resize') terminal.resize(z.number().int().min(10).max(500).parse(data.cols), z.number().int().min(2).max(200).parse(data.rows))
        if (method === 'terminal:close') terminal.kill()
        send({ id, result: true }); return
      }
      if (method === 'approve') { approvals.get(String(data.id))?.(data.approved === true); return }
      if (method === 'cancel') { active.get(String(data.id))?.abort(); return }
      if (method === 'browser:result') {
        const reply = browserRequests.get(String(data.id))
        if (reply && reply.run === data.run) reply.finish(data.result)
        return
      }
      const provider = z.string().refine(id => ['openai','anthropic','google'].includes(id) || isCompatibleProvider(id), 'Unknown provider').parse(data.providerId)
      const compatible = isCompatibleProvider(provider)
      const runtime = createRuntime()
      const toolsEnabled = !compatible || data.toolsEnabled === true
      const controller = new AbortController()
      if (method === 'run') {
        active.set(id, controller)
        const deadline = setTimeout(() => controller.abort(), 30 * 60 * 1000)
        cleanupRun = () => { clearTimeout(deadline); controller.abort(); active.delete(id) }
        if (data.browserEnabled === true) runtime.registerTools([agentBrowserTool(input => new Promise(resolve => {
          const requestId = randomUUID()
          const finish = (result: ToolResult) => {
            clearTimeout(timeout)
            controller.signal.removeEventListener('abort', abort)
            browserRequests.delete(requestId)
            resolve(result)
          }
          const abort = () => finish({ success: false, error: 'Browser action cancelled.' })
          const timeout = setTimeout(() => finish({ success: false, error: 'Browser action timed out.' }), 50_000)
          browserRequests.set(requestId, { run: id, finish })
          controller.signal.addEventListener('abort', abort, { once: true })
          if (controller.signal.aborted) abort()
          else {
            flushText()
            send({ id, event: 'browser', data: { id: requestId, ...input } })
          }
        }))])
      }
      await runtime.configureProvider(provider, compatible ? z.string().parse(data.apiKey) : z.string().min(1).parse(data.apiKey), compatible ? { endpoint: z.string().parse(data.endpoint), toolsEnabled } : {})
      if (method === 'models') { send({ id, result: await runtime.getProvider(provider)!.listModels() }); return }
      if (method === 'test') { send({ id, result: await runtime.getProvider(provider)!.testConnection() }); return }
      {
        const config = z.object({ mode: z.enum(['chat','plan','agent','goal']), permissions: z.enum(['ask','project','full']), modelId: z.string().min(1), reasoningEffort: z.enum(['none','low','medium','high','xhigh','max']).optional(), fastMode: z.boolean().optional(), projectDirs: z.array(z.string()), instructions: z.string(), memories: z.array(z.string()) }).parse(data)
        const plugins = z.array(PluginConfig).max(40).safeParse(data.plugins ?? [])
        if (!plugins.success) throw new Error('Check the enabled plugins in Settings and try again.')
        const guidance = pluginGuidance(plugins.data)
        if (guidance) config.instructions = [config.instructions, 'Installed skill packs (apply only when relevant):\n' + guidance].filter(Boolean).join('\n\n')
        if (toolsEnabled && (config.mode === 'agent' || config.mode === 'goal'))
          runtime.registerTools(pluginTools(plugins.data, savePlugin))
        const messages = z.array(z.object({ role: z.enum(['user','assistant','system']), content: z.string(), imagePng: z.string().max(5_592_408).regex(/^iVBORw0KGgo[A-Za-z0-9+/]*={0,2}$/).optional() })).parse(data.messages)
        if (messages.some(message => message.imagePng && (provider !== 'openai' || message.role !== 'user')))
          throw new Error('Pasted images require an OpenAI model that accepts images.')
        for await (const chunk of runtime.execute(messages, { ...config, toolsEnabled, providerId: provider, signal: controller.signal,
          emitFileChange: change => { flushText(); send({ id, event: 'file-change', data: change }) },
          emitProgress: progress => {
            if (!progress.terminalPhase) return
            flushText()
            send({ id, event: 'terminal-agent', data: { phase: progress.terminalPhase, message: progress.message } })
          }, requestApproval: async action => {
          flushText()
          const approvalId = randomUUID()
          return new Promise(resolve => {
            const finish = (approved: boolean) => { approvals.delete(approvalId); controller.signal.removeEventListener('abort', abort); resolve({ approved }) }
            const abort = () => finish(false)
            approvals.set(approvalId, finish); controller.signal.addEventListener('abort', abort, { once: true })
            if (controller.signal.aborted) { abort(); return }
            send({ id, event: 'approval', data: { ...action, id: approvalId } })
          })
        } })) {
          if (chunk.type === 'text') {
            text += chunk.content ?? ''
            if (text.length >= 8192) flushText()
            else textTimer ??= setTimeout(flushText, 16)
          } else {
            flushText()
            send({ id, event: 'chunk', data: chunk })
          }
        }
        flushText()
        send({ id, result: true })
      }
    } catch (error) { flushText(); const message = error instanceof Error ? error.message : 'Worker request failed'; send({ id, error: credential ? message.split(credential).join('[redacted]') : message }) }
    finally { cleanupRun?.() }
  })()
})
process.stdin.on('end', () => { for (const controller of active.values()) controller.abort(); for (const terminal of terminals.values()) terminal.kill(); stopLocalRuntime(); process.exit(0) })
