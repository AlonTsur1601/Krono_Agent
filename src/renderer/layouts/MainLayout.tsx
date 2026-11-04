import { ProjectsPanel } from '../features/projects/ProjectsPanel'
import { Approvals } from '../features/chat/Approvals'
import { BrowserPanel } from '../features/browser/BrowserPanel'
import { useState, useEffect, lazy, Suspense } from 'react'
import { useAuthStore } from '../stores/authStore'
import { useThemeStore } from '../stores/themeStore'
import { useChatStore } from '../stores/chatStore'
import { Snake } from '../features/games/Snake'
import { TicTacToe } from '../features/games/TicTacToe'
import { TwentyFortyEight } from '../features/games/TwentyFortyEight'
import { Minesweeper } from '../features/games/Minesweeper'
import { ConnectFour } from '../features/games/ConnectFour'
import { Breakout } from '../features/games/Breakout'
import { MemoryMatch } from '../features/games/MemoryMatch'
import { Pong } from '../features/games/Pong'
import { ChatView } from '../features/chat/ChatView'
import { SettingsView } from '../features/settings/SettingsView'

const TerminalDrawer = lazy(() => import('../features/terminal/TerminalDrawer').then(module => ({ default: module.TerminalDrawer })))

type GameId = 'snake' | 'tic-tac-toe' | '2048' | 'minesweeper' | 'connect-four' | 'breakout' | 'memory-match' | 'pong'

export function MainLayout() {
  const [terminalOpen, setTerminalOpen] = useState(false)
  const [terminalMounted, setTerminalMounted] = useState(false)
  const [activeView, setActiveView] = useState<'chat' | 'games' | 'settings' | 'browser' | 'projects'>('chat')
  const [selectedGame, setSelectedGame] = useState<GameId>('snake')
  const { user, logout } = useAuthStore()
  const { theme, setTheme } = useThemeStore()
  const { chats, currentChatId, loadChats, createChat, selectChat, deleteChat } = useChatStore()

  useEffect(() => {
    loadChats()
  }, [loadChats])

  const games = [
    { id: 'snake' as const, name: 'Snake' },
    { id: 'tic-tac-toe' as const, name: 'Tic-Tac-Toe' },
    { id: '2048' as const, name: '2048' },
    { id: 'minesweeper' as const, name: 'Minesweeper' },
    { id: 'connect-four' as const, name: 'Connect Four' },
    { id: 'breakout' as const, name: 'Breakout' },
    { id: 'memory-match' as const, name: 'Memory Match' },
    { id: 'pong' as const, name: 'Pong' },
  ]

  return (
    <div style={{
      width: '100vw',
      height: '100vh',
      display: 'flex',
      backgroundColor: 'var(--bg-primary)',
      color: 'var(--text-primary)',
    }}>
      {/* Sidebar */}
      <div style={{
        width: '248px',
        backgroundColor: 'var(--bg-sidebar)',
        borderRight: '1px solid var(--border)',
        display: 'flex',
        flexDirection: 'column',
        padding: '1rem',
        overflowY: 'auto',
      }}>
        <div style={{ marginBottom: '1.5rem' }}>
          <h1 style={{ fontSize: '1.25rem', fontWeight: 600 }}>Krono Agent</h1>
          <p style={{ fontSize: '0.75rem', opacity: 0.6, marginTop: '0.25rem' }}>
            {user?.email}
          </p>
        </div>

        {activeView === 'chat' && (
          <div style={{ marginBottom: '1rem' }}>
            <button
              onClick={() => createChat()}
              style={{
                width: '100%',
                padding: '0.5rem',
                borderRadius: '0.375rem',
                border: '1px solid var(--border)',
                backgroundColor: 'var(--accent)',
                color: 'var(--bg-primary)',
                fontWeight: 600,
                cursor: 'pointer',
              }}
            >
              + New Chat
            </button>
          </div>
        )}

        <nav style={{ flex: 1, marginBottom: '1rem' }}>
          <button onClick={() => setActiveView('projects')} style={{ width: '100%', padding: 12, color: 'var(--text-primary)', background: activeView === 'projects' ? 'var(--bg-surface)' : 'transparent', border: 'none', textAlign: 'left' }}>Projects & memory</button>
          <button onClick={() => setActiveView('browser')} style={{ width: '100%', padding: 12, color: 'var(--text-primary)', background: activeView === 'browser' ? 'var(--bg-surface)' : 'transparent', border: 'none', textAlign: 'left' }}>Browser</button>
          <div style={{ marginBottom: '1rem' }}>
            <button
              onClick={() => setActiveView('chat')}
              style={{
                width: '100%',
                padding: '0.75rem',
                marginBottom: '0.5rem',
                borderRadius: '0.375rem',
                border: 'none',
                backgroundColor: activeView === 'chat' ? 'var(--bg-surface)' : 'transparent',
                color: 'var(--text-primary)',
                textAlign: 'left',
                cursor: 'pointer',
              }}
            >
              Chat
            </button>
            <button
              onClick={() => setActiveView('games')}
              style={{
                width: '100%',
                padding: '0.75rem',
                marginBottom: '0.5rem',
                borderRadius: '0.375rem',
                border: 'none',
                backgroundColor: activeView === 'games' ? 'var(--bg-surface)' : 'transparent',
                color: 'var(--text-primary)',
                textAlign: 'left',
                cursor: 'pointer',
              }}
            >
              Games
            </button>
            <button
              onClick={() => setActiveView('settings')}
              style={{
                width: '100%',
                padding: '0.75rem',
                borderRadius: '0.375rem',
                border: 'none',
                backgroundColor: activeView === 'settings' ? 'var(--bg-surface)' : 'transparent',
                color: 'var(--text-primary)',
                textAlign: 'left',
                cursor: 'pointer',
              }}
            >
              Settings
            </button>
          </div>

          {activeView === 'chat' && chats.length > 0 && (
            <div>
              <div style={{ fontSize: '0.75rem', opacity: 0.6, marginBottom: '0.5rem' }}>
                Recent Chats
              </div>
              {chats.map((chat) => (
                <div
                  key={chat.id}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.5rem',
                    marginBottom: '0.25rem',
                  }}
                >
                  <button
                    onClick={() => selectChat(chat.id)}
                    style={{
                      flex: 1,
                      padding: '0.5rem',
                      borderRadius: '0.375rem',
                      border: 'none',
                      backgroundColor: currentChatId === chat.id ? 'var(--bg-surface)' : 'transparent',
                      color: 'var(--text-primary)',
                      textAlign: 'left',
                      cursor: 'pointer',
                      fontSize: '0.875rem',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {chat.name}
                  </button>
                  <button
                    onClick={() => deleteChat(chat.id)}
                    style={{
                      padding: '0.25rem 0.5rem',
                      borderRadius: '0.375rem',
                      border: 'none',
                      backgroundColor: 'transparent',
                      color: 'var(--text-muted)',
                      cursor: 'pointer',
                      fontSize: '0.75rem',
                    }}
                  >
                    ×
                  </button>
                </div>
              ))}
            </div>
          )}

          {activeView === 'games' && (
            <div>
              <div style={{ fontSize: '0.75rem', opacity: 0.6, marginBottom: '0.5rem' }}>
                Available Games (8/24)
              </div>
              {games.map((game) => (
                <button
                  key={game.id}
                  onClick={() => setSelectedGame(game.id)}
                  style={{
                    width: '100%',
                    padding: '0.5rem',
                    marginBottom: '0.25rem',
                    borderRadius: '0.375rem',
                    border: 'none',
                    backgroundColor: selectedGame === game.id ? 'var(--bg-surface)' : 'transparent',
                    color: 'var(--text-primary)',
                    textAlign: 'left',
                    cursor: 'pointer',
                    fontSize: '0.875rem',
                  }}
                >
                  {game.name}
                </button>
              ))}
            </div>
          )}
        </nav>

        <div style={{ borderTop: '1px solid var(--border)', paddingTop: '1rem' }}>
          <div style={{ marginBottom: '1rem' }}>
            <label style={{ display: 'block', fontSize: '0.75rem', marginBottom: '0.5rem' }}>
              Theme
            </label>
            <select
              value={theme}
              onChange={(e) => setTheme(e.target.value as any)}
              style={{
                width: '100%',
                padding: '0.5rem',
                borderRadius: '0.375rem',
                border: '1px solid var(--border)',
                backgroundColor: 'var(--bg-surface)',
                color: 'var(--text-primary)',
              }}
            >
              <option value="dark">Dark</option>
              <option value="light">Light</option>
              <option value="halloween">Halloween</option>
              <option value="system">System</option>
            </select>
          </div>
          <button
            onClick={logout}
            style={{
              width: '100%',
              padding: '0.5rem',
              borderRadius: '0.375rem',
              border: '1px solid var(--border)',
              backgroundColor: 'transparent',
              color: 'var(--text-primary)',
              cursor: 'pointer',
            }}
          >
            Sign Out
          </button>
        </div>
      </div>

      {/* Main Content */}
      <div style={{ flex: 1, minWidth: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
      <Approvals />
      <div style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
        {activeView === 'projects' && <ProjectsPanel onOpenChat={() => setActiveView('chat')} />}
        {activeView === 'browser' && <BrowserPanel />}
        {activeView === 'chat' && <ChatView />}
        {activeView === 'games' && selectedGame === 'snake' && <Snake />}
        {activeView === 'games' && selectedGame === 'tic-tac-toe' && <TicTacToe />}
        {activeView === 'games' && selectedGame === '2048' && <TwentyFortyEight />}
        {activeView === 'games' && selectedGame === 'minesweeper' && <Minesweeper />}
        {activeView === 'games' && selectedGame === 'connect-four' && <ConnectFour />}
        {activeView === 'games' && selectedGame === 'breakout' && <Breakout />}
        {activeView === 'games' && selectedGame === 'memory-match' && <MemoryMatch />}
        {activeView === 'games' && selectedGame === 'pong' && <Pong />}
        {activeView === 'settings' && <SettingsView />}
      </div>
      <button onClick={() => { setTerminalMounted(true); setTerminalOpen(!terminalOpen) }} aria-expanded={terminalOpen} style={{ textAlign: 'left', padding: '8px 16px', color: 'var(--text-primary)', background: 'var(--bg-sidebar)', border: '1px solid var(--border)' }}>Terminal {terminalOpen ? '−' : '+'}</button>
      <div style={{ display: terminalOpen ? 'block' : 'none' }}>{terminalMounted && <Suspense fallback={<p>Opening terminal…</p>}><TerminalDrawer /></Suspense>}</div>
      </div>
    </div>
  )
}
