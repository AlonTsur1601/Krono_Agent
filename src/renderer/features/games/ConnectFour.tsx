import { useState } from 'react'

type Cell = null | 'red' | 'yellow'

export function ConnectFour() {
  const [board, setBoard] = useState<Cell[][]>(Array(6).fill(null).map(() => Array(7).fill(null)))
  const [currentPlayer, setCurrentPlayer] = useState<'red' | 'yellow'>('red')
  const [winner, setWinner] = useState<'red' | 'yellow' | 'draw' | null>(null)
  const [winningCells, setWinningCells] = useState<[number, number][]>([])

  const dropPiece = (col: number) => {
    if (winner) return

    // Find lowest empty row in column
    let row = -1
    for (let r = 5; r >= 0; r--) {
      if (board[r][col] === null) {
        row = r
        break
      }
    }

    if (row === -1) return // Column full

    // Place piece
    const newBoard = board.map(r => [...r])
    newBoard[row][col] = currentPlayer

    // Check for win
    const winCells = checkWin(newBoard, row, col, currentPlayer)
    if (winCells) {
      setWinningCells(winCells)
      setWinner(currentPlayer)
    } else if (newBoard.every(r => r.every(c => c !== null))) {
      setWinner('draw')
    }

    setBoard(newBoard)
    setCurrentPlayer(currentPlayer === 'red' ? 'yellow' : 'red')

    // AI move
    if (!winCells && currentPlayer === 'red') {
      setTimeout(() => aiMove(newBoard), 300)
    }
  }

  const aiMove = (currentBoard: Cell[][]) => {
    // Check if AI can win
    for (let c = 0; c < 7; c++) {
      const row = findLowestRow(currentBoard, c)
      if (row !== -1) {
        const testBoard = currentBoard.map(r => [...r])
        testBoard[row][c] = 'yellow'
        if (checkWin(testBoard, row, c, 'yellow')) {
          dropPiece(c)
          return
        }
      }
    }

    // Block player win
    for (let c = 0; c < 7; c++) {
      const row = findLowestRow(currentBoard, c)
      if (row !== -1) {
        const testBoard = currentBoard.map(r => [...r])
        testBoard[row][c] = 'red'
        if (checkWin(testBoard, row, c, 'red')) {
          dropPiece(c)
          return
        }
      }
    }

    // Play center if available
    if (findLowestRow(currentBoard, 3) !== -1) {
      dropPiece(3)
      return
    }

    // Random move
    const available = []
    for (let c = 0; c < 7; c++) {
      if (findLowestRow(currentBoard, c) !== -1) {
        available.push(c)
      }
    }
    if (available.length > 0) {
      dropPiece(available[Math.floor(Math.random() * available.length)])
    }
  }

  const findLowestRow = (b: Cell[][], col: number): number => {
    for (let r = 5; r >= 0; r--) {
      if (b[r][col] === null) return r
    }
    return -1
  }

  const checkWin = (b: Cell[][], row: number, col: number, player: Cell): [number, number][] | null => {
    const directions = [
      [[0, 1], [0, -1]], // Horizontal
      [[1, 0], [-1, 0]], // Vertical
      [[1, 1], [-1, -1]], // Diagonal \
      [[1, -1], [-1, 1]], // Diagonal /
    ]

    for (const [dir1, dir2] of directions) {
      const cells: [number, number][] = [[row, col]]

      // Check in first direction
      let r = row + dir1[0]
      let c = col + dir1[1]
      while (r >= 0 && r < 6 && c >= 0 && c < 7 && b[r][c] === player) {
        cells.push([r, c])
        r += dir1[0]
        c += dir1[1]
      }

      // Check in second direction
      r = row + dir2[0]
      c = col + dir2[1]
      while (r >= 0 && r < 6 && c >= 0 && c < 7 && b[r][c] === player) {
        cells.push([r, c])
        r += dir2[0]
        c += dir2[1]
      }

      if (cells.length >= 4) return cells
    }

    return null
  }

  const restart = () => {
    setBoard(Array(6).fill(null).map(() => Array(7).fill(null)))
    setCurrentPlayer('red')
    setWinner(null)
    setWinningCells([])
  }

  const isWinningCell = (row: number, col: number) => {
    return winningCells.some(([r, c]) => r === row && c === col)
  }

  return (
    <div style={{
      height: '100%',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      gap: '1.5rem',
      padding: '2rem',
    }}>
      <div style={{ fontSize: '1.25rem', fontWeight: 600 }}>
        {winner === 'draw' ? 'Draw!' :
         winner ? `${winner === 'red' ? 'You' : 'AI'} Won!` :
         `${currentPlayer === 'red' ? 'Your' : "AI's"} Turn`}
      </div>

      <div style={{
        display: 'inline-grid',
        gridTemplateColumns: 'repeat(7, 60px)',
        gridTemplateRows: 'repeat(6, 60px)',
        gap: '8px',
        padding: '1rem',
        borderRadius: '0.75rem',
        backgroundColor: 'var(--bg-surface)',
        border: '2px solid var(--border)',
      }}>
        {board.map((row, rowIndex) =>
          row.map((cell, colIndex) => (
            <button
              key={`${rowIndex}-${colIndex}`}
              onClick={() => dropPiece(colIndex)}
              disabled={!!winner || currentPlayer === 'yellow'}
              style={{
                width: '60px',
                height: '60px',
                borderRadius: '50%',
                border: '2px solid var(--border)',
                backgroundColor: cell === null ? 'var(--bg-primary)' :
                                cell === 'red' ? '#EF4444' : '#FBBF24',
                cursor: !winner && currentPlayer === 'red' ? 'pointer' : 'not-allowed',
                transition: 'all 0.2s',
                opacity: isWinningCell(rowIndex, colIndex) ? 1 : cell ? 0.9 : 0.3,
                boxShadow: isWinningCell(rowIndex, colIndex) ? '0 0 20px currentColor' : 'none',
              }}
            />
          ))
        )}
      </div>

      <button
        onClick={restart}
        style={{
          padding: '0.75rem 1.5rem',
          borderRadius: '0.5rem',
          border: '1px solid var(--border)',
          backgroundColor: 'var(--accent)',
          color: 'var(--bg-primary)',
          fontWeight: 600,
          cursor: 'pointer',
        }}
      >
        New Game
      </button>

      <div style={{
        fontSize: '0.875rem',
        color: 'var(--text-muted)',
        textAlign: 'center',
      }}>
        Click column to drop piece · Connect 4 to win
      </div>
    </div>
  )
}
