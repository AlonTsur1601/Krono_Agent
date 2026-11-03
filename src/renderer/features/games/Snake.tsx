import { useEffect, useRef, useState } from 'react'

type Direction = 'UP' | 'DOWN' | 'LEFT' | 'RIGHT'
type Position = { x: number; y: number }

const GRID_SIZE = 20
const CELL_SIZE = 20
const INITIAL_SPEED = 150

export function Snake() {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [gameState, setGameState] = useState<'idle' | 'playing' | 'paused' | 'gameover'>('idle')
  const [score, setScore] = useState(0)
  const [highScore, setHighScore] = useState(0)
  
  // Game state refs (don't trigger re-renders)
  const snakeRef = useRef<Position[]>([{ x: 10, y: 10 }])
  const directionRef = useRef<Direction>('RIGHT')
  const foodRef = useRef<Position>({ x: 15, y: 15 })
  const speedRef = useRef(INITIAL_SPEED)

  const spawnFood = () => {
    const newFood = {
      x: Math.floor(Math.random() * GRID_SIZE),
      y: Math.floor(Math.random() * GRID_SIZE),
    }
    // Check if food spawns on snake
    const onSnake = snakeRef.current.some(segment => 
      segment.x === newFood.x && segment.y === newFood.y
    )
    if (onSnake) {
      spawnFood()
    } else {
      foodRef.current = newFood
    }
  }

  const startGame = () => {
    snakeRef.current = [{ x: 10, y: 10 }]
    directionRef.current = 'RIGHT'
    speedRef.current = INITIAL_SPEED
    setScore(0)
    spawnFood()
    setGameState('playing')
  }

  const pauseGame = () => {
    setGameState(gameState === 'paused' ? 'playing' : 'paused')
  }

  const draw = () => {
    const canvas = canvasRef.current
    if (!canvas) return

    const ctx = canvas.getContext('2d')
    if (!ctx) return

    // Clear canvas
    ctx.fillStyle = 'var(--bg-surface)'
    ctx.fillRect(0, 0, canvas.width, canvas.height)

    // Draw grid
    ctx.strokeStyle = 'var(--border)'
    for (let i = 0; i <= GRID_SIZE; i++) {
      ctx.beginPath()
      ctx.moveTo(i * CELL_SIZE, 0)
      ctx.lineTo(i * CELL_SIZE, GRID_SIZE * CELL_SIZE)
      ctx.stroke()
      ctx.beginPath()
      ctx.moveTo(0, i * CELL_SIZE)
      ctx.lineTo(GRID_SIZE * CELL_SIZE, i * CELL_SIZE)
      ctx.stroke()
    }

    // Draw snake
    ctx.fillStyle = 'var(--accent)'
    snakeRef.current.forEach((segment, index) => {
      ctx.fillRect(
        segment.x * CELL_SIZE + 1,
        segment.y * CELL_SIZE + 1,
        CELL_SIZE - 2,
        CELL_SIZE - 2
      )
      // Draw head slightly different
      if (index === 0) {
        ctx.fillStyle = 'var(--accent-hover)'
        ctx.fillRect(
          segment.x * CELL_SIZE + 3,
          segment.y * CELL_SIZE + 3,
          CELL_SIZE - 6,
          CELL_SIZE - 6
        )
        ctx.fillStyle = 'var(--accent)'
      }
    })

    // Draw food
    ctx.fillStyle = '#FF9B50'
    ctx.fillRect(
      foodRef.current.x * CELL_SIZE + 2,
      foodRef.current.y * CELL_SIZE + 2,
      CELL_SIZE - 4,
      CELL_SIZE - 4
    )
  }

  const gameLoop = () => {
    if (gameState !== 'playing') return

    const snake = snakeRef.current
    const direction = directionRef.current
    const head = snake[0]

    // Calculate new head position
    let newHead: Position
    switch (direction) {
      case 'UP':
        newHead = { x: head.x, y: head.y - 1 }
        break
      case 'DOWN':
        newHead = { x: head.x, y: head.y + 1 }
        break
      case 'LEFT':
        newHead = { x: head.x - 1, y: head.y }
        break
      case 'RIGHT':
        newHead = { x: head.x + 1, y: head.y }
        break
    }

    // Check wall collision
    if (
      newHead.x < 0 ||
      newHead.x >= GRID_SIZE ||
      newHead.y < 0 ||
      newHead.y >= GRID_SIZE
    ) {
      setGameState('gameover')
      if (score > highScore) {
        setHighScore(score)
      }
      return
    }

    // Check self collision
    if (snake.some(segment => segment.x === newHead.x && segment.y === newHead.y)) {
      setGameState('gameover')
      if (score > highScore) {
        setHighScore(score)
      }
      return
    }

    // Move snake
    const newSnake = [newHead, ...snake]

    // Check food collision
    if (newHead.x === foodRef.current.x && newHead.y === foodRef.current.y) {
      // Grow snake (don't remove tail)
      setScore(s => {
        const newScore = s + 10
        // Increase speed every 5 food items
        if (newScore % 50 === 0) {
          speedRef.current = Math.max(50, speedRef.current - 10)
        }
        return newScore
      })
      spawnFood()
    } else {
      // Normal move (remove tail)
      newSnake.pop()
    }

    snakeRef.current = newSnake
    draw()
  }

  // Game loop interval
  useEffect(() => {
    if (gameState !== 'playing') return

    const interval = setInterval(gameLoop, speedRef.current)
    return () => clearInterval(interval)
  }, [gameState])

  // Keyboard controls
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (gameState !== 'playing') return

      const key = e.key.toLowerCase()
      const currentDirection = directionRef.current

      if ((key === 'arrowup' || key === 'w') && currentDirection !== 'DOWN') {
        directionRef.current = 'UP'
        e.preventDefault()
      } else if ((key === 'arrowdown' || key === 's') && currentDirection !== 'UP') {
        directionRef.current = 'DOWN'
        e.preventDefault()
      } else if ((key === 'arrowleft' || key === 'a') && currentDirection !== 'RIGHT') {
        directionRef.current = 'LEFT'
        e.preventDefault()
      } else if ((key === 'arrowright' || key === 'd') && currentDirection !== 'LEFT') {
        directionRef.current = 'RIGHT'
        e.preventDefault()
      } else if (key === ' ') {
        pauseGame()
        e.preventDefault()
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [gameState])

  // Initial draw
  useEffect(() => {
    draw()
  }, [])

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
        <h2 style={{ fontSize: '1.5rem', marginBottom: '0.5rem' }}>Snake</h2>
        <div style={{ display: 'flex', gap: '2rem', fontSize: '0.875rem', opacity: 0.8 }}>
          <div>Score: {score}</div>
          <div>High Score: {highScore}</div>
        </div>
      </div>

      <canvas
        ref={canvasRef}
        width={GRID_SIZE * CELL_SIZE}
        height={GRID_SIZE * CELL_SIZE}
        style={{
          border: '2px solid var(--border)',
          borderRadius: '0.5rem',
        }}
      />

      <div style={{ textAlign: 'center' }}>
        {gameState === 'idle' && (
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
            Start Game
          </button>
        )}

        {gameState === 'playing' && (
          <button
            onClick={pauseGame}
            style={{
              padding: '0.75rem 2rem',
              borderRadius: '0.375rem',
              border: '1px solid var(--border)',
              backgroundColor: 'var(--bg-surface)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
            }}
          >
            Pause (Space)
          </button>
        )}

        {gameState === 'paused' && (
          <button
            onClick={pauseGame}
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
            Resume
          </button>
        )}

        {gameState === 'gameover' && (
          <div>
            <p style={{ marginBottom: '1rem', fontSize: '1.25rem', color: '#DC2626' }}>
              Game Over!
            </p>
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
              Play Again
            </button>
          </div>
        )}

        <p style={{ marginTop: '1rem', fontSize: '0.75rem', opacity: 0.6 }}>
          Use Arrow Keys or WASD • Space to Pause
        </p>
      </div>
    </div>
  )
}
