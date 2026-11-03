import { useEffect, useRef, useState } from 'react'
import { Terminal } from 'xterm'
import { FitAddon } from 'xterm-addon-fit'
import 'xterm/css/xterm.css'

export function TerminalPanel() {
  const container = useRef<HTMLDivElement>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    const terminal = new Terminal({ fontSize: 13, fontFamily: 'Consolas, monospace', theme: { background: '#111316' }, scrollback: 3000 })
    const fit = new FitAddon(); terminal.loadAddon(fit)
    terminal.open(container.current!)
    let id: string | undefined, disposed = false
    const pending: { id: string; data: string }[] = []
    const output = (event: { id: string; data: string }) => {
      if (!id) pending.push(event)
      else if (event.id === id) terminal.write(event.data)
    }
    window.api.on('terminal:data', output)
    const input = terminal.onData(data => { if (id) window.api.invoke('terminal:write', { id, data }).catch(() => {}) })
    const resize = new ResizeObserver(() => {
      if (!container.current?.offsetWidth) return
      fit.fit()
      if (id) window.api.invoke('terminal:resize', { id, cols: Math.max(10, terminal.cols), rows: Math.max(2, terminal.rows) }).catch(() => {})
    })
    resize.observe(container.current!)
    window.api.invoke('terminal:create').then(result => {
      id = result.id
      if (disposed) { window.api.invoke('terminal:close', id).catch(() => {}); return }
      for (const event of pending) if (event.id === id) terminal.write(event.data)
      pending.length = 0; fit.fit(); terminal.focus()
    }).catch(error => { if (!disposed) setError(error instanceof Error ? error.message : 'Terminal unavailable') })
    return () => {
      disposed = true; resize.disconnect(); input.dispose(); terminal.dispose()
      window.api.off('terminal:data', output)
      if (id) window.api.invoke('terminal:close', id).catch(() => {})
    }
  }, [])
  return <section aria-label="Integrated terminal" style={{ height: 250, display: 'flex', flexDirection: 'column', borderTop: '1px solid var(--border)', padding: 12 }}>
    <div style={{ fontSize: 12, marginBottom: 8 }}>User terminal · {navigator.platform.includes('Win') ? 'PowerShell' : 'Shell'}</div>
    {error && <p role="alert">{error}</p>}
    <div ref={container} style={{ flex: 1, minHeight: 0 }} />
  </section>
}
