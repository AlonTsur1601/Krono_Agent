import { create } from 'zustand'

interface Chat {
  id: string
  name: string
  createdAt: number
  updatedAt: number
}

interface Message {
  id: string
  role: 'user' | 'assistant' | 'system'
  content: string
  createdAt: number
}

interface ChatState {
  chats: Chat[]
  currentChatId: string | null
  messages: Message[]
  isLoading: boolean
  
  loadChats: () => Promise<void>
  createChat: (name?: string) => Promise<void>
  selectChat: (chatId: string) => Promise<void>
  sendMessage: (content: string, options: {
    mode: 'chat' | 'plan' | 'agent' | 'goal'
    modelId: string
    permissions: 'ask' | 'project' | 'full'
  }) => Promise<void>
  deleteChat: (chatId: string) => Promise<void>
}

export const useChatStore = create<ChatState>((set, get) => ({
  chats: [],
  currentChatId: null,
  messages: [],
  isLoading: false,

  loadChats: async () => {
    set({ isLoading: true })
    try {
      const chats = await window.api.invoke('chat:list')
      set({ chats, isLoading: false })
    } catch (error) {
      console.error('Failed to load chats:', error)
      set({ isLoading: false })
    }
  },

  createChat: async (name?: string) => {
    try {
      const chat = await window.api.invoke('chat:create', { name })
      set({ chats: [chat, ...get().chats], currentChatId: chat.id, messages: [] })
    } catch (error) {
      console.error('Failed to create chat:', error)
    }
  },

  selectChat: async (chatId: string) => {
    set({ isLoading: true })
    try {
      const chat = await window.api.invoke('chat:get', chatId)
      set({
        currentChatId: chatId,
        messages: chat.messages || [],
        isLoading: false,
      })
    } catch (error) {
      console.error('Failed to select chat:', error)
      set({ isLoading: false })
    }
  },

  sendMessage: async (content: string, options) => {
    const { currentChatId, messages } = get()
    if (!currentChatId) return

    // Optimistic update
    const tempMessage = {
      id: 'temp',
      role: 'user' as const,
      content,
      createdAt: Date.now(),
    }
    set({ messages: [...messages, tempMessage] })

    try {
      const result = await window.api.invoke('chat:send-message', {
        chatId: currentChatId,
        content,
        ...options,
      })

      // Replace temp message with real messages
      set({
        messages: [
          ...messages,
          result.userMessage,
          result.assistantMessage,
        ],
      })
    } catch (error) {
      console.error('Failed to send message:', error)
      // Remove temp message on error
      set({ messages })
    }
  },

  deleteChat: async (chatId: string) => {
    try {
      await window.api.invoke('chat:delete', chatId)
      const newChats = get().chats.filter((c) => c.id !== chatId)
      const updates: any = { chats: newChats }
      
      if (get().currentChatId === chatId) {
        updates.currentChatId = null
        updates.messages = []
      }
      
      set(updates)
    } catch (error) {
      console.error('Failed to delete chat:', error)
    }
  },
}))
