import { useEffect } from 'react'
import { useAuthStore } from './stores/authStore'
import { useThemeStore } from './stores/themeStore'
import { AuthForm } from './features/auth/AuthForm'
import { MainLayout } from './layouts/MainLayout'

function App() {
  const { isAuthenticated } = useAuthStore()
  const { effectiveTheme } = useThemeStore()

  useEffect(() => {
    // Apply initial theme
    document.documentElement.className = `theme-${effectiveTheme}`
  }, [effectiveTheme])

  if (!isAuthenticated) {
    return (
      <div style={{
        width: '100vw',
        height: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--bg-primary)',
        color: 'var(--text-primary)',
      }}>
        <AuthForm />
      </div>
    )
  }

  return <MainLayout />
}

export default App
