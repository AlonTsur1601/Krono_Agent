import { access, open, stat } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js'
import { GoogleAuth } from 'google-auth-library'
import { z } from 'zod'
import type { ExecutionContext, ToolDefinition, ToolResult } from '../types'
import { authorizePath } from './files'
import { CliProfile, connectCli, firebaseAccessToken, githubGet } from './plugin-cli'
import { McpGrant, McpId, checkMcp, connectMcp, mcpTools } from './plugin-mcp'
import catalog from './plugin-catalog.json'

const token = z.string().min(1).max(4096).regex(/^[\x21-\x7E]+$/)
const team = z.string().regex(/^team_[a-z0-9_]+$/i).max(100)
const rest = z.discriminatedUnion('id', [
  z.object({ id: z.literal('github'), token: token.optional(), profile: CliProfile.optional() }).refine(options => !!(options.token || options.profile)),
  z.object({ id: z.literal('vercel'), token, teamId: team.optional() }),
  z.object({ id: z.literal('firebase'), profile: CliProfile.optional() }),
])
const mcp = z.object({ id: McpId, connection: McpGrant, grantId: z.uuid() })
export const PluginConfig = z.union([
  rest, mcp,
  z.object({ id: z.literal('node') }),
  z.object({ id: z.enum(['huggingface', 'npm', 'pypi', 'microsoftlearn']) }),
  z.object({ id: z.string().refine(id => catalog.some(item => item.id === id && item.type === 'skill')), skill: z.literal(true), guidance: z.string().max(2_000).optional() }),
]).and(z.object({ tools: z.array(z.string().regex(/^[a-z0-9_]+$/)).max(20).optional() }))
type Config = z.infer<typeof PluginConfig>
type RestConfig = z.infer<typeof rest>
type FirebaseConfig = Extract<RestConfig, { id: 'firebase' }> & { auth?: GoogleAuth; session?: Awaited<ReturnType<typeof firebaseAccessToken>> }
type RemoteConfig = Exclude<RestConfig, { id: 'firebase' }> | FirebaseConfig
type Query = Record<string, string | number | undefined>
type PersistGrant = (id: string, grantId: string, connection: McpGrant) => void

const origins = {
  github: 'https://api.github.com',
  vercel: 'https://api.vercel.com',
  firebase: 'https://firebase.googleapis.com',
}
const pageSize = 15
const responseLimit = 4 * 1024 * 1024
const packageLimit = 128 * 1024
const createFirebaseAuth = (profile?: string) => new GoogleAuth({
  scopes: ['https://www.googleapis.com/auth/cloud-platform.read-only'],
  ...(profile ? { keyFilename: join(profile, 'application_default_credentials.json') } : {}),
})

class PluginError extends Error {}

function object(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : {}
}

function fields(value: unknown, names: readonly string[]) {
  const row = object(value)
  return Object.fromEntries(names.map(name => [name, row[name]])
    .filter(([, value]) => value === null || ['string', 'number', 'boolean'].includes(typeof value)))
}

function items(value: unknown, names: readonly string[]) {
  return Array.isArray(value) ? value.slice(0, pageSize).map(row => fields(row, names)) : []
}

async function requestHeaders(config: RemoteConfig, url: URL, signal?: AbortSignal): Promise<Headers> {
  if (config.id === 'firebase') {
    try {
      const legacy = config.profile && await access(join(config.profile, 'application_default_credentials.json')).then(() => true, () => false)
      if (config.profile && !legacy) {
        if (!config.session || (config.session.expires ?? 0) < Date.now() + 60_000)
          config.session = await firebaseAccessToken(config.profile, signal)
        return new Headers({ Authorization: `Bearer ${config.session.token}` })
      }
      const firebaseAuth = config.auth ?? createFirebaseAuth(config.profile)
      const credentials = await firebaseAuth.getRequestHeaders(url)
      if (!credentials.has('authorization')) throw new Error()
      return new Headers(credentials)
    } catch {
      throw new PluginError('Connect Firebase in Settings → Plugins using the Google account that has access to your projects.')
    }
  }
  const headers = new Headers({ Authorization: `Bearer ${config.token}` })
  if (config.id === 'github') {
    headers.set('Accept', 'application/vnd.github+json')
    headers.set('X-GitHub-Api-Version', '2022-11-28')
  }
  return headers
}

async function boundedJson(response: Response): Promise<unknown> {
  const reader = response.body?.getReader()
  if (!reader) throw new PluginError('The service returned an empty response.')
  const chunks: Buffer[] = []
  let length = 0
  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      length += value.byteLength
      if (length > responseLimit) throw new PluginError('The service response is too large. Narrow your search and retry.')
      chunks.push(Buffer.from(value))
    }
    return JSON.parse(Buffer.concat(chunks, length).toString('utf8'))
  } finally {
    void reader.cancel().catch(() => {})
  }
}

async function getJson(config: RemoteConfig, path: string, query: Query, signal?: AbortSignal) {
  const url = new URL(path, origins[config.id])
  for (const [key, value] of Object.entries(query)) if (value !== undefined) url.searchParams.set(key, String(value))
  if (config.id === 'github' && config.profile) return githubGet(config.profile, url.pathname + url.search, signal)
  const timeout = AbortSignal.timeout(15_000)
  const combined = signal ? AbortSignal.any([signal, timeout]) : timeout
  const headers = await new Promise<Headers>((resolveHeaders, rejectHeaders) => {
    const abort = () => rejectHeaders(combined.reason)
    if (combined.aborted) { abort(); return }
    combined.addEventListener('abort', abort, { once: true })
    void requestHeaders(config, url, combined).then(resolveHeaders, rejectHeaders).finally(() => combined.removeEventListener('abort', abort))
  })
  const response = await fetch(url, { headers, signal: combined, redirect: 'error', method: 'GET' })
  if (!response.ok) {
    void response.body?.cancel().catch(() => {})
    if (response.status === 401) throw new PluginError('Sign-in expired or the access token is invalid.')
    if (response.status === 403 && response.headers.get('x-ratelimit-remaining') !== '0') throw new PluginError('The account cannot access this resource. Check its permissions and project scope.')
    if (response.status === 404) throw new PluginError('The resource could not be found with this account.')
    if (response.status === 429 || response.headers.get('x-ratelimit-remaining') === '0') throw new PluginError('The service rate limit was reached. Try again later.')
    throw new PluginError(`The service returned HTTP ${response.status}. Try again later.`)
  }
  return { data: await boundedJson(response), link: response.headers.get('link'), auth: headers.get('authorization')?.slice(7) }
}

function success(value: unknown, credential?: string): ToolResult {
  const output = JSON.stringify(value, (_, field) => {
    if (typeof field !== 'string') return field
    const safe = credential ? field.replaceAll(credential, '[redacted]') : field
    return safe.slice(0, 2048)
  })
  return { success: true, output }
}

function failure(error: unknown): ToolResult {
  if (error instanceof PluginError) return { success: false, error: error.message }
  if (error instanceof Error && error.name === 'TimeoutError') return { success: false, error: 'The connection timed out. Try again.' }
  if (error instanceof Error && error.name === 'AbortError') return { success: false, error: 'The request was cancelled.' }
  return { success: false, error: 'Could not read the service response. Check your connection and try again.' }
}

const page = z.number().int().min(1).max(100).optional()
const cursor = z.string().min(1).max(2048).optional()
const repository = z.object({
  owner: z.string().regex(/^[a-z0-9-]+$/i).max(100),
  repository: z.string().regex(/^[a-z0-9_.-]+$/i).max(100).refine(value => value !== '.' && value !== '..'),
  state: z.enum(['open', 'closed', 'all']).optional(), page,
})
const firebaseProject = z.string().regex(/^[a-z0-9][a-z0-9-]{0,63}$/i)

function remoteTool<T extends z.ZodType>(
  config: RemoteConfig, name: string, description: string, schema: T,
  route: (input: z.infer<T>) => { path: string; query: Query },
  summary: (data: unknown, link: string | null) => unknown,
): ToolDefinition {
  return {
    name, description, inputSchema: schema,
    // Authenticated account data can leave the local workspace, so it follows
    // the same permission ceiling as other actions that leave Project scope.
    capabilities: { requiresPermission: 'execute', canCancel: true, longRunning: false },
    async execute(input, context) {
      if (context.permissions === 'project') return { success: false, error: 'Cloud plugins are unavailable in Project permissions.' }
      try {
        const { path, query } = route(schema.parse(input))
        const { data, link, auth } = await getJson(config, path, query, context.signal)
        return success(summary(data, link), auth)
      } catch (error) { return failure(error) }
    },
  }
}

function githubTools(config: Extract<RestConfig, { id: 'github' }>): ToolDefinition[] {
  return [
    remoteTool(config, 'github_list_repositories', 'List repositories accessible to the connected GitHub account, sorted by most recently updated.',
      z.object({ page }), input => ({ path: '/user/repos', query: { sort: 'updated', per_page: pageSize, page: input.page } }),
      (data, link) => ({ repositories: items(data, ['full_name', 'description', 'private', 'default_branch', 'html_url', 'updated_at']), hasMore: link?.includes('rel="next"') ?? false })),
    ...(['issues', 'pulls'] as const).map(feature => remoteTool(config,
      feature === 'issues' ? 'github_list_issues' : 'github_list_pull_requests',
      `List recent ${feature === 'issues' ? 'issues' : 'pull requests'} in a GitHub repository. State defaults to open.`, repository,
      input => ({ path: `/repos/${input.owner}/${input.repository}/${feature}`, query: { state: input.state, per_page: pageSize, page: input.page } }),
      (data, link) => ({ results: items(data, ['number', 'title', 'state', 'html_url', 'created_at', 'updated_at', 'draft']).map((row, index) => ({ ...row, kind: feature === 'pulls' || (Array.isArray(data) && object(data[index]).pull_request) ? 'pull request' : 'issue' })), hasMore: link?.includes('rel="next"') ?? false }))),
  ]
}

function vercelTools(config: Extract<RestConfig, { id: 'vercel' }>): ToolDefinition[] {
  const schemas = { until: z.number().int().min(0).optional(), search: z.string().min(1).max(100).optional() }
  return [
    remoteTool(config, 'vercel_list_projects', 'List projects for the connected Vercel account or configured team.',
      z.object(schemas), input => ({ path: '/v10/projects', query: { limit: pageSize, teamId: config.teamId, ...input } }),
      data => ({ projects: items(object(data).projects, ['id', 'name', 'framework', 'updatedAt', 'nodeVersion']), ...fields(object(data).pagination, ['next']) })),
    remoteTool(config, 'vercel_list_deployments', 'List recent deployments of a Vercel project, including target, state and URL.',
      z.object({ projectId: z.string().min(1).max(120), until: schemas.until }),
      input => ({ path: '/v7/deployments', query: { limit: pageSize, teamId: config.teamId, ...input } }),
      data => ({ deployments: items(object(data).deployments, ['uid', 'name', 'url', 'readyState', 'state', 'target', 'created', 'projectId']), ...fields(object(data).pagination, ['next']) })),
  ]
}

function firebaseTools(config: Extract<RestConfig, { id: 'firebase' }>): ToolDefinition[] {
  const connected = { ...config, auth: createFirebaseAuth(config.profile) }
  return [
    remoteTool(connected, 'firebase_list_projects', 'List Firebase projects accessible to the connected Google account.',
      z.object({ pageToken: cursor }), input => ({ path: '/v1beta1/projects', query: { pageSize, ...input } }),
      data => ({ projects: items(object(data).results, ['projectId', 'displayName', 'state']), ...fields(data, ['nextPageToken']) })),
    remoteTool(connected, 'firebase_list_apps', 'List web, iOS and Android apps registered in a Firebase project.',
      z.object({ projectId: firebaseProject, pageToken: cursor }),
      input => ({ path: `/v1beta1/projects/${input.projectId}:searchApps`, query: { pageSize, pageToken: input.pageToken } }),
      data => ({ apps: items(object(data).apps, ['appId', 'displayName', 'platform', 'namespace', 'state']), ...fields(data, ['nextPageToken']) })),
  ]
}

const nodeInput = z.object({ directory: z.string().max(2048).optional() })
const nodeTool: ToolDefinition = {
  name: 'node_inspect_package',
  description: 'Inspect the Node.js package manifest in a workspace directory, including engines, npm script names and dependencies. Does not run scripts or install packages.',
  inputSchema: nodeInput,
  capabilities: { requiresPermission: 'read', canCancel: false, longRunning: false },
  async execute(input: unknown, context: ExecutionContext) {
    const root = context.projectDirs[0]
    if (!root) return { success: false, error: 'Choose a project with a source folder first.' }
    const path = resolve(root, nodeInput.parse(input).directory ?? '.', 'package.json')
    try {
      await authorizePath(path, context)
      if (!(await stat(path)).isFile()) throw new Error()
      const file = await open(path, 'r')
      try {
        const buffer = Buffer.alloc(packageLimit + 1)
        const { bytesRead } = await file.read(buffer, 0, buffer.length, 0)
        if (bytesRead > packageLimit) return { success: false, error: 'The package manifest is too large.' }
        const manifest = object(JSON.parse(buffer.toString('utf8', 0, bytesRead).replace(/^\uFEFF/, '')))
        return success({
          ...fields(manifest, ['name', 'version', 'packageManager', 'type', 'private']),
          engine: fields(manifest.engines, ['node', 'npm']),
          scripts: Object.keys(object(manifest.scripts)).slice(0, 80),
          dependencies: Object.keys(object(manifest.dependencies)).slice(0, 100),
          devDependencies: Object.keys(object(manifest.devDependencies)).slice(0, 100),
          workerNodeVersion: process.versions.node,
        })
      } finally { await file.close() }
    } catch {
      return { success: false, error: 'Could not read a valid package.json in that project folder.' }
    }
  },
}

const hubInput = z.object({ search: z.string().min(1).max(120), task: z.string().regex(/^[a-z-]+$/).max(70).optional() })
const hubTool: ToolDefinition = {
  name: 'huggingface_find_public_models', description: 'Search public models on the Hugging Face Hub by name or task.', inputSchema: hubInput,
  capabilities: { requiresPermission: 'execute', canCancel: true, longRunning: false },
  async execute(input, context) {
    if (context.permissions === 'project') return { success: false, error: 'Online tools require Ask or Full permissions.' }
    const query = hubInput.parse(input)
    try {
      const url = new URL('https://huggingface.co/api/models')
      url.searchParams.set('search', query.search); url.searchParams.set('limit', String(pageSize))
      if (query.task) url.searchParams.set('pipeline_tag', query.task)
      const timeout = AbortSignal.timeout(15_000)
      const response = await fetch(url, { signal: context.signal ? AbortSignal.any([context.signal, timeout]) : timeout, redirect: 'error' })
      if (!response.ok) return { success: false, error: 'Hugging Face could not complete the model search.' }
      return success({ models: items(await boundedJson(response), ['id', 'pipeline_tag', 'library_name', 'downloads', 'likes']) })
    } catch (error) { return failure(error) }
  },
}

const publicCapabilities = { requiresPermission: 'execute', canCancel: true, longRunning: false } as const

function publicLookup<T extends z.ZodType>(
  name: string, description: string, schema: T, lookup: (input: z.infer<T>, signal: AbortSignal) => Promise<unknown>,
): ToolDefinition {
  return {
    name, description, inputSchema: schema, capabilities: publicCapabilities,
    async execute(input, context) {
      if (context.permissions === 'project') return { success: false, error: 'Online tools require Ask or Full permissions.' }
      try {
        const timeout = AbortSignal.timeout(15_000)
        const signal = context.signal ? AbortSignal.any([context.signal, timeout]) : timeout
        return success(await lookup(schema.parse(input), signal))
      } catch (error) { return failure(error) }
    },
  }
}

async function registryMetadata(url: URL, signal: AbortSignal) {
  const response = await fetch(url, { redirect: 'error', signal, headers: { Accept: 'application/json' } })
  if (!response.ok) {
    await response.body?.cancel()
    throw new PluginError(response.status === 404 ? 'Public package not found.' : 'The package registry did not respond successfully.')
  }
  return boundedJson(response)
}

const npmPackage = publicLookup('npm_get_package', 'Look up the latest release of a public npm package by exact name.',
  z.object({ package: z.string().max(214).regex(/^(?:@[a-z0-9][a-z0-9._~-]*\/)?[a-z0-9][a-z0-9._~-]*$/i) }), async (input, signal) => {
    const url = new URL(encodeURIComponent(input.package) + '/latest', 'https://registry.npmjs.org/')
    const data = object(await registryMetadata(url, signal))
    return { ...fields(data, ['name', 'version', 'description', 'license', 'homepage']), engines: fields(data.engines, ['node', 'npm']) }
  })

const pypiPackage = publicLookup('pypi_get_package', 'Look up the latest release of a public Python package on PyPI by exact name.',
  z.object({ package: z.string().max(200).regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/i) }), async (input, signal) => {
    const url = new URL('/pypi/' + encodeURIComponent(input.package) + '/json', 'https://pypi.org/')
    const data = object(await registryMetadata(url, signal))
    return fields(data.info, ['name', 'version', 'summary', 'requires_python', 'license_expression', 'package_url'])
  })

const learnSearch = publicLookup('microsoftlearn_search_docs', 'Search public Microsoft Learn documentation for official guidance.',
  z.object({ query: z.string().trim().min(1).max(300) }), async (input, signal) => {
    const client = new Client({ name: 'Krono Agent', version: '1.0' })
    try {
      await client.connect(new StreamableHTTPClientTransport(new URL('https://learn.microsoft.com/api/mcp')), { signal })
      const tools = await client.listTools(undefined, { signal })
      if (!tools.tools.some(tool => tool.name === 'microsoft_docs_search'))
        throw new PluginError('Microsoft Learn did not offer documentation search.')
      const result = await client.callTool({ name: 'microsoft_docs_search', arguments: input }, undefined, { signal })
      if (result.isError) throw new PluginError('Microsoft Learn could not complete the search.')
      return { results: Array.isArray(result.content) ? result.content.slice(0, 8)
        .map(item => object(item)).filter(item => item.type === 'text' && typeof item.text === 'string')
        .map(item => String(item.text).slice(0, 2200)) : [] }
    } finally { await client.close() }
  })

export function pluginTools(configs: readonly Config[], persist: PersistGrant = () => {}): ToolDefinition[] {
  function available(config: Config): ToolDefinition[] {
    if ('skill' in config) return []
    if ('connection' in config) return mcpTools(config.id, config.connection, grant => persist(config.id, config.grantId, grant))
    switch (config.id) {
      case 'github': return githubTools(config)
      case 'vercel': return vercelTools(config)
      case 'firebase': return firebaseTools(config)
      case 'node': return [nodeTool]
      case 'huggingface': return [hubTool]
      case 'npm': return [npmPackage]
      case 'pypi': return [pypiPackage]
      case 'microsoftlearn': return [learnSearch]
    }
  }
  return configs.flatMap(config => {
    const exported = available(config)
    const installed = config.tools
    return installed === undefined ? exported : exported.filter(tool => installed.includes(tool.name))
  })
}

export function pluginGuidance(configs: readonly Config[]) {
  const installed = new Map(configs.filter(item => 'skill' in item).map(item => [item.id, item.guidance]))
  return catalog.filter(item => installed.has(item.id) && item.guidance &&
    (installed.get(item.id) ?? item.guidance) === item.guidance).map(item => `${item.name}: ${installed.get(item.id) ?? item.guidance}`).join('\n')
}

export const PluginConnect = z.union([
  z.object({ id: McpId }),
  z.object({ id: z.literal('github'), profile: CliProfile }),
  z.object({ id: z.literal('firebase'), profile: CliProfile }),
])

export async function connectPlugin(config: z.infer<typeof PluginConnect>, signal: AbortSignal, open: (url: string) => void, showDeviceCode: (code: string) => void) {
  if (!('profile' in config)) return connectMcp(config.id, signal, open)
  const state = await connectCli(config.id, config.profile, signal, open, showDeviceCode)
  if (!state.success) return state
  const checked = await testPlugin(config, signal)
  return checked.success ? { ...checked, profile: state.profile } : checked
}

export async function testPlugin(config: Config, signal = AbortSignal.timeout(20_000), persist: PersistGrant = () => {}): Promise<{ success: boolean; message: string }> {
  try {
    if ('skill' in config) return { success: true, message: 'Skill pack is available.' }
    if ('connection' in config) return checkMcp(config.id, config.connection, signal, grant => persist(config.id, config.grantId, grant))
    switch (config.id) {
      case 'github': {
        const { data } = await getJson(config, '/user', {}, signal)
        if (typeof object(data).login !== 'string') throw new PluginError('GitHub did not confirm the account.')
        return { success: true, message: 'GitHub confirmed the connected account.' }
      }
      case 'vercel': {
        const { data } = await getJson(config, '/v10/projects', { teamId: config.teamId, limit: 1 }, signal)
        if (!Array.isArray(object(data).projects)) throw new PluginError('Vercel did not confirm project access.')
        return { success: true, message: 'Vercel project access verified.' }
      }
      case 'firebase':
        await getJson(config, '/v1beta1/projects', { pageSize: 1 }, signal)
        return { success: true, message: 'Firebase project access verified.' }
      case 'node':
        return { success: true, message: `Bundled Node.js ${process.versions.node} is available.` }
      case 'huggingface':
        return { success: true, message: 'Public model search is available.' }
      case 'npm':
      case 'pypi':
      case 'microsoftlearn':
        return { success: true, message: 'The public lookup tool is installed.' }
    }
  } catch (error) {
    return { success: false, message: failure(error).error ?? 'The connection failed.' }
  }
}
