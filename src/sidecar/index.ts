// Private stdio worker. No TCP listener and no renderer or Electron dependency.
import readline from 'node:readline'
import { randomUUID } from 'node:crypto'
import { AgentRuntime } from '../agent/runtime'
import { AnthropicProvider } from '../agent/providers/anthropic'
import { GoogleProvider } from '../agent/providers/google'
import { CompatibleProvider } from '../agent/providers/compatible'
import { z } from 'zod'
import * as pty from 'node-pty'

function createRuntime() {
  const runtime = new AgentRuntime()
  runtime.registerProvider(new AnthropicProvider())
  runtime.registerProvider(new GoogleProvider())
  for (const id of ['ollama', 'lmstudio', 'custom'] as const) runtime.registerProvider(new CompatibleProvider(id))
  return runtime
}
const active = new Map<string, AbortController>()
const approvals = new Map<string, (approved: boolean) => void>()
const terminals = new Map<string, pty.IPty>()
const send = (data: unknown) => process.stdout.write(JSON.stringify(data) + '\n')
const Request = z.object({ id: z.string(), method: z.enum(['models','test','run','cancel','approve','terminal:create','terminal:write','terminal:resize','terminal:close']), data: z.any() })
readline.createInterface({ input: process.stdin }).on('line', line => {
  void (async () => {
    let id = ''
    let cleanupRun: (() => void) | undefined
    try {
      const request = Request.parse(JSON.parse(line)); id = request.id
      const { method, data } = request
      if (method === 'terminal:create') {
        const terminal = pty.spawn('powershell.exe', ['-NoLogo'], { cols: 100, rows: 25, cwd: data.cwd || process.env.USERPROFILE, env: process.env })
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
      const provider = z.enum(['openai','anthropic','google','ollama','lmstudio','custom']).parse(data.providerId)
      const compatible = ['ollama','lmstudio','custom'].includes(provider)
      const runtime = createRuntime()
      const toolsEnabled = !compatible || data.toolsEnabled === true
      const controller = new AbortController()
      if (method === 'run') {
        active.set(id, controller)
        const deadline = setTimeout(() => controller.abort(), 30 * 60 * 1000)
        cleanupRun = () => { clearTimeout(deadline); controller.abort(); active.delete(id) }
      }
      await runtime.configureProvider(provider, compatible ? z.string().parse(data.apiKey) : z.string().min(1).parse(data.apiKey), compatible ? { endpoint: z.string().parse(data.endpoint), toolsEnabled } : {})
      if (method === 'models') { send({ id, result: await runtime.getProvider(provider)!.listModels() }); return }
      if (method === 'test') { send({ id, result: await runtime.getProvider(provider)!.testConnection() }); return }
      {
        const config = z.object({ mode: z.enum(['chat','plan','agent','goal']), permissions: z.enum(['ask','project','full']), modelId: z.string().min(1), projectDirs: z.array(z.string()), instructions: z.string(), memories: z.array(z.string()) }).parse(data)
        const messages = z.array(z.object({ role: z.enum(['user','assistant','system']), content: z.string() })).parse(data.messages)
        for await (const chunk of runtime.execute(messages, { ...config, toolsEnabled, providerId: provider, signal: controller.signal, requestApproval: async action => {
          const approvalId = randomUUID()
          return new Promise(resolve => {
            const finish = (approved: boolean) => { approvals.delete(approvalId); controller.signal.removeEventListener('abort', abort); resolve({ approved }) }
            const abort = () => finish(false)
            approvals.set(approvalId, finish); controller.signal.addEventListener('abort', abort, { once: true })
            if (controller.signal.aborted) { abort(); return }
            send({ id, event: 'approval', data: { ...action, id: approvalId } })
          })
        } })) send({ id, event: 'chunk', data: chunk })
        send({ id, result: true })
      }
    } catch (error) { send({ id, error: error instanceof Error ? error.message : 'Worker request failed' }) }
    finally { cleanupRun?.() }
  })()
})
process.stdin.on('end', () => { for (const controller of active.values()) controller.abort(); for (const terminal of terminals.values()) terminal.kill(); process.exit(0) })
