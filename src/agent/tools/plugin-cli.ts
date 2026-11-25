import { execFile, spawn } from 'node:child_process'
import { access, mkdir } from 'node:fs/promises'
import { delimiter, isAbsolute, join } from 'node:path'
import { promisify } from 'node:util'
import { z } from 'zod'
import { sensitiveVariable } from './commands'

const execute = promisify(execFile)
export const CliProfile = z.string().min(1).max(3000).refine(value => isAbsolute(value) && !value.includes('\0'))
export type CliAccount = { profile: string }
type Provider = 'github' | 'firebase'
const firebaseSetup = 'Install Google Cloud CLI, then return to Plugins and try connecting Firebase again.'

async function cloudSdkPath(env: NodeJS.ProcessEnv) {
  const folders = [
    ...(env.Path ?? env.PATH ?? '').split(delimiter),
    ...[env.LOCALAPPDATA, env.ProgramFiles, env['ProgramFiles(x86)']].filter(Boolean)
      .map(root => join(root!, 'Google', 'Cloud SDK', 'google-cloud-sdk', 'bin')),
  ]
  for (const folder of folders.map(folder => folder.replace(/^"|"$/g, '')).filter(isAbsolute)) {
    try { await access(join(folder, 'gcloud.cmd')); return folder } catch { /* keep looking */ }
  }
}

function environment(id: Provider, profile: string): NodeJS.ProcessEnv {
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !sensitiveVariable.test(key) &&
    !/^(GH_|GITHUB_|CLOUDSDK_|GIT_CONFIG|BROWSER$|NODE_OPTIONS$|PYTHONPATH$)/i.test(key)))
  if (id === 'github') Object.assign(env, { GH_CONFIG_DIR: profile, GH_NO_UPDATE_NOTIFIER: '1', GH_TELEMETRY: '0', GH_PROMPT_DISABLED: '1' })
  else Object.assign(env, { CLOUDSDK_CONFIG: profile, CLOUDSDK_CORE_DISABLE_USAGE_REPORTING: '1' })
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

export async function connectCli(id: Provider, profile: string, signal: AbortSignal, open: (url: string) => void, showDeviceCode: (code: string) => void) {
  if (signal.aborted) return { success: false as const, message: 'Sign-in cancelled.' }
  try {
    await mkdir(profile, { recursive: true })
    const windows = process.platform === 'win32'
    const env = environment(id, profile)
    const program = id === 'github' ? windows ? 'gh.exe' : 'gh' : windows ? join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'cmd.exe') : 'gcloud'
    const googleLogin = ['auth', 'application-default', 'login', '--launch-browser', '--disable-quota-project',
      '--scopes=openid,https://www.googleapis.com/auth/userinfo.email,https://www.googleapis.com/auth/cloud-platform.read-only']
    if (id === 'firebase' && windows) {
      const sdk = await cloudSdkPath(env)
      if (!sdk) return { success: false as const, message: firebaseSetup, needsSetup: true }
      const pathKey = Object.keys(env).find(key => key.toLowerCase() === 'path') ?? 'Path'
      env[pathKey] = [sdk, env[pathKey]].filter(Boolean).join(delimiter)
    }
    const args = id === 'github' ? ['auth', 'login', '--hostname', 'github.com', '--web', '--git-protocol', 'https', '--skip-ssh-key'] :
      windows ? ['/d', '/s', '/c', 'gcloud.cmd ' + googleLogin.join(' ')] : googleLogin
    const code = await new Promise<number | null>((resolve, reject) => {
      const child = spawn(program, args, { cwd: profile, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
      const abort = () => {
        if (windows && id === 'firebase' && child.pid) {
          const killer = spawn(join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'taskkill.exe'), ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' })
          killer.once('error', () => child.kill())
          killer.once('exit', code => { if (code !== 0) child.kill() })
        } else child.kill()
      }
      signal.addEventListener('abort', abort, { once: true })
      if (signal.aborted) abort()
      let tail = '', announcedCode = false, opened = false
      function read(chunk: Buffer) {
        if (id !== 'github' || announcedCode && opened) return
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
    if (code !== 0) return { success: false as const, message: id === 'github' ? 'GitHub sign-in did not finish. Update GitHub CLI and try again.' : 'Google sign-in did not finish. Try again and approve access with the Google account that has access to Firebase.' }
    return { success: true as const, profile }
  } catch {
    return signal.aborted
      ? { success: false as const, message: 'Sign-in was cancelled or timed out.' }
      : { success: false as const, message: id === 'github' ? 'Install GitHub CLI before connecting.' : firebaseSetup, needsSetup: true }
  }
}
