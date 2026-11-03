// Application constants

export const APP_NAME = 'Krono Agent'
export const APP_VERSION = '0.1.0'

// Execution modes
export const MODES = {
  CHAT: 'chat',
  PLAN: 'plan',
  AGENT: 'agent',
  GOAL: 'goal',
} as const

// Permission profiles
export const PERMISSIONS = {
  ASK: 'ask',
  PROJECT: 'project',
  FULL: 'full',
} as const

// Themes
export const THEMES = {
  DARK: 'dark',
  LIGHT: 'light',
  HALLOWEEN: 'halloween',
  SYSTEM: 'system',
} as const

// Game IDs
export const GAMES = {
  MINESWEEPER: 'minesweeper',
  SNAKE: 'snake',
  TIC_TAC_TOE: 'tic-tac-toe',
  MAZE_CHASE: 'maze-chase',
  PONG: 'pong',
  BLOCK_FIT: 'block-fit',
  FALLING_BLOCKS: 'falling-blocks',
  SAND_BLOCKS: 'sand-blocks',
  FLAP: 'flap',
  DARTS: 'darts',
  JIGSAW: 'jigsaw',
  STACK: 'stack',
  ORBIT: 'orbit',
  COLOR_GAUNTLET: 'color-gauntlet',
  ODD_COLOR: 'odd-color',
  TWO_THOUSAND_FORTY_EIGHT: '2048',
  BREAKOUT: 'breakout',
  MEMORY_MATCH: 'memory-match',
  SUDOKU: 'sudoku',
  SOLITAIRE: 'solitaire',
  CONNECT_FOUR: 'connect-four',
  REVERSI: 'reversi',
  SIMON: 'simon',
  LIGHTS_OUT: 'lights-out',
} as const

// Database settings
export const DB_WAL_MODE = 'WAL'
export const MAX_SYNC_RETRIES = 3

// UI settings
export const SIDEBAR_DEFAULT_WIDTH = 248
export const SIDEBAR_MIN_WIDTH = 200
export const SIDEBAR_MAX_WIDTH = 400

export const TERMINAL_DEFAULT_HEIGHT = 280
export const TERMINAL_MIN_HEIGHT = 120

// Performance targets
export const TARGET_FPS = 60
export const TERMINAL_LATENCY_MS = 50
export const MODEL_TTFB_LOCAL_MS = 500
export const MODEL_TTFB_CLOUD_MS = 2000
