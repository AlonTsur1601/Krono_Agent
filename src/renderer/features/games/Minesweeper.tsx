import { useState, useEffect } from 'react'

type Cell = {
  isMine: boolean
  isRevealed: boolean
  isFlagged: boolean
  neighborMines: number
}

const ROWS = 10
const COLS = 10
const MINES = 10

export function Minesweeper() {
  const [grid, setGrid] = useState<Cell[][]>([])
  const [gameState, setGameState] = useState<'idle' | 'playing' | 'won' | 'lost'>('idle')
  const [flagsLeft, setFlagsLeft] = useState(MINES)
  const [firstClick, setFirstClick] = useState(true)

  const initializeGrid = (safeRow?: number, safeCol?: number): Cell[][] => {
    const newGrid: Cell[][] = Array(ROWS)
      .fill(null)
      .map(() =>
        Array(COLS).fill(null).map(() => ({
          isMine: false,
          isRevealed: false,
          isFlagged: false,
          neighborMines: 0,
        }))
      )

    // Place mines randomly, avoiding first click position
    let minesPlaced = 0
    while (minesPlaced < MINES) {
      const row = Math.floor(Math.random() * ROWS)
      const col = Math.floor(Math.random() * COLS)

      if (!newGrid[row][col].isMine && !(row === safeRow && col === safeCol)) {
        newGrid[row][col].isMine = true
        minesPlaced++
      }
    }

    // Calculate neighbor counts
    for (let r = 0; r < ROWS; r++) {
      for (let c = 0; c < COLS; c++) {
        if (!newGrid[r][c].isMine) {
          let count = 0
          for (let dr = -1; dr <= 1; dr++) {
            for (let dc = -1; dc <= 1; dc++) {
              const nr = r + dr
              const nc = c + dc
              if (nr >= 0 && nr < ROWS && nc >= 0 && nc < COLS && newGrid[nr][nc].isMine) {
                count++
              }
            }
          }
          newGrid[r][c].neighborMines = count
        }
      }
    }

    return newGrid
  }

  const startGame = () => {
    setGrid(initializeGrid())
    setGameState('idle')
    setFlagsLeft(MINES)
    setFirstClick(true)
  }

  useEffect(() => {
    startGame()
  }, [])

  const revealCell = (row: number, col: number) => {
    if (gameState === 'won' || gameState === 'lost') return
    if (grid[row][col].isRevealed || grid[row][col].isFlagged) return

    // Handle first click - ensure it's safe
    if (firstClick) {
      const newGrid = initializeGrid(row, col)
      setGrid(newGrid)
      setFirstClick(false)
      setGameState('playing')
      revealCellInGrid(newGrid, row, col)
      return
    }

    revealCellInGrid(grid, row, col)
  }

  const revealCellInGrid = (currentGrid: Cell[][], row: number, col: number) => {
    const newGrid = currentGrid.map(r => r.map(c => ({ ...c })))
    const toReveal: [number, number][] = [[row, col]]
    const visited = new Set<string>()

    while (toReveal.length > 0) {
      const [r, c] = toReveal.pop()!
      const key = `${r},${c}`
      if (visited.has(key)) continue
      visited.add(key)

      if (r < 0 || r >= ROWS || c < 0 || c >= COLS) continue
      if (newGrid[r][c].isRevealed || newGrid[r][c].isFlagged) continue

      newGrid[r][c].isRevealed = true

      // If mine, game over
      if (newGrid[r][c].isMine) {
        // Reveal all mines
        for (let i = 0; i < ROWS; i++) {
          for (let j = 0; j < COLS; j++) {
            if (newGrid[i][j].isMine) {
              newGrid[i][j].isRevealed = true
            }
          }
        }
        setGrid(newGrid)
        setGameState('lost')
        return
      }

      // If no neighboring mines, reveal neighbors
      if (newGrid[r][c].neighborMines === 0) {
        for (let dr = -1; dr <= 1; dr++) {
          for (let dc = -1; dc <= 1; dc++) {
            if (dr === 0 && dc === 0) continue
            toReveal.push([r + dr, c + dc])
          }
        }
      }
    }

    setGrid(newGrid)

    // Check if won
    let unrevealedNonMines = 0
    for (let r = 0; r < ROWS; r++) {
      for (let c = 0; c < COLS; c++) {
        if (!newGrid[r][c].isMine && !newGrid[r][c].isRevealed) {
          unrevealedNonMines++
        }
      }
    }
    if (unrevealedNonMines === 0) {
      setGameState('won')
    }
  }

  const toggleFlag = (e: React.MouseEvent, row: number, col: number) => {
    e.preventDefault()
    if (gameState === 'won' || gameState === 'lost') return
    if (grid[row][col].isRevealed) return

    const newGrid = grid.map(r => r.map(c => ({ ...c })))
    newGrid[row][col].isFlagged = !newGrid[row][col].isFlagged
    setGrid(newGrid)
    setFlagsLeft(flagsLeft + (newGrid[row][col].isFlagged ? -1 : 1))
  }

  const getCellColor = (cell: Cell): string => {
    if (!cell.isRevealed) return 'var(--bg-surface)'
    if (cell.isMine) return '#DC2626'
    return 'var(--bg-primary)'
  }

  const getNumberColor = (num: number): string => {
    const colors = ['', '#0000FF', '#008000', '#FF0000', '#000080', '#800000', '#008080', '#000000', '#808080']
    return colors[num] || '#000000'
  }

  return (
    <div style={{
      height: '100%',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      flexDirection: 'column',
      gap: '2rem',
    }}>
      <div style={{ textAlign: 'center' }}>
        <h2 style={{ fontSize: '1.5rem', marginBottom: '0.5rem' }}>Minesweeper</h2>
        <div style={{ fontSize: '0.875rem', opacity: 0.8 }}>
          Flags: {flagsLeft} / {MINES}
        </div>
      </div>

      <div style={{
        display: 'grid',
        gridTemplateColumns: `repeat(${COLS}, 30px)`,
        gap: '2px',
        padding: '10px',
        backgroundColor: 'var(--border)',
        borderRadius: '0.5rem',
      }}>
        {grid.flatMap((row, r) =>
          row.map((cell, c) => (
            <button
              key={`${r}-${c}`}
              onClick={() => revealCell(r, c)}
              onContextMenu={(e) => toggleFlag(e, r, c)}
              style={{
                width: '30px',
                height: '30px',
                border: 'none',
                backgroundColor: getCellColor(cell),
                cursor: cell.isRevealed ? 'default' : 'pointer',
                fontSize: '0.875rem',
                fontWeight: 'bold',
                color: cell.isRevealed && !cell.isMine ? getNumberColor(cell.neighborMines) : 'var(--text-primary)',
              }}
            >
              {cell.isFlagged
                ? '🚩'
                : cell.isRevealed
                ? cell.isMine
                  ? '💣'
                  : cell.neighborMines > 0
                  ? cell.neighborMines
                  : ''
                : ''}
            </button>
          ))
        )}
      </div>

      <div style={{ textAlign: 'center' }}>
        {gameState === 'won' && (
          <p style={{ color: 'var(--accent)', marginBottom: '1rem', fontSize: '1.25rem', fontWeight: 600 }}>
            You won!
          </p>
        )}
        {gameState === 'lost' && (
          <p style={{ color: '#DC2626', marginBottom: '1rem', fontSize: '1.25rem', fontWeight: 600 }}>
            Game Over!
          </p>
        )}
        <button
          onClick={startGame}
          style={{
            padding: '0.75rem 2rem',
            borderRadius: '0.375rem',
            border: 'none',
            backgroundColor: 'var(--accent)',
            color: 'var(--bg-primary)',
            fontSize: '1rem',
            fontWeight: 600,
            cursor: 'pointer',
          }}
        >
          New Game
        </button>
        <p style={{ marginTop: '1rem', fontSize: '0.75rem', opacity: 0.6 }}>
          Left click to reveal • Right click to flag
        </p>
      </div>
    </div>
  )
}
