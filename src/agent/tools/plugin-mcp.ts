import { randomBytes } from 'node:crypto'
import { createServer } from 'node:http'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js'
import { UnauthorizedError, type OAuthClientProvider } from '@modelcontextprotocol/sdk/client/auth.js'
import { OAuthClientInformationSchema, OAuthTokensSchema } from '@modelcontextprotocol/sdk/shared/auth.js'
import { z } from 'zod'
import type { ToolDefinition } from '../types'

export const McpId = z.enum(['vercel', 'notion', 'linear', 'monday', 'netlify'])
export type McpId = z.infer<typeof McpId>
const vercelCallback = 'https://kronoagent.github.io/Krono_Agent/plugin-callback.html'
export const McpGrant = z.object({
  redirect: z.union([z.literal(vercelCallback), z.string().regex(/^http:\/\/(127\.0\.0\.1|localhost):[0-9]{1,5}\/callback$/)]),
  client: OAuthClientInformationSchema,
  tokens: OAuthTokensSchema,
})
export type McpGrant = z.infer<typeof McpGrant>

type ReadTool = { method: string; description: string; input: z.ZodType }
type Profile = { endpoint: string; origins: string[]; metadata?: { url: string; callback: string; port: number }; scope?: string; tools: ReadTool[] }
const name = z.string().trim().min(1).max(300)
const identifier = z.string().trim().min(1).max(128)
const limit = z.number().int().min(1).max(20).optional()
const vercelTeam = z.string().regex(/^team_[a-z0-9]+$/i).max(100)
const vercelProject = z.string().regex(/^prj_[a-z0-9]+$/i).max(100)
const profiles: Record<McpId, Profile> = {
  vercel: {
    endpoint: 'https://mcp.vercel.com', origins: ['https://mcp.vercel.com', 'https://vercel.com'],
    metadata: {
      url: 'https://kronoagent.github.io/Krono_Agent/oauth-client-v3.json',
      callback: vercelCallback,
      port: 54127,
    },
    tools: [
      { method: 'list_teams', description: 'List the connected Vercel account’s teams.', input: z.object({}) },
      { method: 'list_projects', description: 'List projects for a Vercel team. Use vercel_list_teams to find its ID.', input: z.object({ teamId: vercelTeam }) },
      { method: 'list_deployments', description: 'List recent Vercel deployments associated with a project and team.', input: z.object({ teamId: vercelTeam, projectId: vercelProject }) },
    ],
  },
  notion: {
    endpoint: 'https://mcp.notion.com/mcp', origins: ['https://mcp.notion.com'],
    tools: [
      { method: 'notion-search', description: 'Search authorized Notion pages and databases by title or keyword.', input: z.object({ query: name, query_type: z.literal('internal').default('internal') }) },
      { method: 'notion-fetch', description: 'Read an authorized Notion page or database using the result ID from notion_search.', input: z.object({ id: identifier }) },
    ],
  },
  linear: {
    endpoint: 'https://mcp.linear.app/mcp/readonly', origins: ['https://mcp.linear.app'], scope: 'read',
    tools: [
      { method: 'list_issues', description: 'Find Linear issues by keyword or team.', input: z.object({ query: name.optional(), team: identifier.optional(), limit }) },
      { method: 'get_issue', description: 'Read details of a Linear issue by ID.', input: z.object({ id: identifier }) },
    ],
  },
  monday: {
    endpoint: 'https://mcp.monday.com/mcp', origins: ['https://mcp.monday.com', 'https://auth.monday.com'],
    tools: [
      { method: 'search', description: 'Find boards, documents or folders in the connected monday.com account.', input: z.object({ searchType: z.enum(['BOARD', 'DOCUMENTS', 'FOLDERS']), searchTerm: name.optional(), limit }) },
    ],
  },
  netlify: {
    endpoint: 'https://mcp.netlify.com/mcp', origins: ['https://mcp.netlify.com', 'https://app.netlify.com'],
    tools: [
      { method: 'netlify-open-deployments', description: 'Read recent deployment status for a Netlify site ID.', input: z.object({ siteId: z.uuid(), deployId: identifier.optional() }) },
    ],
  },
}

class ConnectionFailure extends Error {}
const interrupted = 'The connection was cancelled or timed out.'
const reconnect = 'Reconnect this service in Settings → Plugins.'
const requestTimeout = 20_000
const textContent = z.object({ type: z.literal('text'), text: z.string() })

function transport(profile: Profile, redirect: string, state: string, signal: AbortSignal,
  connection?: McpGrant, onAuthorize?: (url: string) => void, onRefresh?: (grant: McpGrant) => void) {
  let client = connection?.client, tokens = connection?.tokens, verifier = ''
  const provider: OAuthClientProvider = {
    redirectUrl: redirect,
    clientMetadataUrl: profile.metadata?.url,
    clientMetadata: { client_name: 'Krono Agent', redirect_uris: [redirect], token_endpoint_auth_method: 'none',
      grant_types: ['authorization_code', 'refresh_token'], response_types: ['code'], scope: profile.scope },
    state: () => state,
    clientInformation: () => client,
    saveClientInformation: value => { client = value },
    tokens: () => tokens,
    saveTokens: value => { tokens = value; if (client) onRefresh?.({ redirect, client, tokens: value }) },
    redirectToAuthorization: url => {
      if (!onAuthorize) throw new ConnectionFailure(reconnect)
      allow(profile, url)
      if (url.searchParams.get('state') !== state || url.searchParams.get('redirect_uri') !== redirect ||
          profile.metadata && url.searchParams.get('client_id') !== profile.metadata.url)
        throw new ConnectionFailure('The authorization request could not be verified.')
      onAuthorize(url.href)
    },
    saveCodeVerifier: value => { verifier = value },
    codeVerifier: () => verifier,
  }
  const channel = new StreamableHTTPClientTransport(new URL(profile.endpoint), {
    authProvider: provider,
    fetch: async (target, init) => {
      const url = new URL(target instanceof Request ? target.url : target)
      allow(profile, url)
      const deadline = AbortSignal.timeout(requestTimeout)
      const combined = init?.signal ? AbortSignal.any([signal, deadline, init.signal]) : AbortSignal.any([signal, deadline])
      return fetch(target, { ...init, redirect: 'error', signal: combined })
    },
  })
  return { channel, grant: () => client && tokens ? { redirect, client, tokens } : undefined }
}

function allow(profile: Profile, url: URL) {
  if (!profile.origins.includes(url.origin) || url.username || url.password)
    throw new ConnectionFailure('The provider returned an unexpected authorization address.')
}

async function startCallback(state: string, signal: AbortSignal, registration?: Profile['metadata']) {
  type Callback = { code: string } | { error: string }
  let finish: (value: Callback) => void = () => {}
  const result = new Promise<Callback>(resolve => { finish = resolve })
  const server = createServer((request, response) => {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1')
    const code = url.searchParams.get('code')
    if (request.method !== 'GET' || url.pathname !== '/callback' || url.searchParams.get('state') !== state) {
      response.writeHead(400).end('This sign-in link does not match the current attempt. Return to Krono Agent and start a new connection.'); return
    }
    response.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer' })
    response.end(code ? 'Authorization received. Return to Krono Agent.' : 'Sign-in was declined. Return to Krono Agent.')
    finish(code ? { code } : { error: url.searchParams.get('error') === 'access_denied'
      ? 'Authorization was declined.' : 'The provider could not approve the connection. Return to Plugins and try again.' })
  })
  server.requestTimeout = 10_000
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject)
    server.listen(registration?.port ?? 0, '127.0.0.1', () => { server.off('error', reject); resolve() })
  }).catch(() => { throw new ConnectionFailure('The local sign-in port is already in use. Close other Krono sign-in attempts and try again.') })
  const address = server.address()
  if (!address || typeof address === 'string') throw new ConnectionFailure('The local sign-in callback is unavailable.')
  const aborted = () => finish({ error: interrupted })
  signal.addEventListener('abort', aborted, { once: true })
  if (signal.aborted) aborted()
  return { redirect: registration?.callback ?? `http://127.0.0.1:${address.port}/callback`, result, close: () => {
    signal.removeEventListener('abort', aborted)
    server.closeIdleConnections()
    server.close()
  } }
}

const createClient = () => new Client({ name: 'Krono Agent', version: '1' })

async function listReadTools(profile: Profile, client: Client, signal: AbortSignal) {
  let cursor: string | undefined
  for (let page = 0; page < 5; page++) {
    const result = await client.listTools({ cursor }, { signal, timeout: requestTimeout })
    if (result.tools.some(tool => profile.tools.some(read => read.method === tool.name))) return
    cursor = result.nextCursor
    if (!cursor) break
  }
  throw new ConnectionFailure('The account does not expose the supported read tools. Check its permissions.')
}

export async function connectMcp(id: McpId, signal: AbortSignal, onAuthorize: (url: string) => void) {
  const profile = profiles[id]
  const state = randomBytes(32).toString('base64url')
  let callback: Awaited<ReturnType<typeof startCallback>> | undefined
  let client = createClient()
  try {
    callback = await startCallback(state, signal, profile.metadata)
    let connection = transport(profile, callback.redirect, state, signal, undefined, onAuthorize)
    try { await client.connect(connection.channel, { signal, timeout: requestTimeout }) }
    catch (error) {
      if (!(error instanceof UnauthorizedError)) throw error
      const result = await callback.result
      if ('error' in result) throw new ConnectionFailure(result.error)
      await connection.channel.finishAuth(result.code)
      const grant = connection.grant()
      if (!grant) throw new ConnectionFailure('The service did not return an authorized account session.')
      client = createClient()
      connection = transport(profile, callback.redirect, state, signal, grant)
      await client.connect(connection.channel, { signal, timeout: requestTimeout })
    }
    await listReadTools(profile, client, signal)
    const grant = connection.grant()
    if (!grant) throw new ConnectionFailure('The service did not return an authorized account session.')
    return { success: true as const, message: 'Account connected.', connection: grant }
  } catch (error) {
    return { success: false as const, message: signal.aborted ? interrupted : error instanceof ConnectionFailure ? error.message : 'Could not authorize this service. Try again or check the provider’s access settings.' }
  } finally {
    callback?.close()
    try { await client.close() } catch { /* the transport may already be closed */ }
  }
}

async function useConnection<T>(id: McpId, connection: McpGrant, signal: AbortSignal, onRefresh: (grant: McpGrant) => void,
  run: (client: Client, profile: Profile) => Promise<T>) {
  const profile = profiles[id]
  const client = createClient()
  const { channel } = transport(profile, connection.redirect, '', signal, connection, undefined, onRefresh)
  try {
    await client.connect(channel, { signal, timeout: requestTimeout })
    return await run(client, profile)
  } finally { try { await client.close() } catch { /* already closed */ } }
}

export async function checkMcp(id: McpId, connection: McpGrant, signal: AbortSignal, refresh: (grant: McpGrant) => void) {
  try {
    await useConnection(id, connection, signal, refresh, (client, profile) => listReadTools(profile, client, signal))
    return { success: true, message: 'Account connection verified.' }
  } catch { return { success: false, message: signal.aborted ? interrupted : reconnect } }
}

export function mcpTools(id: McpId, current: McpGrant, persist: (grant: McpGrant) => void): ToolDefinition[] {
  let connection = current
  const refresh = (grant: McpGrant) => { connection = grant; persist(grant) }
  return profiles[id].tools.map(tool => ({
    name: tool.method.startsWith(id) ? tool.method.replaceAll('-', '_') : `${id}_${tool.method.replaceAll('-', '_')}`,
    description: tool.description,
    inputSchema: tool.input,
    capabilities: { requiresPermission: 'execute', canCancel: true, longRunning: false },
    async execute(input, context) {
      if (context.permissions === 'project') return { success: false, error: 'Cloud account tools require Ask or Full permissions.' }
      const signal = context.signal ?? AbortSignal.timeout(requestTimeout)
      try {
        const args = tool.input.parse(input) as Record<string, unknown>
        const result = await useConnection(id, connection, signal, refresh, client =>
          client.callTool({ name: tool.method, arguments: args }, undefined, { signal, timeout: requestTimeout }))
        if (result.isError) return { success: false, error: 'The service could not complete that read request.' }
        const credentials = [connection.tokens.access_token, connection.tokens.refresh_token, connection.client.client_secret]
        let output = (Array.isArray(result.content) ? result.content : []).flatMap(item => {
          const block = textContent.safeParse(item)
          return block.success ? [block.data.text] : []
        }).join('\n')
        for (const credential of credentials) if (credential) output = output.replaceAll(credential, '[redacted]')
        return { success: true, output: output.slice(0, 16_000) || 'The service returned no text results.' }
      } catch { return { success: false, error: signal.aborted ? interrupted : 'Could not read the service. Check the connection in Settings → Plugins.' } }
    },
  }))
}
