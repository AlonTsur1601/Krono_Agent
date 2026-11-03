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

export function Pong() {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [playerScore, setPlayerScore] = useState(0)
  const [aiScore, setAIScore] = useState(0)
  const [gameOver, setGameOver] = useState(false)
  const [paused, setPaused] = useState(false)

  const gameStateRef = useRef({
    ball: { x: 400, y: 300, dx: 4, dy: 4, radius: 8 } as Ball,
    playerPaddle: { x: 20, y: 250, width: 12, height: 100 } as Paddle,
    aiPaddle: { x: 768, y: 250, width: 12, height: 100 } as Paddle,
    mouseY: 300,
  })

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const handleMouseMove = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect()
      gameStateRef.current.mouseY = e.clientY - rect.top
    }

    canvas.addEventListener('mousemove', handleMouseMove)

    let animationId: number

    const gameLoop = () => {
      if (!paused && !gameOver) {
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
  }, [paused, gameOver])

  const update = () => {
    const { ball, playerPaddle, aiPaddle, mouseY } = gameStateRef.current

    // Move player paddle with mouse
    playerPaddle.y = Math.max(0, Math.min(600 - playerPaddle.height, mouseY - playerPaddle.height / 2))

    // AI paddle follows ball
    const aiSpeed = 3
    const aiTarget = ball.y - aiPaddle.height / 2
    if (aiPaddle.y < aiTarget - aiSpeed) {
      aiPaddle.y += aiSpeed
    } else if (aiPaddle.y > aiTarget + aiSpeed) {
      aiPaddle.y -= aiSpeed
    }

    // Move ball
    ball.x += ball.dx
    ball.y += ball.dy

    // Top/bottom wall collision
    if (ball.y - ball.radius < 0 || ball.y + ball.radius > 600) {
      ball.dy = -ball.dy
    }

    // Player paddle collision
    if (
      ball.x - ball.radius < playerPaddle.x + playerPaddle.width &&
      ball.x - ball.radius > playerPaddle.x &&
      ball.y > playerPaddle.y &&
      ball.y < playerPaddle.y + playerPaddle.height
    ) {
      ball.dx = Math.abs(ball.dx)
      // Add spin based on where ball hits paddle
      const hitPos = (ball.y - playerPaddle.y) / playerPaddle.height
      ball.dy = (hitPos - 0.5) * 8
    }

    // AI paddle collision
    if (
      ball.x + ball.radius > aiPaddle.x &&
      ball.x + ball.radius < aiPaddle.x + aiPaddle.width &&
      ball.y > aiPaddle.y &&
      ball.y < aiPaddle.y + aiPaddle.height
    ) {
      ball.dx = -Math.abs(ball.dx)
      const hitPos = (ball.y - aiPaddle.y) / aiPaddle.height
      ball.dy = (hitPos - 0.5) * 8
    }

    // Score
    if (ball.x - ball.radius < 0) {
      setAIScore((s) => {
        const newScore = s + 1
        if (newScore >= 10) setGameOver(true)
        return newScore
      })
      resetBall()
    } else if (ball.x + ball.radius > 800) {
      setPlayerScore((s) => {
        const newScore = s + 1
        if (newScore >= 10) setGameOver(true)
        return newScore
      })
      resetBall()
    }
  }

  const resetBall = () => {
    const { ball } = gameStateRef.current
    ball.x = 400
    ball.y = 300
    ball.dx = (Math.random() > 0.5 ? 1 : -1) * 4
    ball.dy = (Math.random() - 0.5) * 8
  }

  const draw = (ctx: CanvasRenderingContext2D) => {
    const { ball, playerPaddle, aiPaddle } = gameStateRef.current

    // Clear
    ctx.fillStyle = 'var(--bg-primary)'
    ctx.fillRect(0, 0, 800, 600)

    // Center line
    ctx.strokeStyle = 'var(--border)'
    ctx.setLineDash([10, 10])
    ctx.beginPath()
    ctx.moveTo(400, 0)
    ctx.lineTo(400, 600)
    ctx.stroke()
    ctx.setLineDash([])

    // Ball
    ctx.fillStyle = 'var(--accent)'
    ctx.beginPath()
    ctx.arc(ball.x, ball.y, ball.radius, 0, Math.PI * 2)
    ctx.fill()

    // Paddles
    ctx.fillStyle = 'var(--text-primary)'
    ctx.fillRect(playerPaddle.x, playerPaddle.y, playerPaddle.width, playerPaddle.height)
    ctx.fillRect(aiPaddle.x, aiPaddle.y, aiPaddle.width, aiPaddle.height)
  }

  const restart = () => {
    setPlayerScore(0)
    setAIScore(0)
    setGameOver(false)
    setPaused(false)
    resetBall()
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
      <div style={{ display: 'flex', gap: '3rem', fontSize: '1.5rem', fontWeight: 600 }}>
        <div>You: {playerScore}</div>
        <div>AI: {aiScore}</div>
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
          disabled={gameOver}
          style={{
            padding: '0.75rem 1.5rem',
            borderRadius: '0.5rem',
            border: '1px solid var(--border)',
            backgroundColor: 'var(--bg-surface)',
            color: 'var(--text-primary)',
            fontWeight: 600,
            cursor: gameOver ? 'not-allowed' : 'pointer',
            opacity: gameOver ? 0.5 : 1,
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
          color: playerScore > aiScore ? 'var(--accent)' : 'var(--text-primary)',
        }}>
          {playerScore > aiScore ? 'You Won!' : 'AI Won!'}
        </div>
      )}

      <div style={{
        fontSize: '0.875rem',
        color: 'var(--text-muted)',
        textAlign: 'center',
      }}>
        Move mouse up/down to control left paddle · First to 10 wins
      </div>
    </div>
  )
}
