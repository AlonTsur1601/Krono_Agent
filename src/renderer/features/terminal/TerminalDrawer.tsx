import { useRef, useState } from 'react'
import { TerminalPanel } from './TerminalPanel'

export function TerminalDrawer() {
  const next = useRef(2)
  const [tabs, setTabs] = useState([1]), [active, setActive] = useState(1)
  return <div style={{ height: 280, minHeight: 140, maxHeight: '65vh', resize: 'vertical', overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
    <div role="tablist" aria-label="Terminal sessions" style={{ display: 'flex', gap: 8, padding: 8, borderBottom: '1px solid var(--border)' }}>
      {tabs.map(id => <div key={id} style={{ display: 'flex' }}><button role="tab" aria-selected={id === active} onClick={() => setActive(id)}>Terminal {id}</button><button aria-label={`Close terminal ${id}`} onClick={() => { setTabs(tabs.filter(tab => tab !== id)); if (active === id) setActive(tabs.find(tab => tab !== id) ?? 0) }}>×</button></div>)}
      <button onClick={() => { const id = next.current++; setTabs([...tabs, id]); setActive(id) }}>New terminal</button>
    </div>
    {tabs.map(id => <div key={id} role="tabpanel" style={{ display: id === active ? 'flex' : 'none', flex: 1, minHeight: 0 }}><TerminalPanel /></div>)}
  </div>
}
