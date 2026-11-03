import { z } from 'zod'

const ConfigSchema = z.object({
  url: z.string().url().refine(value => {
    const url = new URL(value)
    return !url.username && !url.password && (url.protocol === 'https:' ||
      (url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)))
  }, 'Use HTTPS, or HTTP for a local Supabase development server.'),
  anonKey: z.string().trim().min(1),
}).strict()

export function parseAccountConfig(input: unknown) {
  const result = ConfigSchema.safeParse(input)
  if (!result.success) throw new Error('Invalid account-service configuration. Expected url and anonKey; remote URLs must use HTTPS.')
  const { anonKey } = result.data
  if (anonKey.startsWith('sb_secret_')) throw new Error('Use a publishable or anon key, never a secret/service-role key.')
  if (anonKey.split('.').length === 3) {
    let role: unknown
    try { role = JSON.parse(Buffer.from(anonKey.split('.')[1], 'base64url').toString()).role }
    catch { throw new Error('Invalid account-service key format.') }
    if (role !== 'anon') throw new Error('Use a publishable or anon key, never a secret/service-role key.')
  }
  return result.data
}
