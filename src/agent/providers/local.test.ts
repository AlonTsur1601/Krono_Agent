import { afterEach, expect, it, vi } from 'vitest'
import { LocalProvider } from './local'

afterEach(() => vi.unstubAllGlobals())

it('discovers only the local catalog and preserves the source of duplicate model IDs', async () => {
  const request = vi.fn(async (input: string) => {
    const url = new URL(input)
    if (url.port === '11434') return Response.json({ models: [
      { name: 'qwen:4b' }, { name: 'qwen:4b' }, { name: 'qwen:cloud' }, { name: 'cloud-alias', remote_host: 'https://ollama.com' },
    ] })
    if (url.port === '1234') return Response.json({ data: [{ id: 'qwen:4b', type: 'llm' }, { id: 'embed', type: 'embeddings' }] })
    throw new TypeError('Server unavailable')
  })
  vi.stubGlobal('fetch', request)
  const provider = new LocalProvider()
  await provider.configure({ endpoint: 'https://example.com/v1', apiKey: 'fixture', toolsEnabled: true })
  expect((await provider.listModels()).map(model => [model.id, model.capabilities.supportsTools])).toEqual([
    ['qwen:4b · Ollama', true], ['qwen:4b · LM Studio', true],
  ])
  expect(request.mock.calls.every(([url]) => new URL(url).hostname === '127.0.0.1')).toBe(true)
})

it('routes the chosen model to its serving runtime and rejects a disappeared model', async () => {
  let available = true
  const request = vi.fn(async (input: string | URL | Request, options?: RequestInit) => {
    if (String(input).endsWith('/api/tags')) return Response.json({ models: available ? [{ name: 'qwen:4b' }] : [] })
    expect(String(input)).toBe('http://127.0.0.1:11434/v1/chat/completions')
    expect(JSON.parse(String(options?.body)).model).toBe('qwen:4b')
    return new Response('data: {"choices":[{"delta":{"content":"Local reply"},"finish_reason":"stop"}]}\n\ndata: [DONE]\n\n',
      { headers: { 'content-type': 'text/event-stream' } })
  })
  vi.stubGlobal('fetch', request)
  const provider = new LocalProvider()
  const run = async () => {
    const response = []
    for await (const chunk of provider.createCompletion({ model: 'qwen:4b · Ollama', messages: [] })) response.push(chunk)
    return response
  }
  expect(await run()).toEqual([{ type: 'text', content: 'Local reply' }, { type: 'done' }])
  available = false
  await expect(run()).rejects.toThrow('unavailable')
  expect(request).toHaveBeenCalledTimes(3)
})
