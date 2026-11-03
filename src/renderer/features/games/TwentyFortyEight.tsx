import { useEffect, useState } from 'react'

type Grid = number[][]

const SIZE = 4

export function TwentyFortyEight() {
  const [grid, setGrid] = useState<Grid>(() => initializeGrid())
  const [score, setScore] = useState(0)
  const [gameOver, setGameOver] = useState(false)
  const [won, setWon] = useState(false)

  function initializeGrid(): Grid {
    const newGrid: Grid = Array(SIZE).fill(null).map(() => Array(SIZE).fill(0))
    addRandomTile(newGrid)
    addRandomTile(newGrid)
    return newGrid
  }

  function addRandomTile(currentGrid: Grid): void {
    const emptyCells: [number, number][] = []
    for (let r = 0; r < SIZE; r++) {
      for (let c = 0; c < SIZE; c++) {
        if (currentGrid[r][c] === 0) {
          emptyCells.push([r, c])
        }
      }
    }
    if (emptyCells.length > 0) {
      const [r, c] = emptyCells[Math.floor(Math.random() * emptyCells.length)]
      currentGrid[r][c] = Math.random() < 0.9 ? 2 : 4
    }
  }

  function move(direction: 'up' | 'down' | 'left' | 'right'): void {
    if (gameOver) return

    const newGrid = grid.map(row => [...row])
    let moved = false
    let points = 0

    const moveAndMerge = (line: number[]): [number[], number] => {
      // Remove zeros
      const filtered = line.filter(x => x !== 0)
      const merged: number[] = []
      let linePoints = 0
      let skip = false

      for (let i = 0; i < filtered.length; i++) {
        if (skip) {
          skip = false
          continue
        }
        if (i < filtered.length - 1 && filtered[i] === filtered[i + 1]) {
          merged.push(filtered[i] * 2)
          linePoints += filtered[i] * 2
          skip = true
          if (filtered[i] * 2 === 2048 && !won) {
            setWon(true)
          }
        } else {
          merged.push(filtered[i])
        }
      }

      // Pad with zeros
      while (merged.length < SIZE) {
        merged.push(0)
      }

      return [merged, linePoints]
    }

    if (direction === 'left') {
      for (let r = 0; r < SIZE; r++) {
        const [newRow, linePoints] = moveAndMerge(newGrid[r])
        if (JSON.stringify(newRow) !== JSON.stringify(newGrid[r])) moved = true
        newGrid[r] = newRow
        points += linePoints
      }
    } else if (direction === 'right') {
      for (let r = 0; r < SIZE; r++) {
        const [newRow, linePoints] = moveAndMerge(newGrid[r].reverse())
        newRow.reverse()
        if (JSON.stringify(newRow) !== JSON.stringify(newGrid[r])) moved = true
        newGrid[r] = newRow
        points += linePoints
      }
    } else if (direction === 'up') {
      for (let c = 0; c < SIZE; c++) {
        const column = newGrid.map(row => row[c])
        const [newColumn, linePoints] = moveAndMerge(column)
        if (JSON.stringify(newColumn) !== JSON.stringify(column)) moved = true
        newColumn.forEach((val, r) => {
          newGrid[r][c] = val
        })
        points += linePoints
      }
    } else if (direction === 'down') {
      for (let c = 0; c < SIZE; c++) {
        const column = newGrid.map(row => row[c])
        const [newColumn, linePoints] = moveAndMerge(column.reverse())
        newColumn.reverse()
        if (JSON.stringify(newColumn) !== JSON.stringify(column.reverse())) moved = true
        newColumn.forEach((val, r) => {
          newGrid[r][c] = val
        })
        points += linePoints
      }
    }

    if (moved) {
      addRandomTile(newGrid)
      setGrid(newGrid)
      setScore(score + points)

      // Check if game over
      if (!hasValidMoves(newGrid)) {
        setGameOver(true)
      }
    }
  }

  function hasValidMoves(currentGrid: Grid): boolean {
    // Check for empty cells
    for (let r = 0; r < SIZE; r++) {
      for (let c = 0; c < SIZE; c++) {
        if (currentGrid[r][c] === 0) return true
      }
    }

    // Check for possible merges
    for (let r = 0; r < SIZE; r++) {
      for (let c = 0; c < SIZE; c++) {
        const current = currentGrid[r][c]
        if (r < SIZE - 1 && currentGrid[r + 1][c] === current) return true
        if (c < SIZE - 1 && currentGrid[r][c + 1] === current) return true
      }
    }

    return false
  }

  const restart = () => {
    setGrid(initializeGrid())
    setScore(0)
    setGameOver(false)
    setWon(false)
  }

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.key)) {
        e.preventDefault()
        const directionMap: Record<string, 'up' | 'down' | 'left' | 'right'> = {
          ArrowUp: 'up',
          ArrowDown: 'down',
          ArrowLeft: 'left',
          ArrowRight: 'right',
        }
        move(directionMap[e.key])
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [grid, gameOver])

  const getTileColor = (value: number): string => {
    const colors: Record<number, string> = {
      2: '#eee4da',
      4: '#ede0c8',
      8: '#f2b179',
      16: '#f59563',
      32: '#f67c5f',
      64: '#f65e3b',
      128: '#edcf72',
      256: '#edcc61',
      512: '#edc850',
      1024: '#edc53f',
      2048: '#edc22e',
    }
    return colors[value] || '#3c3a32'
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
        <h2 style={{ fontSize: '1.5rem', marginBottom: '0.5rem' }}>2048</h2>
        <div style={{ fontSize: '1rem', fontWeight: 600 }}>Score: {score}</div>
      </div>

      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(4, 80px)',
        gap: '10px',
        padding: '10px',
        backgroundColor: '#bbada0',
        borderRadius: '0.75rem',
      }}>
        {grid.flatMap((row, r) =>
          row.map((cell, c) => (
            <div
              key={`${r}-${c}`}
              style={{
                width: '80px',
                height: '80px',
                backgroundColor: cell === 0 ? '#cdc1b4' : getTileColor(cell),
                borderRadius: '0.5rem',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: cell >= 1000 ? '1.5rem' : '2rem',
                fontWeight: 'bold',
                color: cell <= 4 ? '#776e65' : '#f9f6f2',
              }}
            >
              {cell !== 0 && cell}
            </div>
          ))
        )}
      </div>

      <div style={{ textAlign: 'center' }}>
        {won && !gameOver && (
          <p style={{ color: 'var(--accent)', marginBottom: '1rem', fontSize: '1.25rem', fontWeight: 600 }}>
            You reached 2048! Keep going!
          </p>
        )}
        {gameOver && (
          <p style={{ color: '#DC2626', marginBottom: '1rem', fontSize: '1.25rem', fontWeight: 600 }}>
            Game Over!
          </p>
        )}
        <button
          onClick={restart}
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
          Use Arrow Keys to play
        </p>
      </div>
    </div>
  )
}