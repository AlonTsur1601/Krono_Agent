import { useState } from 'react'
import { useAuthStore } from '../stores/authStore'
import { useThemeStore } from '../stores/themeStore'
import { Snake } from '../features/games/Snake'

export function MainLayout() {
  const [activeView, setActiveView] = useState<'chat' | 'games'>('chat')
  const { user, logout } = useAuthStore()
  const { theme, setTheme } = useThemeStore()

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
      }}>
        <div style={{ marginBottom: '2rem' }}>
          <h1 style={{ fontSize: '1.25rem', fontWeight: 600 }}>Krono Agent</h1>
          <p style={{ fontSize: '0.75rem', opacity: 0.6, marginTop: '0.25rem' }}>
            {user?.email}
          </p>
        </div>

        <nav style={{ flex: 1 }}>
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
      <div style={{ flex: 1, overflow: 'auto' }}>
        {activeView === 'chat' && (
          <div style={{
            height: '100%',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexDirection: 'column',
          }}>
            <h2 style={{ fontSize: '1.5rem', marginBottom: '1rem' }}>Chat Coming Soon</h2>
            <p style={{ opacity: 0.7 }}>
              Model providers and chat interface in development
            </p>
          </div>
        )}
        {activeView === 'games' && <Snake />}
      </div>
    </div>
  )
}
