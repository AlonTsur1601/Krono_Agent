import { ipcMain, WebContents } from 'electron'
import { randomUUID } from 'crypto'
import { z } from 'zod'
import type { ApprovalRequest, ApprovalResult } from '../agent/types'

const pending = new Map<string, { owner: WebContents; finish: (approved: boolean) => void }>()
export function requestApproval(owner: WebContents, chatId: string, request: ApprovalRequest, signal: AbortSignal): Promise<ApprovalResult> {
  if (signal.aborted || owner.isDestroyed()) return Promise.resolve({ approved: false })
  const id = randomUUID()
  return new Promise(resolve => {
    const abort = () => finish(false)
    const finish = (approved: boolean) => {
      pending.delete(id); signal.removeEventListener('abort', abort)
      if (!owner.isDestroyed()) owner.send('chat:approval', { id, resolved: true })
      resolve({ approved })
    }
    pending.set(id, { owner, finish }); signal.addEventListener('abort', abort, { once: true })
    owner.send('chat:approval', { ...request, id, chatId, resolved: false })
  })
}
export function setupApprovalHandlers() {
  ipcMain.handle('chat:approve', (event, input) => {
    const request = z.object({ id: z.string().uuid(), approved: z.boolean() }).parse(input)
    const approval = pending.get(request.id)
    if (!approval || approval.owner !== event.sender) throw new Error('Approval not found')
    approval.finish(request.approved)
  })
}
