import { useEffect, useState } from 'react'

interface PendingApproval { id: string; action: string; target: string; details: Record<string, unknown>; resolved: boolean }
export function Approvals() {
  const [pending, setPending] = useState<PendingApproval[]>([])
  const [error, setError] = useState('')
  useEffect(() => {
    const listener = (request: PendingApproval) => setPending(current => request.resolved ? current.filter(item => item.id !== request.id) : [...current, request])
    window.api.on('chat:approval', listener)
    return () => window.api.off('chat:approval', listener)
  }, [])
  if (!pending.length) return null
  const request = pending[0]
  const answer = (approved: boolean) => window.api.invoke('chat:approve', { id: request.id, approved }).catch(error => setError(error.message))
  return <section aria-label="Pending approvals" style={{ padding: 12, background: 'var(--bg-surface)', borderBottom: '1px solid var(--border)', maxHeight: 220, overflow: 'auto' }}>
    <strong>{pending.length} action{pending.length === 1 ? '' : 's'} waiting for approval</strong>
    <p>{request.action} · {request.target}</p>
    <details><summary>Review action</summary><pre style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{JSON.stringify(request.details, null, 2)}</pre></details>
    <button onClick={() => answer(false)}>Deny</button> <button onClick={() => answer(true)}>Allow this action</button>
    {error && <p role="alert">{error}</p>}
  </section>
}
