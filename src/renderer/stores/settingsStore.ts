import { create } from 'zustand'

interface Provider {
  id: string
  name: string
  configured: boolean
}

interface Model {
  id: string
  name: string
  providerId: string
}

interface SettingsState {
  providers: Provider[]
  models: Model[]
  selectedProvider: string | null
  selectedModel: string | null
  isLoading: boolean

  loadProviders: () => Promise<void>
  loadModels: () => Promise<void>
  configureProvider: (providerId: string, apiKey: string) => Promise<void>
  selectProvider: (providerId: string) => void
  selectModel: (modelId: string) => void
}

export const useSettingsStore = create<SettingsState>((set, get) => ({
  providers: [],
  models: [],
  selectedProvider: null,
  selectedModel: null,
  isLoading: false,

  loadProviders: async () => {
    set({ isLoading: true })
    try {
      const providers = await window.api.invoke('settings:get-providers')
      set({ providers: providers as Provider[], isLoading: false })
    } catch (error) {
      console.error('Failed to load providers:', error)
      set({ isLoading: false })
    }
  },

  loadModels: async () => {
    const { selectedProvider } = get()
    if (!selectedProvider) return

    set({ isLoading: true })
    try {
      const models = await window.api.invoke('settings:get-models', selectedProvider)
      set({ models: models as Model[], isLoading: false })
    } catch (error) {
      console.error('Failed to load models:', error)
      set({ isLoading: false })
    }
  },

  configureProvider: async (providerId: string, apiKey: string) => {
    try {
      await window.api.invoke('settings:configure-provider', { providerId, apiKey })
      await get().loadProviders()
    } catch (error) {
      console.error('Failed to configure provider:', error)
      throw error
    }
  },

  selectProvider: (providerId: string) => {
    set({ selectedProvider: providerId, models: [], selectedModel: null })
    get().loadModels()
  },

  selectModel: (modelId: string) => {
    set({ selectedModel: modelId })
  },
}))
