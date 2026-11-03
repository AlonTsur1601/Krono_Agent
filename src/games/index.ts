// Game Registry

import type { GameMetadata } from './types'

export const GAME_CATALOG: GameMetadata[] = [
  {
    id: 'snake',
    name: 'Snake',
    category: 'arcade',
    description: 'Classic snake game with growing difficulty',
    thumbnailUrl: '/assets/games/snake-thumb.png',
  },
  {
    id: 'minesweeper',
    name: 'Minesweeper',
    category: 'puzzle',
    description: 'Find all mines without clicking one',
    thumbnailUrl: '/assets/games/minesweeper-thumb.png',
  },
  {
    id: 'tic-tac-toe',
    name: 'Tic-Tac-Toe',
    category: 'strategy',
    description: 'Classic 3x3 grid against AI',
    thumbnailUrl: '/assets/games/tic-tac-toe-thumb.png',
  },
  {
    id: '2048',
    name: '2048',
    category: 'puzzle',
    description: 'Slide tiles to reach 2048',
    thumbnailUrl: '/assets/games/2048-thumb.png',
  },
  {
    id: 'connect-four',
    name: 'Connect Four',
    category: 'strategy',
    description: 'Connect four pieces in a row',
    thumbnailUrl: '/assets/games/connect-four-thumb.png',
  },
  // TODO: Add remaining 19 games
]

export function getGameById(id: string): GameMetadata | undefined {
  return GAME_CATALOG.find(game => game.id === id)
}

export function getGamesByCategory(category: 'puzzle' | 'arcade' | 'strategy'): GameMetadata[] {
  return GAME_CATALOG.filter(game => game.category === category)
}
