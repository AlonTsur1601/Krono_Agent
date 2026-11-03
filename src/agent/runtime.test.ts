import { describe, it, expect } from 'vitest'
import { z } from 'zod'
import { AgentRuntime } from './runtime'
import type { CompletionRequest, ProviderAdapter, ToolDefinition } from './types'

function provider(responses: any[][]) {
  const requests: CompletionRequest[] = []
  const adapter = {
    id: 'test',
    async *createCompletion(request: CompletionRequest) {
      requests.push(structuredClone({ ...request, signal: undefined }))
      for (const chunk of responses.shift() ?? [{ type: 'done' }]) yield chunk
    },
  } as unknown as ProviderAdapter
  return { adapter, requests }
}
const call = { type: 'tool_call', toolCall: { id: 'call-1', name: 'write_test', arguments: { value: 'ok' } } }
describe('agent execution boundaries', () => {
  it('omits tools in Chat mode and explains the limitation', async () => {
    const runtime = new AgentRuntime()
    const fake = provider([[{ type: 'text', content: 'Hi' }, { type: 'done' }]])
    runtime.registerProvider(fake.adapter)
    for await (const _ of runtime.execute([], { mode: 'chat', permissions: 'full', modelId: 'test', providerId: 'test' })) { void _ }
    expect(fake.requests[0].tools).toBeUndefined()
    expect(fake.requests[0].messages[0].content).toContain('cannot use any tools')
  })
  it('rejects a mutating tool requested in Plan mode before execution', async () => {
    const runtime = new AgentRuntime()
    runtime.registerProvider(provider([[call]]).adapter)
    let executed = false
    runtime.registerTools([{ name: 'write_test', description: '', inputSchema: z.object({ value: z.string() }), capabilities: { requiresPermission: 'write', canCancel: false, longRunning: false }, execute: async () => { executed = true; return { success: true } } }])
    await expect((async () => { for await (const _ of runtime.execute([], { mode: 'plan', permissions: 'full', modelId: 'test', providerId: 'test' })) { void _ } })()).rejects.toThrow('unavailable')
    expect(executed).toBe(false)
  })
  it('denies writes without approval and sends tool results back after approval', async () => {
    const tool: ToolDefinition = { name: 'write_test', description: '', inputSchema: z.object({ value: z.string() }), capabilities: { requiresPermission: 'write', canCancel: false, longRunning: false }, execute: async () => ({ success: true, output: 'written' }) }
    for (const approved of [false, true]) {
      const runtime = new AgentRuntime()
      const fake = provider([[call], [{ type: 'text', content: 'Complete' }]])
      runtime.registerProvider(fake.adapter); runtime.registerTools([tool])
      const run = async () => { const chunks = []; for await (const chunk of runtime.execute([], { mode: 'agent', permissions: 'ask', modelId: 'test', providerId: 'test', requestApproval: async () => ({ approved }) })) chunks.push(chunk); return chunks }
      if (!approved) await expect(run()).rejects.toThrow('approval')
      else { await run(); expect(fake.requests[1].messages.at(-1)?.role).toBe('tool'); expect(fake.requests[0].tools?.find(t => t.name === 'write_test')?.inputSchema).toHaveProperty('properties.value') }
    }
  })
})
