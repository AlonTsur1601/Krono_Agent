import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createServer, type Server, type ServerResponse } from 'node:http'
import { CompatibleProvider, compatibleEndpoint } from './compatible'
import { AgentRuntime } from '../runtime'
import type { CompletionChunk, CompletionRequest } from '../types'

let server: Server, endpoint: string
let requests: Record<string, unknown>[]
let authorization: string[]
let reply: (response: ServerResponse) => void
const delta = (response: ServerResponse, value: unknown) => response.write(`data: ${JSON.stringify(value)}\n\n`)
const textReply = (response: ServerResponse) => {
  delta(response, { choices: [{ delta: { content: 'Local reply' }, finish_reason: null }] })
  delta(response, { choices: [{ delta: {}, finish_reason: 'stop' }] }); response.end('data: [DONE]\n\n')
}
async function collect(provider: CompatibleProvider, request: CompletionRequest) { const result: CompletionChunk[] = []; for await (const chunk of provider.createCompletion(request)) result.push(chunk); return result }
beforeEach(async () => {
  requests = []; authorization = []; reply = textReply
  server = createServer((request, response) => {
    if (request.url === '/v1/models') { response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ data: [{ id: 'local-model' }] })); return }
    let body = ''; request.on('data', chunk => body += chunk)
    request.on('end', () => { authorization.push(request.headers.authorization || ''); requests.push(JSON.parse(body)); response.setHeader('Content-Type', 'text/event-stream'); reply(response) })
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  endpoint = `http://127.0.0.1:${(server.address() as { port: number }).port}/v1`
})
afterEach(async () => { server.closeAllConnections(); await new Promise<void>(resolve => server.close(() => resolve())) })

describe('OpenAI-compatible providers over real HTTP', () => {
  it.each(['azure', 'xai', 'groq', 'mistral', 'openrouter'] as const)('%s requires its credential, discovers models and streams without implicit tools', async id => {
    const provider = new CompatibleProvider(id)
    await expect(provider.configure({ endpoint })).rejects.toThrow('API key')
    await provider.configure({ endpoint, apiKey: 'fixture-cloud-credential' })
    expect((await provider.listModels())[0].providerId).toBe(id)
    const result = await collect(provider, { model: 'local-model', messages: [{ role: 'user', content: 'Hello' }] })
    expect(result[0].content).toBe('Local reply')
    expect(authorization[0]).toBe('Bearer fixture-cloud-credential')
    expect(requests[0].tools).toBeUndefined()
  })
  it.each([[401, 'denied'], [403, 'denied'], [429, 'quota reached. Retry in 20 seconds'], [503, 'temporarily unavailable']] as const)('classifies HTTP %s without forwarding response bodies', async (status, message) => {
    const provider = new CompatibleProvider('custom'); await provider.configure({ endpoint })
    reply = response => { response.writeHead(status, { 'Content-Type': 'application/json', 'retry-after': '20' }); response.end(JSON.stringify({ error: { message: 'Untrusted sensitive diagnostic' } })) }
    await expect(collect(provider, { model: 'local-model', messages: [] })).rejects.toThrow(message)
    expect(requests).toHaveLength(1)
  })
  it('validates transport and rejects credentials/queries in URLs', async () => {
    for (const url of ['http://example.com/v1', 'https://user:secret@example.com/v1', 'https://example.com/v1?key=secret', 'file:///tmp/model', 'http://localhost.evil.test/v1']) expect(() => compatibleEndpoint(url)).toThrow()
    expect(compatibleEndpoint('http://[::1]:1234/v1').hostname).toBe('[::1]')
    const provider = new CompatibleProvider('custom')
    await expect(provider.configure({ endpoint: 'https://example.com/v1' })).rejects.toThrow('API key')
  })
  it('discovers local models without a key and omits tools even in Agent mode when disabled', async () => {
    const provider = new CompatibleProvider('ollama'); await provider.configure({ endpoint })
    expect((await provider.testConnection()).success).toBe(true)
    expect((await provider.listModels())[0].id).toBe('local-model')
    const runtime = new AgentRuntime(); runtime.registerProvider(provider)
    const result: CompletionChunk[] = []
    for await (const chunk of runtime.execute([{ role: 'user', content: 'Hi' }], { providerId: 'ollama', modelId: 'local-model', mode: 'agent', permissions: 'full', toolsEnabled: false })) result.push(chunk)
    expect(result[0].content).toBe('Local reply')
    expect(requests[0].tools).toBeUndefined()
    expect(JSON.stringify(requests[0].messages)).toContain('Tool calling is disabled')
  })
  it('assembles split parallel calls and preserves assistant text and tool result IDs', async () => {
    const provider = new CompatibleProvider('lmstudio'); await provider.configure({ endpoint, toolsEnabled: true })
    reply = response => {
      delta(response, { choices: [{ delta: { tool_calls: [{ index: 0, id: 'call-a', function: { name: 'read_file', arguments: '{"path":' } }, { index: 1, id: 'call-b', function: { name: 'read_file', arguments: '{"path":"b"}' } }] } }] })
      delta(response, { choices: [{ delta: { tool_calls: [{ index: 0, function: { arguments: '"a"}' } }] }, finish_reason: 'tool_calls' }] }); response.end('data: [DONE]\n\n')
    }
    const chunks = await collect(provider, { model: 'local-model', messages: [], tools: [{ name: 'read_file', description: '', inputSchema: { type: 'object' } }] })
    const calls = chunks.flatMap(chunk => chunk.toolCall ? [chunk.toolCall] : [])
    expect(calls.map(call => call.arguments)).toEqual([{ path: 'a' }, { path: 'b' }])
    reply = textReply
    await collect(provider, { model: 'local-model', messages: [{ role: 'assistant', content: 'Reading.', toolCalls: calls }, { role: 'tool', toolCallId: 'call-a', content: 'A' }, { role: 'tool', toolCallId: 'call-b', content: 'B' }] })
    expect(requests[1].messages).toMatchObject([{ content: 'Reading.', tool_calls: [{ id: 'call-a' }, { id: 'call-b' }] }, { tool_call_id: 'call-a' }, { tool_call_id: 'call-b' }])
  })
  it('cancels an active HTTP stream with AbortSignal', async () => {
    const provider = new CompatibleProvider('custom'); await provider.configure({ endpoint })
    reply = response => { delta(response, { choices: [{ delta: { content: 'Started' } }] }) }
    const controller = new AbortController()
    const iterator = provider.createCompletion({ model: 'local-model', messages: [], signal: controller.signal })[Symbol.asyncIterator]()
    expect((await iterator.next()).value?.content).toBe('Started'); controller.abort()
    await expect(iterator.next()).rejects.toThrow('cancelled')
  })
  it('rejects truncated streams and malformed calls instead of marking them done', async () => {
    const provider = new CompatibleProvider('custom'); await provider.configure({ endpoint, toolsEnabled: true })
    reply = response => { delta(response, { choices: [{ delta: { content: 'Partial' } }] }); response.end() }
    await expect(collect(provider, { model: 'local-model', messages: [] })).rejects.toThrow('incomplete')
    reply = response => { delta(response, { choices: [{ delta: { tool_calls: [{ index: 0, id: 'a', function: { name: 'read_file', arguments: '[]' } }] }, finish_reason: 'tool_calls' }] }); response.end() }
    await expect(collect(provider, { model: 'local-model', messages: [] })).rejects.toThrow('incomplete')
  })
  it('refuses redirects and redacts remote error bodies', async () => {
    const provider = new CompatibleProvider('custom'); await provider.configure({ endpoint })
    reply = response => { response.writeHead(302, { location: endpoint + '/leaked' }); response.end('Sensitive error body') }
    await expect(collect(provider, { model: 'local-model', messages: [] })).rejects.toThrow('Endpoint response failed')
    expect(requests).toHaveLength(1)
    reply = response => { response.writeHead(401, { 'Content-Type': 'application/json' }); response.end(JSON.stringify({ error: { message: 'Sensitive error body' } })) }
    try { await collect(provider, { model: 'local-model', messages: [] }); throw new Error('Expected failure') } catch (error) { expect(String(error)).not.toContain('Sensitive error body') }
  })
})
