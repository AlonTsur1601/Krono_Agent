import { useEffect, useRef, useState } from 'react'

interface Ball {
  x: number
  y: number
  dx: number
  dy: number
  radius: number
}

interface Paddle {
  x: number
  y: number
  width: number
  height: number
}

interface Brick {
  x: number
  y: number
  width: number
  height: number
  hits: number
}

export function Breakout() {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [score, setScore] = useState(0)
  const [lives, setLives] = useState(3)
  const [gameOver, setGameOver] = useState(false)
  const [won, setWon] = useState(false)
  const [paused, setPaused] = useState(false)

  const gameStateRef = useRef({
    ball: { x: 400, y: 300, dx: 3, dy: -3, radius: 8 } as Ball,
    paddle: { x: 350, y: 550, width: 100, height: 12 } as Paddle,
    bricks: [] as Brick[],
    mouseX: 400,
  })

  useEffect(() => {
    initBricks()
  }, [])

  const initBricks = () => {
    const bricks: Brick[] = []
    const rows = 5
    const cols = 10
    const brickWidth = 75
    const brickHeight = 20
    const padding = 5

    for (let row = 0; row < rows; row++) {
      for (let col = 0; col < cols; col++) {
        bricks.push({
          x: col * (brickWidth + padding) + 5,
          y: row * (brickHeight + padding) + 30,
          width: brickWidth,
          height: brickHeight,
          hits: row < 2 ? 2 : 1,
        })
      }
    }

    gameStateRef.current.bricks = bricks
  }

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const handleMouseMove = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect()
      gameStateRef.current.mouseX = e.clientX - rect.left
    }

    canvas.addEventListener('mousemove', handleMouseMove)

    let animationId: number

    const gameLoop = () => {
      if (!paused && !gameOver && !won) {
        update()
        draw(ctx)
      }
      animationId = requestAnimationFrame(gameLoop)
    }

    gameLoop()

    return () => {
      canvas.removeEventListener('mousemove', handleMouseMove)
      cancelAnimationFrame(animationId)
    }
  }, [paused, gameOver, won])

  const update = () => {
    const { ball, paddle, bricks, mouseX } = gameStateRef.current

    // Move paddle with mouse
    paddle.x = Math.max(0, Math.min(800 - paddle.width, mouseX - paddle.width / 2))

    // Move ball
    ball.x += ball.dx
    ball.y += ball.dy

    // Wall collision
    if (ball.x - ball.radius < 0 || ball.x + ball.radius > 800) {
      ball.dx = -ball.dx
    }
    if (ball.y - ball.radius < 0) {
      ball.dy = -ball.dy
    }

    // Paddle collision
    if (
      ball.y + ball.radius > paddle.y &&
      ball.y - ball.radius < paddle.y + paddle.height &&
      ball.x > paddle.x &&
      ball.x < paddle.x + paddle.width
    ) {
      ball.dy = -Math.abs(ball.dy)
      // Add spin based on where ball hits paddle
      const hitPos = (ball.x - paddle.x) / paddle.width
      ball.dx = (hitPos - 0.5) * 6
    }

    // Brick collision
    for (let i = bricks.length - 1; i >= 0; i--) {
      const brick = bricks[i]
      if (
        ball.x + ball.radius > brick.x &&
        ball.x - ball.radius < brick.x + brick.width &&
        ball.y + ball.radius > brick.y &&
        ball.y - ball.radius < brick.y + brick.height
      ) {
        ball.dy = -ball.dy
        brick.hits--
        if (brick.hits <= 0) {
          bricks.splice(i, 1)
          setScore((s) => s + 10)
        }
        break
      }
    }

    // Check win
    if (bricks.length === 0) {
      setWon(true)
    }

    // Check lose
    if (ball.y - ball.radius > 600) {
      setLives((l) => {
        const newLives = l - 1
        if (newLives <= 0) {
          setGameOver(true)
        } else {
          // Reset ball
          ball.x = 400
          ball.y = 300
          ball.dx = 3
          ball.dy = -3
        }
        return newLives
      })
    }
  }

  const draw = (ctx: CanvasRenderingContext2D) => {
    const { ball, paddle, bricks } = gameStateRef.current

    // Clear
    ctx.fillStyle = 'var(--bg-primary)'
    ctx.fillRect(0, 0, 800, 600)

    // Ball
    ctx.fillStyle = 'var(--accent)'
    ctx.beginPath()
    ctx.arc(ball.x, ball.y, ball.radius, 0, Math.PI * 2)
    ctx.fill()

    // Paddle
    ctx.fillStyle = 'var(--text-primary)'
    ctx.fillRect(paddle.x, paddle.y, paddle.width, paddle.height)

    // Bricks
    bricks.forEach((brick) => {
      ctx.fillStyle = brick.hits > 1 ? 'var(--accent)' : 'var(--text-muted)'
      ctx.fillRect(brick.x, brick.y, brick.width, brick.height)
    })
  }

  const restart = () => {
    setScore(0)
    setLives(3)
    setGameOver(false)
    setWon(false)
    setPaused(false)
    gameStateRef.current.ball = { x: 400, y: 300, dx: 3, dy: -3, radius: 8 }
    gameStateRef.current.paddle = { x: 350, y: 550, width: 100, height: 12 }
    initBricks()
  }

  return (
    <div style={{
      height: '100%',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      gap: '1rem',
      padding: '2rem',
    }}>
      <div style={{ display: 'flex', gap: '2rem', fontSize: '1.25rem', fontWeight: 600 }}>
        <div>Score: {score}</div>
        <div>Lives: {lives}</div>
      </div>

      <canvas
        ref={canvasRef}
        width={800}
        height={600}
        style={{
          border: '2px solid var(--border)',
          borderRadius: '0.5rem',
          backgroundColor: 'var(--bg-surface)',
        }}
      />

      <div style={{ display: 'flex', gap: '1rem' }}>
        <button
          onClick={() => setPaused(!paused)}
          disabled={gameOver || won}
          style={{
            padding: '0.75rem 1.5rem',
            borderRadius: '0.5rem',
            border: '1px solid var(--border)',
            backgroundColor: 'var(--bg-surface)',
            color: 'var(--text-primary)',
            fontWeight: 600,
            cursor: gameOver || won ? 'not-allowed' : 'pointer',
            opacity: gameOver || won ? 0.5 : 1,
          }}
        >
          {paused ? 'Resume' : 'Pause'}
        </button>
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
      </div>

      {gameOver && (
        <div style={{
          fontSize: '2rem',
          fontWeight: 600,
          color: 'var(--text-primary)',
        }}>
          Game Over!
        </div>
      )}

      {won && (
        <div style={{
          fontSize: '2rem',
          fontWeight: 600,
          color: 'var(--accent)',
        }}>
          You Won!
        </div>
      )}

      <div style={{
        fontSize: '0.875rem',
        color: 'var(--text-muted)',
        textAlign: 'center',
      }}>
        Move mouse to control paddle
      </div>
    </div>
  )
}
