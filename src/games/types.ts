// Game Module Interface

export interface GameModule {
  id: string
  name: string
  category: 'puzzle' | 'arcade' | 'strategy'
  description: string
  thumbnailUrl: string
  
  // Lifecycle
  mount(canvas: HTMLCanvasElement): void
  unmount(): void
  pause(): void
  resume(): void
  restart(): void
  
  // Input
  handleInput(event: InputEvent): void
  
  // State
  serialize(): GameState
  deserialize(state: GameState): void
  
  // Settings
  getSettings(): GameSettings
  updateSettings(settings: Partial<GameSettings>): void
  
  // Metadata
  isComplete(): boolean
  getScore(): number
  getResult(): 'win' | 'loss' | 'ongoing'
}

export interface GameState {
  timestamp: number
  score: number
  [key: string]: unknown
}

export interface GameSettings {
  soundEnabled: boolean
  difficulty?: 'easy' | 'medium' | 'hard'
  [key: string]: unknown
}

export interface GameMetadata {
  id: string
  name: string
  category: 'puzzle' | 'arcade' | 'strategy'
  description: string
  thumbnailUrl: string
}
