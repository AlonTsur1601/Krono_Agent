import { execFile, spawn } from 'node:child_process'
import { mkdir } from 'node:fs/promises'
import { isAbsolute, join } from 'node:path'
import { createInterface } from 'node:readline'
import { promisify } from 'node:util'
import { z } from 'zod'
import { sensitiveVariable } from './commands'

const execute = promisify(execFile)
export const CliProfile = z.string().min(1).max(3000).refine(value => isAbsolute(value) && !value.includes('\0'))
export type CliAccount = { profile: string }
type Provider = 'github' | 'firebase'
function environment(id: Provider, profile: string): NodeJS.ProcessEnv {
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !sensitiveVariable.test(key) &&
    !/^(GH_|GITHUB_|CLOUDSDK_|GOOGLE_|GCLOUD_|FIREBASE_|XDG_CONFIG_HOME$|GIT_CONFIG|BROWSER$|NODE_OPTIONS$|PYTHONPATH$)/i.test(key)))
  if (id === 'github') Object.assign(env, { GH_CONFIG_DIR: profile, GH_NO_UPDATE_NOTIFIER: '1', GH_TELEMETRY: '0', GH_PROMPT_DISABLED: '1' })
  else Object.assign(env, { XDG_CONFIG_HOME: profile })
  return env
}

export async function githubGet(profile: string, path: string, signal?: AbortSignal): Promise<{ data: unknown; link: string | null; auth?: string }> {
  const { stdout } = await execute(process.platform === 'win32' ? 'gh.exe' : 'gh', ['api', '--hostname', 'github.com', '--method', 'GET', '--include', path], {
    cwd: profile, env: environment('github', profile), timeout: 15_000, maxBuffer: 4 * 1024 * 1024, signal, windowsHide: true,
  })
  const boundary = /\r?\n\r?\n/.exec(stdout)
  if (!boundary || boundary.index < 0) throw new Error('Invalid GitHub response')
  const header = stdout.slice(0, boundary.index)
  return { data: JSON.parse(stdout.slice(boundary.index + boundary[0].length)), link: /^link:\s*([^\r\n]+)/im.exec(header)?.[1] ?? null }
}

async function firebaseLogin(profile: string, signal: AbortSignal, open: (url: string) => void) {
  await new Promise<void>((resolve, reject) => {
    const child = spawn(process.execPath, [join(__dirname, 'firebase', 'firebase-login.cjs'), 'login'], {
      cwd: profile, env: environment('firebase', profile), signal, windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'],
    })
    const output = createInterface({ input: child.stdout })
    output.on('line', line => {
      try {
        const address = new URL(JSON.parse(line).authorize)
        if (address.origin === 'https://accounts.google.com' && address.pathname === '/o/oauth2/auth')
          open(address.href)
      } catch { /* Ignore unexpected output from the helper. */ }
    })
    child.once('error', reject)
    child.once('close', code => code === 0 ? resolve() : reject(new Error('Firebase sign-in failed')))
  })
}

export async function firebaseAccessToken(profile: string, signal?: AbortSignal) {
  const { stdout } = await execute(process.execPath, [join(__dirname, 'firebase', 'firebase-login.cjs'), 'access'], {
    cwd: profile, env: environment('firebase', profile), timeout: 15_000, signal, windowsHide: true, maxBuffer: 64 * 1024,
  })
  return z.object({ token: z.string().min(1).max(4096).regex(/^[!-~]+$/), expires: z.number().optional() }).parse(JSON.parse(stdout))
}

export async function connectCli(id: Provider, profile: string, signal: AbortSignal, open: (url: string) => void, showDeviceCode: (code: string) => void) {
  if (signal.aborted) return { success: false as const, message: 'Sign-in cancelled.' }
  try {
    await mkdir(profile, { recursive: true })
    if (id === 'firebase') {
      await firebaseLogin(profile, signal, open)
      return { success: true as const, profile }
    }
    const program = process.platform === 'win32' ? 'gh.exe' : 'gh'
    const args = ['auth', 'login', '--hostname', 'github.com', '--web', '--git-protocol', 'https', '--skip-ssh-key']
    const env = environment(id, profile)
    const code = await new Promise<number | null>((resolve, reject) => {
      const child = spawn(program, args, { cwd: profile, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
      const abort = () => { child.kill() }
      signal.addEventListener('abort', abort, { once: true })
      if (signal.aborted) abort()
      let tail = '', announcedCode = false, opened = false
      function read(chunk: Buffer) {
        if (announcedCode && opened) return
        tail = (tail + chunk.toString('utf8')).slice(-512)
        const device = /(?:code|enter)\b[^\r\n]{0,100}\b([A-Z0-9]{4}-[A-Z0-9]{4})\b/i.exec(tail)?.[1]
        if (!announcedCode && device) { announcedCode = true; showDeviceCode(device.toUpperCase()) }
        if (!opened && /https:\/\/github\.com\/login\/device\b/i.test(tail)) { opened = true; open('https://github.com/login/device') }
      }
      child.stdout.on('data', read); child.stderr.on('data', read)
      const cleanup = () => signal.removeEventListener('abort', abort)
      child.once('close', code => { cleanup(); resolve(code) })
      child.once('error', error => { cleanup(); reject(error) })
    })
    if (signal.aborted) return { success: false as const, message: 'Sign-in was cancelled or timed out.' }
    if (code !== 0) return { success: false as const, message: 'GitHub sign-in did not finish. Update GitHub CLI and try again.' }
    return { success: true as const, profile }
  } catch {
    return signal.aborted
      ? { success: false as const, message: 'Sign-in was cancelled or timed out.' }
      : id === 'github'
        ? { success: false as const, message: 'Install GitHub CLI before connecting.', needsSetup: true }
        : { success: false as const, message: 'Firebase sign-in did not finish. Try again and approve access with your Firebase Google account.' }
  }
}
