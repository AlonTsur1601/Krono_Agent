import { useEffect, useRef, useState } from 'react'
import { useChatStore } from '@/stores/chatStore'

export function ChatView() {
  const { currentChatId, messages, sendMessage } = useChatStore()
  const [input, setInput] = useState('')
  const [mode, setMode] = useState<'chat' | 'plan' | 'agent' | 'goal'>('chat')
  const [permissions, setPermissions] = useState<'ask' | 'project' | 'full'>('ask')
  const messagesEndRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!input.trim() || !currentChatId) return

    await sendMessage(input, {
      mode,
      modelId: 'gpt-4', // TODO: Make selectable
      permissions,
    })
    setInput('')
  }

  if (!currentChatId) {
    return (
      <div style={{
        height: '100%',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'var(--text-muted)',
      }}>
        Select a chat or create a new one
      </div>
    )
  }

  return (
    <div style={{
      height: '100%',
      display: 'flex',
      flexDirection: 'column',
    }}>
      {/* Messages */}
      <div style={{
        flex: 1,
        overflowY: 'auto',
        padding: '1rem',
      }}>
        {messages.length === 0 ? (
          <div style={{
            height: '100%',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: 'var(--text-muted)',
          }}>
            Start a conversation
          </div>
        ) : (
          <div style={{ maxWidth: '820px', margin: '0 auto' }}>
            {messages.map((message) => (
              <div
                key={message.id}
                style={{
                  marginBottom: '1.5rem',
                  padding: '1rem',
                  borderRadius: '0.75rem',
                  backgroundColor: message.role === 'user' ? 'var(--bg-surface)' : 'transparent',
                  border: message.role === 'user' ? '1px solid var(--border)' : 'none',
                }}
              >
                <div style={{
                  fontSize: '0.875rem',
                  fontWeight: 600,
                  marginBottom: '0.5rem',
                  color: message.role === 'user' ? 'var(--accent)' : 'var(--text-primary)',
                }}>
                  {message.role === 'user' ? 'You' : 'Assistant'}
                </div>
                <div style={{
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                }}>
                  {message.content || (message.streaming ? '...' : '')}
                  {message.streaming && (
                    <span style={{
                      display: 'inline-block',
                      width: '8px',
                      height: '14px',
                      marginLeft: '2px',
                      backgroundColor: 'var(--accent)',
                      animation: 'blink 1s infinite',
                    }}></span>
                  )}
                </div>
              </div>
            ))}
            <div ref={messagesEndRef} />
          </div>
        )}
      </div>

      {/* Composer */}
      <div style={{
        borderTop: '1px solid var(--border)',
        padding: '1rem',
        backgroundColor: 'var(--bg-surface)',
      }}>
        <div style={{ maxWidth: '820px', margin: '0 auto' }}>
          {/* Controls */}
          <div style={{
            display: 'flex',
            gap: '1rem',
            marginBottom: '0.75rem',
            fontSize: '0.875rem',
          }}>
            <select
              value={mode}
              onChange={(e) => setMode(e.target.value as any)}
              style={{
                padding: '0.375rem 0.5rem',
                borderRadius: '0.375rem',
                border: '1px solid var(--border)',
                backgroundColor: 'var(--bg-primary)',
                color: 'var(--text-primary)',
              }}
            >
              <option value="chat">Chat</option>
              <option value="plan">Plan</option>
              <option value="agent">Agent</option>
              <option value="goal">Goal</option>
            </select>

            <select
              value={permissions}
              onChange={(e) => setPermissions(e.target.value as any)}
              style={{
                padding: '0.375rem 0.5rem',
                borderRadius: '0.375rem',
                border: '1px solid var(--border)',
                backgroundColor: 'var(--bg-primary)',
                color: 'var(--text-primary)',
              }}
            >
              <option value="ask">Ask</option>
              <option value="project">Project</option>
              <option value="full">Full Computer</option>
            </select>
          </div>

          {/* Input */}
          <form onSubmit={handleSubmit}>
            <div style={{ display: 'flex', gap: '0.5rem' }}>
              <textarea
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault()
                    handleSubmit(e)
                  }
                }}
                placeholder="Type a message... (Shift+Enter for new line)"
                rows={3}
                style={{
                  flex: 1,
                  padding: '0.75rem',
                  borderRadius: '0.75rem',
                  border: '1px solid var(--border)',
                  backgroundColor: 'var(--bg-primary)',
                  color: 'var(--text-primary)',
                  resize: 'vertical',
                  fontFamily: 'inherit',
                }}
              />
              <button
                type="submit"
                disabled={!input.trim()}
                style={{
                  padding: '0 1.5rem',
                  borderRadius: '0.75rem',
                  border: 'none',
                  backgroundColor: 'var(--accent)',
                  color: 'var(--bg-primary)',
                  fontWeight: 600,
                  cursor: input.trim() ? 'pointer' : 'not-allowed',
                  opacity: input.trim() ? 1 : 0.5,
                }}
              >
                Send
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  )
}
