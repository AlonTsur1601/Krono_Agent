import { spawn } from 'node:child_process'
import { realpath, stat } from 'node:fs/promises'
import { homedir } from 'node:os'
import { join, resolve } from 'node:path'
import { StringDecoder } from 'node:string_decoder'
import { randomUUID } from 'node:crypto'
import { z } from 'zod'
import type { ExecutionContext, ToolDefinition, ToolResult } from '../types'

export const sensitiveVariable = /(?:^|_)(?:TOKENS?|SECRETS?|PASSWORD|PASSWD|APIKEY|KEY|CREDENTIALS?|PAT)(?:_|$)/i
const Input = z.object({ command: z.string().min(1).max(16000).refine(value => !value.includes('\0')), cwd: z.string().optional(), timeout_ms: z.number().int().min(100).max(300000).default(120000) })
const WINDOW = 4096

// Retain overlap around both display windows, so clipping cannot reveal half
// of a known credential that happens to cross a truncation boundary.
class Capture {
  head = ''; tail = ''; length = 0
  readonly decoder = new StringDecoder('utf8')
  readonly capacity: number
  constructor(readonly secrets: readonly string[]) { this.capacity = WINDOW + Math.max(2, ...secrets.map(value => value.length)) }
  write(buffer: Buffer) { this.append(this.decoder.write(buffer)) }
  append(value: string) { this.length += value.length; this.head = (this.head + value).slice(0, this.capacity); this.tail = (this.tail + value).slice(-this.capacity) }
  mask(text: string, start: number, end: number) {
    const ranges: [number, number][] = []
    for (const secret of this.secrets) {
      let position = text.indexOf(secret)
      while (position >= 0) { if (position < end && position + secret.length > start) ranges.push([Math.max(start, position), Math.min(end, position + secret.length)]); position = text.indexOf(secret, position + secret.length) }
    }
    let result = '', cursor = start
    for (const [left, right] of ranges.sort((a, b) => a[0] - b[0])) { if (right <= cursor) continue; if (left > cursor) result += text.slice(cursor, left); result += '[redacted]'; cursor = right }
    return result + text.slice(cursor, end)
  }
  finish() {
    this.append(this.decoder.end())
    if (this.length <= WINDOW * 2) { const text = this.head + this.tail.slice(Math.max(0, this.head.length + this.tail.length - this.length)); return this.mask(text, 0, text.length) }
    return this.mask(this.head, 0, WINDOW) + '\n… output truncated …\n' + this.mask(this.tail, this.tail.length - WINDOW, this.tail.length)
  }
}

const windowsWrapper = `$kronoUtf8 = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $kronoUtf8
$OutputEncoding = $kronoUtf8
$kronoReader = [IO.StreamReader]::new([Console]::OpenStandardInput(), $kronoUtf8)
$kronoCode = $kronoReader.ReadToEnd()
$ErrorActionPreference = 'Stop'
& ([ScriptBlock]::Create($kronoCode))
if ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }
`

export const runCommandTool: ToolDefinition = {
  name: 'run_command',
  description: 'Run a non-interactive command: PowerShell on Windows, sh elsewhere. Returns exit status and bounded output. Ask requires one-action approval; Project mode cannot execute shells without an OS sandbox. Background services/interactive sessions are not managed by this bounded tool.',
  inputSchema: Input,
  capabilities: { requiresPermission: 'execute', canCancel: true, longRunning: true },
  async execute(input: unknown, context: ExecutionContext): Promise<ToolResult> {
    try {
      const config = Input.parse(input)
      if (context.permissions === 'project') return { success: false, error: 'Shell execution is unavailable in Project mode; no OS sandbox is configured.' }
      if (context.signal?.aborted) return { success: false, error: 'Command cancelled' }
      const cwd = await realpath(resolve(context.projectDirs[0] || homedir(), config.cwd || '.'))
      if (!(await stat(cwd)).isDirectory()) return { success: false, error: 'Command working directory must be a directory.' }
      if (context.permissions === 'ask' && !(await context.requestApproval({ id: randomUUID(), action: 'run_command', target: config.command, details: { cwd, timeout_ms: config.timeout_ms } })).approved) return { success: false, error: 'Command approval denied' }
      if (context.signal?.aborted) return { success: false, error: 'Command cancelled' }
      const env = Object.fromEntries(Object.entries(process.env).filter(([name]) => !sensitiveVariable.test(name)))
      const secrets = [...new Set((context.redactValues || []).filter(value => value.length > 0))]
      const stdout = new Capture(secrets), stderr = new Capture(secrets)
      return await new Promise<ToolResult>(resolveResult => {
        const windows = process.platform === 'win32'
        const system = join(process.env.SystemRoot || 'C:\\Windows', 'System32')
        const child = spawn(windows ? join(system, 'WindowsPowerShell', 'v1.0', 'powershell.exe') : '/bin/sh', windows ? ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(windowsWrapper, 'utf16le').toString('base64')] : ['-s'], { cwd, env, windowsHide: true, detached: !windows, stdio: ['pipe', 'pipe', 'pipe'] })
        let settled = false, cancelled = false, timedOut = false
        const terminate = () => {
          if (settled || !child.pid) return
          if (windows) { const killer = spawn(join(system, 'taskkill.exe'), ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }); killer.on('error', () => { try { child.kill() } catch { /* already closed */ } }) }
          else { try { process.kill(-child.pid, 'SIGKILL') } catch { /* already closed */ } }
        }
        const abort = () => { cancelled = true; terminate() }
        const timer = setTimeout(() => { timedOut = true; terminate() }, config.timeout_ms)
        const finish = (code: number | null, error?: string) => {
          if (settled) return; settled = true; clearTimeout(timer); context.signal?.removeEventListener('abort', abort)
          resolveResult({ success: code === 0 && !cancelled && !timedOut && !error, output: `Exit code: ${code ?? 'terminated'}\nSTDOUT:\n${stdout.finish()}\nSTDERR:\n${stderr.finish()}`, ...(error || cancelled || timedOut || code !== 0 ? { error: error || (cancelled ? 'Command cancelled' : timedOut ? 'Command timed out' : 'Command exited unsuccessfully') } : {}) })
        }
        child.stdout.on('data', buffer => stdout.write(buffer)); child.stderr.on('data', buffer => stderr.write(buffer))
        child.stdin.on('error', () => { /* close/error settles the result */ })
        child.on('error', error => finish(null, error.message)); child.on('close', code => finish(code))
        context.signal?.addEventListener('abort', abort, { once: true }); if (context.signal?.aborted) abort()
        child.stdin.end(config.command + '\n')
      })
    } catch (error) { return { success: false, error: error instanceof Error ? error.message : 'Command failed' } }
  },
}
