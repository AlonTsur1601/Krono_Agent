import { useEffect, useRef, useState } from 'react'

interface Entry { url: string; title: string; at: number }
interface BrowserState { downloads?: { name: string; state: string; received: number; total: number }[]; activeId?: string; tabs: { id: string; title: string; url: string; loading: boolean; canGoBack: boolean; canGoForward: boolean }[] }
export function BrowserPanel() {
  const [state, setState] = useState<BrowserState>({ tabs: [] })
  const [address, setAddress] = useState(''), [error, setError] = useState('')
  const [saved, setSaved] = useState<{ bookmarks: Entry[]; history: Entry[] } | null>(null)
  const host = useRef<HTMLDivElement>(null)
  const tab = state.tabs.find(tab => tab.id === state.activeId)
  useEffect(() => {
    const listener = (next: BrowserState) => setState(next)
    window.api.on('browser:state', listener)
    window.api.invoke('browser:show', true).then(setState).catch(error => setError(error.message))
    const measure = () => {
      if (!host.current) return
      const rect = host.current.getBoundingClientRect()
      window.api.invoke('browser:bounds', { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.max(1, Math.round(rect.width)), height: Math.max(1, Math.round(rect.height)) }).catch(() => {})
    }
    const observer = new ResizeObserver(measure); observer.observe(host.current!)
    window.addEventListener('resize', measure)
    return () => { observer.disconnect(); window.removeEventListener('resize', measure); window.api.off('browser:state', listener); window.api.invoke('browser:show', false).catch(() => {}) }
  }, [])
  useEffect(() => { setAddress(tab?.url === 'about:blank' ? '' : tab?.url ?? '') }, [tab?.url])
  const action = (action: string, extra = {}) => { setError(''); window.api.invoke('browser:action', { action, ...extra }).then(setState).catch(error => setError(error.message)) }
  return <section style={{ height: '100%', display: 'flex', flexDirection: 'column' }} aria-label="Browser">
    <div style={{ display: 'flex', gap: 8, padding: 8, overflowX: 'auto' }}>
      {state.tabs.map(tab => <div key={tab.id} style={{ display: 'flex' }}><button aria-pressed={tab.id === state.activeId} onClick={() => action('select', { id: tab.id })}>{tab.title.slice(0, 28)}</button><button aria-label={`Close ${tab.title}`} onClick={() => action('close', { id: tab.id })}>×</button></div>)}
      <button onClick={() => action('new')}>New tab</button><button onClick={() => action('new', { privateMode: true })}>Private tab</button>
    </div>
    <form style={{ display: 'flex', gap: 8, padding: 8 }} onSubmit={event => { event.preventDefault(); const value = address.trim(); if (value) action('navigate', { url: /^https?:\/\//i.test(value) ? value : `https://www.google.com/search?q=${encodeURIComponent(value)}` }) }}>
      <button type="button" aria-label="Back" disabled={!tab?.canGoBack} onClick={() => action('back')}>←</button>
      <button type="button" aria-label="Forward" disabled={!tab?.canGoForward} onClick={() => action('forward')}>→</button>
      <button type="button" onClick={() => action(tab?.loading ? 'stop' : 'reload')}>{tab?.loading ? 'Stop' : 'Reload'}</button>
      <input aria-label="Address or search" value={address} onChange={event => setAddress(event.target.value)} style={{ flex: 1, minWidth: 100, padding: 8, background: 'var(--bg-surface)', color: 'var(--text-primary)', border: '1px solid var(--border)' }} />
      <button type="submit">Go</button>
      <button type="button" onClick={() => action('bookmark')}>Bookmark</button>
      <button type="button" onClick={() => { if (saved) setSaved(null); else window.api.invoke('browser:action', { action: 'library' }).then(setSaved).catch(error => setError(error.message)) }}>Library</button>
      <select aria-label="Browser zoom" defaultValue="1" onChange={event => action('zoom', { zoom: Number(event.target.value) })}><option value="0.75">75%</option><option value="1">100%</option><option value="1.25">125%</option><option value="1.5">150%</option></select>
    </form>
    <div style={{ display: 'flex', padding: '0 8px 8px', gap: 8 }}><label>Find in page <input aria-label="Find in page" onChange={event => action('find', { text: event.target.value })} /></label></div>
    {saved && <div style={{ maxHeight: 180, overflow: 'auto', padding: 8 }}>
      <strong>Bookmarks</strong>{saved.bookmarks.map(entry => <div key={entry.url}><button onClick={() => { action('navigate', { url: entry.url }); setSaved(null) }}>{entry.title}</button></div>)}
      <strong>History</strong><button onClick={() => { action('clear-history'); setSaved({ ...saved, history: [] }) }}>Clear history</button>
      {saved.history.map(entry => <div key={entry.url}><button onClick={() => { action('navigate', { url: entry.url }); setSaved(null) }}>{entry.title}</button></div>)}
    </div>}
    {!!state.downloads?.length && <div style={{ maxHeight: 80, overflow: 'auto', padding: 8 }}><strong>Downloads</strong>{state.downloads.map((item, index) => <div key={index}>{item.name} · {item.state} · {Math.round(item.received / 1024)} KB</div>)}</div>}
    {error && <p role="alert" style={{ padding: 8 }}>{error}</p>}
    <div ref={host} style={{ flex: 1, minHeight: 1 }} />
  </section>
}
