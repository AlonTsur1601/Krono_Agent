import { useState } from 'react'

type Cell = 'X' | 'O' | null
type Board = Cell[]

const WINNING_COMBINATIONS = [
  [0, 1, 2], [3, 4, 5], [6, 7, 8], // rows
  [0, 3, 6], [1, 4, 7], [2, 5, 8], // columns
  [0, 4, 8], [2, 4, 6], // diagonals
]

export function TicTacToe() {
  const [board, setBoard] = useState<Board>(Array(9).fill(null))
  const [isPlayerTurn, setIsPlayerTurn] = useState(true)
  const [gameOver, setGameOver] = useState(false)
  const [winner, setWinner] = useState<'X' | 'O' | 'draw' | null>(null)

  const checkWinner = (currentBoard: Board): 'X' | 'O' | 'draw' | null => {
    // Check winning combinations
    for (const combo of WINNING_COMBINATIONS) {
      const [a, b, c] = combo
      if (currentBoard[a] && currentBoard[a] === currentBoard[b] && currentBoard[a] === currentBoard[c]) {
        return currentBoard[a]
      }
    }

    // Check draw
    if (currentBoard.every(cell => cell !== null)) {
      return 'draw'
    }

    return null
  }

  const minimax = (currentBoard: Board, isMaximizing: boolean): number => {
    const result = checkWinner(currentBoard)
    if (result === 'O') return 10
    if (result === 'X') return -10
    if (result === 'draw') return 0

    if (isMaximizing) {
      let bestScore = -Infinity
      for (let i = 0; i < 9; i++) {
        if (currentBoard[i] === null) {
          currentBoard[i] = 'O'
          const score = minimax(currentBoard, false)
          currentBoard[i] = null
          bestScore = Math.max(score, bestScore)
        }
      }
      return bestScore
    } else {
      let bestScore = Infinity
      for (let i = 0; i < 9; i++) {
        if (currentBoard[i] === null) {
          currentBoard[i] = 'X'
          const score = minimax(currentBoard, true)
          currentBoard[i] = null
          bestScore = Math.min(score, bestScore)
        }
      }
      return bestScore
    }
  }

  const getBestMove = (currentBoard: Board): number => {
    let bestScore = -Infinity
    let bestMove = -1

    for (let i = 0; i < 9; i++) {
      if (currentBoard[i] === null) {
        currentBoard[i] = 'O'
        const score = minimax(currentBoard, false)
        currentBoard[i] = null
        if (score > bestScore) {
          bestScore = score
          bestMove = i
        }
      }
    }

    return bestMove
  }

  const handleClick = (index: number) => {
    if (gameOver || !isPlayerTurn || board[index]) return

    const newBoard = [...board]
    newBoard[index] = 'X'

    const result = checkWinner(newBoard)
    if (result) {
      setBoard(newBoard)
      setWinner(result)
      setGameOver(true)
      return
    }

    setBoard(newBoard)
    setIsPlayerTurn(false)

    // AI move after a short delay
    setTimeout(() => {
      const aiMove = getBestMove(newBoard)
      if (aiMove !== -1) {
        newBoard[aiMove] = 'O'
        const aiResult = checkWinner(newBoard)
        setBoard(newBoard)
        if (aiResult) {
          setWinner(aiResult)
          setGameOver(true)
        } else {
          setIsPlayerTurn(true)
        }
      }
    }, 300)
  }

  const restart = () => {
    setBoard(Array(9).fill(null))
    setIsPlayerTurn(true)
    setGameOver(false)
    setWinner(null)
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
      <h2 style={{ fontSize: '1.5rem' }}>Tic-Tac-Toe</h2>

      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(3, 100px)',
        gap: '8px',
      }}>
        {board.map((cell, i) => (
          <button
            key={i}
            onClick={() => handleClick(i)}
            disabled={gameOver || !isPlayerTurn || cell !== null}
            style={{
              width: '100px',
              height: '100px',
              fontSize: '2rem',
              fontWeight: 'bold',
              borderRadius: '0.75rem',
              border: '2px solid var(--border)',
              backgroundColor: cell ? 'var(--bg-surface)' : 'var(--bg-primary)',
              color: cell === 'X' ? 'var(--accent)' : '#FF9B50',
              cursor: (gameOver || !isPlayerTurn || cell) ? 'not-allowed' : 'pointer',
              opacity: (gameOver || !isPlayerTurn || cell) ? 0.7 : 1,
            }}
          >
            {cell}
          </button>
        ))}
      </div>

      <div style={{ textAlign: 'center' }}>
        {!gameOver && (
          <p style={{ fontSize: '1rem', opacity: 0.8 }}>
            {isPlayerTurn ? 'Your turn (X)' : 'AI thinking...'}
          </p>
        )}
        {gameOver && (
          <div>
            <p style={{ fontSize: '1.25rem', marginBottom: '1rem', fontWeight: 600 }}>
              {winner === 'draw' ? "It's a draw!" : winner === 'X' ? 'You win!' : 'AI wins!'}
            </p>
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
              Play Again
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
