import { useEffect } from 'react'
import { useAuthStore } from './stores/authStore'
import { useThemeStore } from './stores/themeStore'
import { AuthForm } from './features/auth/AuthForm'
import { TitleBar } from './layouts/TitleBar'
import { MainLayout } from './layouts/MainLayout'

function App() {
  const { isAuthenticated } = useAuthStore()
  const { effectiveTheme } = useThemeStore()

  useEffect(() => {
    // Apply initial theme
    document.documentElement.className = `theme-${effectiveTheme}`
  }, [effectiveTheme])

  useEffect(() => { window.api.invoke('auth:restore').then(result => useAuthStore.getState().setUser(result.user)).catch(() => {}) }, [])

  if (!isAuthenticated) {
    return (
      <div style={{
        width: '100vw',
        height: 'calc(100vh - 44px)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--bg-primary)',
        color: 'var(--text-primary)',
      }}>
        <TitleBar />
        <AuthForm />
      </div>
    )
  }

  return <><TitleBar /><MainLayout /></>
}

export default App
