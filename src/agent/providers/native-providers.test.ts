import { beforeEach, describe, expect, it, vi } from 'vitest'
const mocks = vi.hoisted(() => ({ anthropicCreate: vi.fn(), anthropicModels: vi.fn(), googleStream: vi.fn(), googleModels: vi.fn() }))
vi.mock('@anthropic-ai/sdk', () => ({ default: class { messages = { create: mocks.anthropicCreate }; models = { list: mocks.anthropicModels } } }))
vi.mock('@google/genai', () => ({ GoogleGenAI: class { models = { generateContentStream: mocks.googleStream, list: mocks.googleModels } } }))
import { AnthropicProvider } from './anthropic'
import { GoogleProvider } from './google'
import { AgentRuntime } from '../runtime'

async function* events(items: unknown[]) { for (const item of items) yield item }
describe('native provider contracts', () => {
  beforeEach(() => Object.values(mocks).forEach(mock => mock.mockReset()))
  it('Anthropic assembles fragmented input and coalesces parallel tool results', async () => {
    mocks.anthropicCreate.mockResolvedValue(events([
      { type: 'message_start', message: { usage: { input_tokens: 9, output_tokens: 0 } } },
      { type: 'content_block_start', index: 0, content_block: { type: 'tool_use', id: 'call', name: 'read_file', input: {} } },
      { type: 'content_block_delta', index: 0, delta: { type: 'input_json_delta', partial_json: '{"path":' } },
      { type: 'content_block_delta', index: 0, delta: { type: 'input_json_delta', partial_json: '"a.txt"}' } },
      { type: 'content_block_stop', index: 0 },
      { type: 'message_delta', usage: { output_tokens: 4 } },
    ]))
    const provider = new AnthropicProvider(); await provider.configure({ apiKey: 'test-placeholder' })
    const signal = new AbortController().signal
    const output = []
    for await (const chunk of provider.createCompletion({ model: 'test', signal, messages: [
      { role: 'assistant', content: '', toolCalls: [{ id: 'a', name: 'read_file', arguments: {} }, { id: 'b', name: 'read_file', arguments: {} }] },
      { role: 'tool', toolCallId: 'a', content: 'first' }, { role: 'tool', toolCallId: 'b', content: 'second' },
    ] })) output.push(chunk)
    expect(output[0].toolCall?.arguments).toEqual({ path: 'a.txt' })
    expect(output.at(-1)?.usage?.totalTokens).toBe(13)
    expect(mocks.anthropicCreate.mock.calls[0][0].messages[1].content).toHaveLength(2)
    expect(mocks.anthropicCreate.mock.calls[0][1].signal).toBe(signal)
  })
  it('Gemini preserves signed model parts and exact function IDs without displaying hidden thoughts', async () => {
    mocks.googleStream.mockResolvedValue(events([{ candidates: [{ content: { parts: [{ text: 'private reasoning', thought: true, thoughtSignature: 'opaque' }, { functionCall: { id: 'g1', name: 'read_file', args: { path: 'a.txt' } }, thoughtSignature: 'signature' }] } }] }]))
    const provider = new GoogleProvider(); await provider.configure({ apiKey: 'test-placeholder' })
    const output = []
    for await (const chunk of provider.createCompletion({ model: 'test', messages: [{ role: 'system', content: 'Instructions' }, { role: 'user', content: 'Read' }] })) output.push(chunk)
    expect(output.some(chunk => chunk.type === 'text')).toBe(false)
    const state = output.find(chunk => chunk.type === 'provider_state')!.providerState
    mocks.googleStream.mockResolvedValue(events([{ candidates: [{ content: { parts: [{ text: 'Done' }] } }] }]))
    for await (const chunk of provider.createCompletion({ model: 'test', messages: [
      { role: 'assistant', content: '', providerState: state, toolCalls: [output.find(chunk => chunk.toolCall)!.toolCall!] },
      { role: 'tool', toolCallId: 'g1', content: 'file result' },
    ] })) void chunk
    const request = mocks.googleStream.mock.calls[1][0]
    expect(request.contents[0].parts).toEqual(state!.googleParts)
    expect(request.contents[1].parts[0].functionResponse).toEqual({ id: 'g1', name: 'read_file', response: { output: 'file result' } })
  })
  it('Chat mode sends Gemini no tools and runtime does not expose provider-only state', async () => {
    mocks.googleStream.mockResolvedValue(events([{ candidates: [{ content: { parts: [{ text: 'hidden', thought: true }, { text: 'Visible reply' }] } }] }]))
    const provider = new GoogleProvider(); await provider.configure({ apiKey: 'test-placeholder' })
    const runtime = new AgentRuntime(); runtime.registerProvider(provider)
    const chunks = []
    for await (const chunk of runtime.execute([{ role: 'user', content: 'Hello' }], { mode: 'chat', permissions: 'full', modelId: 'test', providerId: 'google' })) chunks.push(chunk)
    expect(mocks.googleStream.mock.calls[0][0].config.tools).toBeUndefined()
    expect(chunks.map(chunk => chunk.content).filter(Boolean)).toEqual(['Visible reply'])
    expect(chunks.some(chunk => chunk.type === 'provider_state')).toBe(false)
  })
  it('discovers models from provider catalogs rather than stale hardcoded IDs', async () => {
    mocks.anthropicModels.mockReturnValue(events([{ id: 'current', display_name: 'Current', lifecycle: 'active' }, { id: 'retired', lifecycle: 'retired' }]))
    mocks.googleModels.mockResolvedValue(events([{ name: 'models/current', supportedActions: ['generateContent'], inputTokenLimit: 123 }, { name: 'models/embedding', supportedActions: ['embedContent'] }]))
    const anthropic = new AnthropicProvider(); await anthropic.configure({ apiKey: 'test-placeholder' })
    const google = new GoogleProvider(); await google.configure({ apiKey: 'test-placeholder' })
    expect((await anthropic.listModels()).map(model => model.id)).toEqual(['current'])
    expect((await google.listModels()).map(model => model.id)).toEqual(['current'])
  })
})
