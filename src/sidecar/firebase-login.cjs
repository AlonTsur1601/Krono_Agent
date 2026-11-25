const auth = require('firebase-tools/lib/auth')
const { logger } = require('firebase-tools/lib/logger')

// Child process stdout is a private pipe to the engine. Never emit provider diagnostics.
logger.silent = true

async function main() {
  if (process.argv[2] === 'login') {
    auth.recordCredentials(await auth.loginGoogle(true))
    return
  }
  if (process.argv[2] !== 'access') throw new Error()
  const { tokens } = auth.getGlobalDefaultAccount() ?? {}
  if (!tokens?.refresh_token) throw new Error()
  const access = tokens.expires_at > Date.now() + 60_000 && tokens.access_token
    ? tokens : await auth.getAccessToken(tokens.refresh_token, tokens.scopes)
  if (!access?.access_token || access.access_token === tokens.refresh_token) throw new Error()
  process.stdout.write(JSON.stringify({ token: access.access_token, expires: access.expires_at }))
}

main().then(() => process.exit(0), () => process.exit(1))
