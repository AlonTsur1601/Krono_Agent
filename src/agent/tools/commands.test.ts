import { describe, expect, it } from 'vitest'
import { mkdtemp, readFile, realpath, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { runCommandTool } from './commands'
import type { ExecutionContext } from '../types'

const pause = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))
const quote = (value: string) => process.platform === 'win32' ? "'" + value.replace(/'/g, "''") + "'" : "'" + value.replace(/'/g, "'\\''") + "'"
async function script(code: string) {
  const folder = await mkdtemp(join(tmpdir(), 'krono-command-'))
  const file = join(folder, 'command.cjs'); await writeFile(file, code)
  return { folder, command: (process.platform === 'win32' ? '& ' : '') + quote(process.execPath) + ' ' + quote(file) }
}
const context = (overrides: Partial<ExecutionContext> = {}): ExecutionContext => ({ projectDirs: [], permissions: 'full', requestApproval: async () => ({ approved: false }), emitProgress() {}, ...overrides })

describe('real bounded command execution', () => {
  it('preserves working directories, UTF-8 output and exit status', async () => {
    const job = await script("console.log('42 · שלום'); console.log(process.cwd()); process.exit(7)")
    await writeFile(join(job.folder, 'powershell.exe'), 'fixture decoy: do not execute')
    const result = await runCommandTool.execute({ command: job.command, cwd: job.folder }, context())
    expect(result.success).toBe(false)
    expect(result.output).toContain('42 · שלום')
    expect(result.output).toContain(await realpath(job.folder))
    expect(result.output).toContain('Exit code: 7')
  }, 15000)
  it('never starts denied or Project-mode commands', async () => {
    const job = await script("require('fs').writeFileSync('proof.txt','ran')")
    for (const permissions of ['ask', 'project'] as const) {
      expect((await runCommandTool.execute({ command: job.command, cwd: job.folder }, context({ permissions }))).success).toBe(false)
      await expect(readFile(join(job.folder, 'proof.txt'))).rejects.toThrow()
    }
  })
  it('requires Ask approval and excludes secret environment variables', async () => {
    const job = await script("console.log(process.env.KRONO_COMMAND_SECRET || 'not-forwarded')")
    const old = process.env.KRONO_COMMAND_SECRET
    process.env.KRONO_COMMAND_SECRET = 'fixture-environment-secret'
    try {
      let approvals = 0
      const result = await runCommandTool.execute({ command: job.command, cwd: job.folder }, context({ permissions: 'ask', requestApproval: async () => { approvals++; return { approved: true } } }))
      expect(approvals).toBe(1); expect(result.success).toBe(true)
      expect(result.output).toContain('not-forwarded'); expect(result.output).not.toContain('fixture-environment-secret')
    } finally { if (old === undefined) delete process.env.KRONO_COMMAND_SECRET; else process.env.KRONO_COMMAND_SECRET = old }
  }, 15000)
  it('redacts credentials across both truncation boundaries and limits output', async () => {
    const secret = 'fixture-private-token-00000000'
    const job = await script(`process.stdout.write('A'.repeat(4090)+${JSON.stringify(secret)}+'B'.repeat(10000)+${JSON.stringify(secret)}+'C'.repeat(4088))`)
    const result = await runCommandTool.execute({ command: job.command, cwd: job.folder }, context({ redactValues: [secret] }))
    expect(result.success).toBe(true); expect(result.output).toContain('output truncated')
    expect(result.output).not.toContain('fixture-private'); expect(result.output).not.toContain('token-00000000')
    expect(result.output!.length).toBeLessThan(9000)
  }, 15000)
  it('times out commands and cancels a live process tree before its delayed write', async () => {
    const timeout = await script('setInterval(()=>{},1000)')
    const timed = await runCommandTool.execute({ command: timeout.command, cwd: timeout.folder, timeout_ms: 200 }, context())
    expect(timed.success).toBe(false); expect(timed.error).toContain('timed out')
    const controller = new AbortController()
    const child = "setTimeout(()=>require('fs').writeFileSync('late.txt','survived'),1600)"
    const job = await script(`require('child_process').spawn(process.execPath,['-e',${JSON.stringify(child)}],{stdio:'ignore'}); require('fs').writeFileSync('started.txt','ready'); setInterval(()=>{},1000)`)
    const running = runCommandTool.execute({ command: job.command, cwd: job.folder }, context({ signal: controller.signal }))
    try {
      const end = Date.now() + 6000
      while (true) { try { await readFile(join(job.folder, 'started.txt')); break } catch { if (Date.now() > end) throw new Error('Command fixture never started'); await pause(30) } }
      controller.abort(); expect((await running).error).toContain('cancelled')
    } finally { controller.abort(); await running }
    await pause(1900); await expect(readFile(join(job.folder, 'late.txt'))).rejects.toThrow()
  }, 15000)
})
