import { describe, it, expect, vi } from 'vitest'
import type { WebContents } from 'electron'
const { handle } = vi.hoisted(() => ({ handle: vi.fn() }))
vi.mock('electron', () => ({ ipcMain: { handle } }))
import { requestApproval, setupApprovalHandlers } from './approvals'

describe('action-bound approvals', () => {
  it('allows only the requesting window to answer, once', async () => {
    setupApprovalHandlers()
    const answer = handle.mock.calls.find(call => call[0] === 'chat:approve')![1]
    const owner = { isDestroyed: () => false, send: vi.fn() } as unknown as WebContents
    const controller = new AbortController()
    const result = requestApproval(owner, 'chat', { id: 'model-call', action: 'write_file', target: 'test.txt', details: { content: 'reviewed content' } }, controller.signal)
    const payload = vi.mocked(owner.send).mock.calls[0][1]
    expect(() => answer({ sender: {} }, { id: payload.id, approved: true })).toThrow('not found')
    answer({ sender: owner }, { id: payload.id, approved: true })
    expect(await result).toEqual({ approved: true })
    expect(() => answer({ sender: owner }, { id: payload.id, approved: true })).toThrow('not found')
  })
  it('resolves as denied when the run is cancelled', async () => {
    const owner = { isDestroyed: () => false, send: vi.fn() } as unknown as WebContents
    const controller = new AbortController()
    const result = requestApproval(owner, 'chat', { id: 'call', action: 'write_file', target: 'test.txt', details: {} }, controller.signal)
    controller.abort()
    expect(await result).toEqual({ approved: false })
    expect(vi.mocked(owner.send).mock.calls.at(-1)?.[1].resolved).toBe(true)
  })
})
