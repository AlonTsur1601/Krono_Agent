import { beforeEach, describe, expect, it, vi } from 'vitest'
const { create } = vi.hoisted(() => ({ create: vi.fn() }))
vi.mock('openai', () => ({ default: class { responses = { create }; models = { list: vi.fn() } } }))
import { OpenAIProvider } from './openai'

async function* events(items: unknown[]) { for (const item of items) yield item }
describe('OpenAI Responses transport', () => {
  beforeEach(() => create.mockReset())
  it('uses Responses without storing content and omits tools for plain chat', async () => {
    create.mockResolvedValue(events([{ type: 'response.output_text.delta', delta: 'Hello' }, { type: 'response.completed', response: { usage: { input_tokens: 1, output_tokens: 2, total_tokens: 3 } } }]))
    const provider = new OpenAIProvider(); await provider.configure({ apiKey: 'test-placeholder' })
    const chunks = []
    for await (const chunk of provider.createCompletion({ model: 'test-model', messages: [{ role: 'user', content: 'Hi' }] })) chunks.push(chunk)
    expect(create.mock.calls[0][0].tools).toBeUndefined()
    expect(create.mock.calls[0][0].store).toBe(false)
    expect(chunks[0]).toEqual({ type: 'text', content: 'Hello' })
    expect(chunks.at(-1)?.usage?.totalTokens).toBe(3)
  })
  it('parses complete tool arguments and returns function outputs on the wire', async () => {
    create.mockResolvedValue(events([
      { type: 'response.function_call_arguments.delta', delta: '{"path":' },
      { type: 'response.output_item.done', item: { type: 'function_call', call_id: 'c1', name: 'read_file', arguments: '{"path":"test.txt"}' } },
    ]))
    const provider = new OpenAIProvider(); await provider.configure({ apiKey: 'test-placeholder' })
    const chunks = []
    for await (const chunk of provider.createCompletion({ model: 'test-model', messages: [{ role: 'tool', toolCallId: 'previous', content: 'result' }] })) chunks.push(chunk)
    expect(chunks).toHaveLength(1)
    expect(chunks[0].toolCall?.arguments).toEqual({ path: 'test.txt' })
    expect(create.mock.calls[0][0].input[0]).toEqual({ type: 'function_call_output', call_id: 'previous', output: 'result' })
  })
  it('propagates cancellation and provider failure', async () => {
    create.mockResolvedValue(events([{ type: 'response.failed' }]))
    const provider = new OpenAIProvider(); await provider.configure({ apiKey: 'test-placeholder' })
    const controller = new AbortController()
    await expect((async () => { for await (const item of provider.createCompletion({ model: 'test-model', messages: [], signal: controller.signal })) void item })()).rejects.toThrow('could not complete')
    expect(create.mock.calls[0][1].signal).toBe(controller.signal)
  })
})
