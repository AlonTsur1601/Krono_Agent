// @ts-expect-error Vite resolves the bundled SVG asset.
import logo from '../../../assets/logo/logo.svg'

export function TitleBar() {
  return <header className="titlebar">
    <img src={logo} alt="" width="25" height="25" />
    <button className="app-menu" aria-label="Application menu" onClick={() => window.api.invoke('window:action', 'menu')}>☰</button>
    <span className="window-name">Krono Agent</span>
    <div className="window-controls">
      <button aria-label="Minimize window" onClick={() => window.api.invoke('window:action', 'minimize')}>−</button>
      <button aria-label="Maximize or restore window" onClick={() => window.api.invoke('window:action', 'maximize')}>□</button>
      <button aria-label="Close window" onClick={() => window.api.invoke('window:action', 'close')}>×</button>
    </div>
  </header>
}
