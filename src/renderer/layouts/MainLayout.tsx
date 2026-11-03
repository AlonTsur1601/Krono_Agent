import { useState, useEffect } from 'react'
import { useAuthStore } from '../stores/authStore'
import { useThemeStore } from '../stores/themeStore'
import { useChatStore } from '../stores/chatStore'
import { Snake } from '../features/games/Snake'
import { ChatView } from '../features/chat/ChatView'

export function MainLayout() {
  const [activeView, setActiveView] = useState<'chat' | 'games'>('chat')
  const { user, logout } = useAuthStore()
  const { theme, setTheme } = useThemeStore()
  const { chats, currentChatId, loadChats, createChat, selectChat, deleteChat } = useChatStore()

  useEffect(() => {
    loadChats()
  }, [loadChats])

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
      <div style={{ flex: 1, overflow: 'hidden' }}>
        {activeView === 'chat' && <ChatView />}
        {activeView === 'games' && <Snake />}
      </div>
    </div>
  )
}
