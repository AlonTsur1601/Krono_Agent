import { create } from 'zustand'

interface AuthState {
  user: { id: string; email: string } | null
  isAuthenticated: boolean
  isLoading: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  setUser: (user: { id: string; email: string } | null) => void
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  isAuthenticated: false,
  isLoading: false,

  login: async (email: string, password: string) => {
    set({ isLoading: true })
    try {
      const result = await window.api.invoke('auth:login', { email, password })
      set({ user: result.user, isAuthenticated: true, isLoading: false })
    } catch (error) {
      set({ isLoading: false })
      throw error
    }
  },

  register: async (email: string, password: string) => {
    set({ isLoading: true })
    try {
      const result = await window.api.invoke('auth:register', { email, password })
      set({ user: result.user, isAuthenticated: true, isLoading: false })
    } catch (error) {
      set({ isLoading: false })
      throw error
    }
  },

  logout: async () => {
    try {
      await window.api.invoke('auth:logout')
      set({ user: null, isAuthenticated: false })
    } catch (error) {
      console.error('Logout failed:', error)
    }
  },

  setUser: (user) => {
    set({ user, isAuthenticated: !!user })
  },
}))
