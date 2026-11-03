import { create } from 'zustand'
import type { Theme } from '@/shared/types'

interface ThemeState {
  theme: Theme
  setTheme: (theme: Theme) => void
  effectiveTheme: 'dark' | 'light' | 'halloween'
}

const getSystemTheme = (): 'dark' | 'light' => {
  if (typeof window === 'undefined') return 'dark'
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export const useThemeStore = create<ThemeState>((set) => ({
  theme: 'dark',
  
  setTheme: (theme: Theme) => {
    set({ theme })
    
    // Persist theme preference
    localStorage.setItem('krono-theme', theme)
    
    // Update effective theme
    const effectiveTheme = theme === 'system' ? getSystemTheme() : theme
    set({ effectiveTheme })
    
    // Apply theme class to document
    document.documentElement.className = `theme-${effectiveTheme}`
  },
  
  effectiveTheme: 'dark',
}))

// Listen for system theme changes
if (typeof window !== 'undefined') {
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    const store = useThemeStore.getState()
    if (store.theme === 'system') {
      store.setTheme('system')
    }
  })
}
