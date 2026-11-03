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
  streaming?: boolean
}

interface ChatState {
  chats: Chat[]
  currentChatId: string | null
  messages: Message[]
  isLoading: boolean
  streamingMessageId: string | null
  
  loadChats: () => Promise<void>
  createChat: (name?: string) => Promise<void>
  selectChat: (chatId: string) => Promise<void>
  sendMessage: (content: string, options: {
    mode: 'chat' | 'plan' | 'agent' | 'goal'
    modelId: string
    providerId: string
    permissions: 'ask' | 'project' | 'full'
  }) => Promise<void>
  deleteChat: (chatId: string) => Promise<void>
  handleStreamChunk: (data: any) => void
}

export const useChatStore = create<ChatState>((set, get) => ({
  chats: [],
  currentChatId: null,
  messages: [],
  isLoading: false,
  streamingMessageId: null,

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

    // Add user message optimistically
    const tempUserId = `temp-user-${Date.now()}`
    const userMessage = {
      id: tempUserId,
      role: 'user' as const,
      content,
      createdAt: Date.now(),
    }
    
    // Add empty assistant message for streaming
    const tempAssistantId = `temp-assistant-${Date.now()}`
    const assistantMessage = {
      id: tempAssistantId,
      role: 'assistant' as const,
      content: '',
      createdAt: Date.now(),
      streaming: true,
    }

    set({
      messages: [...messages, userMessage, assistantMessage],
      streamingMessageId: tempAssistantId,
    })

    try {
      const result = await window.api.invoke('chat:send-message', {
        chatId: currentChatId,
        content,
        ...options,
      })

      // Replace temp messages with real ones
      set((state) => ({
        messages: state.messages.map((msg) =>
          msg.id === tempUserId ? result.userMessage :
          msg.id === tempAssistantId ? { ...result.assistantMessage, streaming: false } :
          msg
        ),
        streamingMessageId: null,
      }))
    } catch (error) {
      console.error('Failed to send message:', error)
      // Remove temp messages on error
      set((state) => ({
        messages: state.messages.filter(
          (msg) => msg.id !== tempUserId && msg.id !== tempAssistantId
        ),
        streamingMessageId: null,
      }))
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

  handleStreamChunk: (data: any) => {
    const { chatId, chunk } = data
    const { currentChatId, streamingMessageId } = get()

    if (chatId !== currentChatId || !streamingMessageId) {
      return
    }

    if (chunk.type === 'text') {
      set((state) => ({
        messages: state.messages.map((msg) =>
          msg.id === streamingMessageId
            ? { ...msg, content: msg.content + chunk.content }
            : msg
        ),
      }))
    } else if (chunk.type === 'done') {
      set((state) => ({
        messages: state.messages.map((msg) =>
          msg.id === streamingMessageId
            ? { ...msg, streaming: false }
            : msg
        ),
        streamingMessageId: null,
      }))
    }
  },
}))

// Set up stream chunk listener
if (typeof window !== 'undefined' && window.api) {
  window.api.on('chat:stream-chunk', (data: any) => {
    useChatStore.getState().handleStreamChunk(data)
  })
}
