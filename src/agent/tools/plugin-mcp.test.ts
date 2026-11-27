import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { afterEach, expect, it, vi } from 'vitest'
import { connectMcp, mcpTools, type McpGrant } from './plugin-mcp'
import vercelMetadata from '../../../docs/oauth-client-v3.json'

const origin = 'https://mcp.notion.com'
const remoteFetch = globalThis.fetch
const authValue = 'fixture-remote-oauth'
const refreshedValue = 'fixture-renewed-oauth'
const refreshValue = 'fixture-refresh-grant'
const verification = 'fixture-code'
afterEach(() => vi.unstubAllGlobals())

it('authorizes and refreshes a remote plugin with PKCE and exposes only curated read tools', async () => {
  let challenge = '', wrongState = 0, register = 0
  let calls: string[] = []
  let redirected: Promise<void> | undefined
  let accepted = authValue
  const fixture = vi.fn<typeof fetch>(async (target, init) => {
    const url = new URL(target instanceof Request ? target.url : target)
    if (url.origin !== origin) throw new Error('Provider request escaped its origin')
    if (url.pathname.startsWith('/.well-known/oauth-protected-resource'))
      return Response.json({ resource: `${origin}/mcp`, authorization_servers: [origin] })
    if (url.pathname.startsWith('/.well-known/oauth-authorization-server'))
      return Response.json({ issuer: origin, authorization_endpoint: `${origin}/authorize`, token_endpoint: `${origin}/token`, registration_endpoint: `${origin}/register`,
        response_types_supported: ['code'], grant_types_supported: ['authorization_code', 'refresh_token'], token_endpoint_auth_methods_supported: ['none'], code_challenge_methods_supported: ['S256'] })
    if (url.pathname === '/register') {
      register++
      return Response.json({ client_id: 'fixture-public-client', ...JSON.parse(String(init?.body)) }, { status: 201 })
    }
    if (url.pathname === '/token') {
      const body = new URLSearchParams(String(init?.body))
      if (body.get('grant_type') === 'refresh_token') {
        expect(body.get('refresh_token')).toBe(refreshValue)
        return Response.json({ access_token: refreshedValue, token_type: 'Bearer' })
      }
      expect(body.get('code')).toBe(verification)
      expect(createHash('sha256').update(body.get('code_verifier')!).digest('base64url')).toBe(challenge)
      return Response.json({ access_token: authValue, token_type: 'Bearer', refresh_token: refreshValue })
    }
    if (url.pathname !== '/mcp') return new Response(null, { status: 404 })
    if (new Headers(init?.headers).get('authorization') !== `Bearer ${accepted}`)
      return new Response(null, { status: 401, headers: { 'WWW-Authenticate': `Bearer resource_metadata="${origin}/.well-known/oauth-protected-resource/mcp"` } })
    if (init?.method !== 'POST') return new Response(null, { status: 405 })
    const request = JSON.parse(String(init.body))
    if (request.method === 'initialize') return Response.json({ jsonrpc: '2.0', id: request.id, result: {
      protocolVersion: request.params.protocolVersion, capabilities: { tools: {} }, serverInfo: { name: 'Fixture MCP', version: '1' },
    } })
    if (request.method === 'tools/list') return Response.json({ jsonrpc: '2.0', id: request.id, result: {
      tools: ['notion-search', 'notion-fetch', 'send_message'].map(name => ({ name, inputSchema: { type: 'object' } })),
    } })
    if (request.method === 'tools/call') {
      calls.push(request.params.name)
      expect(request.params.arguments).toEqual({ query: 'Launch', query_type: 'internal' })
      return Response.json({ jsonrpc: '2.0', id: request.id, result: { content: [{ type: 'text', text: `A matching page. ${accepted}` }] } })
    }
    return new Response(null, { status: 202 })
  })
  vi.stubGlobal('fetch', fixture)
  const result = await connectMcp('notion', AbortSignal.timeout(5_000), address => {
    redirected = (async () => {
      const url = new URL(address)
      expect(url.origin).toBe(origin)
      expect(url.searchParams.get('code_challenge_method')).toBe('S256')
      challenge = url.searchParams.get('code_challenge')!
      const callback = new URL(url.searchParams.get('redirect_uri')!)
      callback.searchParams.set('code', verification); callback.searchParams.set('state', 'wrong')
      wrongState = (await remoteFetch(callback)).status
      callback.searchParams.set('state', url.searchParams.get('state')!)
      await remoteFetch(callback)
    })()
  })
  await redirected
  expect(wrongState).toBe(400)
  expect(register).toBe(1)
  expect(result.success).toBe(true)
  if (!result.success) throw new Error(result.message)
  const persist = vi.fn<(grant: McpGrant) => void>()
  const tools = mcpTools('notion', result.connection, persist)
  expect(tools.map(tool => tool.name)).toEqual(['notion_search', 'notion_fetch'])
  expect(tools.every(tool => tool.capabilities.requiresPermission === 'execute')).toBe(true)
  const context = { projectDirs: [], requestApproval: async () => ({ approved: true }), emitProgress: () => {} }
  expect(await tools[0].execute({ query: 'Launch' }, { ...context, permissions: 'project' })).toMatchObject({ success: false })
  expect(calls).toEqual([])
  expect(await tools[0].execute({ query: 'Launch', unrelated: 'discarded' }, { ...context, permissions: 'full' })).toEqual({ success: true, output: 'A matching page. [redacted]' })
  expect(calls).toEqual(['notion-search'])
  accepted = refreshedValue
  expect(await tools[0].execute({ query: 'Launch' }, { ...context, permissions: 'full' })).toEqual({ success: true, output: 'A matching page. [redacted]' })
  expect(persist).toHaveBeenCalledOnce()
  expect(persist.mock.calls[0][0].tokens).toMatchObject({ access_token: refreshedValue, refresh_token: refreshValue })
})

it('rejects provider discovery outside the allowlisted host', async () => {
  const fixture = vi.fn<typeof fetch>(async () => new Response(null, { status: 401, headers: { 'WWW-Authenticate': 'Bearer resource_metadata="https://unrelated.example/discovery"' } }))
  vi.stubGlobal('fetch', fixture)
  const authorize = vi.fn()
  expect(await connectMcp('notion', AbortSignal.timeout(1_500), authorize)).toMatchObject({ success: false })
  expect(authorize).not.toHaveBeenCalled()
  expect(fixture.mock.calls.every(([target]) => new URL(target instanceof Request ? target.url : target).origin === new URL(origin).origin)).toBe(true)
})

it('relays the published HTTPS Vercel callback to the app and exchanges the code with PKCE', async () => {
  const endpoint = 'https://mcp.vercel.com/'
  const issuer = 'https://vercel.com'
  let challenge = ''
  vi.stubGlobal('fetch', vi.fn<typeof fetch>(async (target, init) => {
    const url = new URL(target instanceof Request ? target.url : target)
    if (url.pathname.startsWith('/.well-known/oauth-protected-resource'))
      return Response.json({ resource: endpoint, authorization_servers: [issuer], scopes_supported: ['openid'] })
    if (url.pathname.startsWith('/.well-known/oauth-authorization-server'))
      return Response.json({ issuer, authorization_endpoint: issuer + '/oauth/authorize', token_endpoint: issuer + '/oauth/token',
        response_types_supported: ['code'], code_challenge_methods_supported: ['S256'], client_id_metadata_document_supported: true })
    if (url.href === issuer + '/oauth/token') {
      const body = new URLSearchParams(String(init?.body))
      expect(body.get('redirect_uri')).toBe(vercelMetadata.redirect_uris[0])
      expect(body.get('code')).toBe(verification)
      expect(createHash('sha256').update(body.get('code_verifier')!).digest('base64url')).toBe(challenge)
      return Response.json({ access_token: authValue, token_type: 'Bearer' })
    }
    if (url.href !== endpoint) throw new Error('Unexpected authorization endpoint')
    if (new Headers(init?.headers).get('authorization') === `Bearer ${authValue}`) {
      if (init?.method !== 'POST') return new Response(null, { status: 405 })
      const request = JSON.parse(String(init.body))
      if (request.method === 'initialize') return Response.json({ jsonrpc: '2.0', id: request.id,
        result: { protocolVersion: request.params.protocolVersion, capabilities: { tools: {} }, serverInfo: { name: 'Fixture', version: '1' } } })
      if (request.method === 'tools/list') return Response.json({ jsonrpc: '2.0', id: request.id,
        result: { tools: [{ name: 'list_teams', inputSchema: { type: 'object' } }] } })
      return new Response(null, { status: 202 })
    }
    return new Response(null, { status: 401, headers: { 'WWW-Authenticate': 'Bearer resource_metadata="https://mcp.vercel.com/.well-known/oauth-protected-resource"' } })
  }))
  let callback: Promise<number> | undefined
  const result = await connectMcp('vercel', AbortSignal.timeout(5_000), address => {
    const url = new URL(address)
    expect(url.searchParams.get('client_id')).toBe(vercelMetadata.client_id)
    expect(url.searchParams.get('redirect_uri')).toBe(vercelMetadata.redirect_uris[0])
    expect(url.searchParams.get('resource')).toBe(endpoint)
    challenge = url.searchParams.get('code_challenge')!
    const target = new URL(vercelMetadata.redirect_uris[0])
    target.searchParams.set('state', url.searchParams.get('state')!)
    target.searchParams.set('code', verification)
    const clearAddress = vi.fn()
    runInNewContext(readFileSync('docs/plugin-callback.js', 'utf8'), {
      URL, URLSearchParams,
      location: { search: target.search, pathname: target.pathname, replace: (address: string) => {
        expect(new URL(address).origin).toBe('http://127.0.0.1:54127')
        callback = remoteFetch(address).then(response => response.status)
      } },
      history: { replaceState: clearAddress },
      document: { getElementById: () => ({}) },
    })
    expect(clearAddress).toHaveBeenCalledWith(null, '', target.pathname)
  })
  expect(await callback).toBe(200)
  expect(result).toMatchObject({ success: true, connection: { redirect: vercelMetadata.redirect_uris[0] } })
})
