import { useState, useEffect } from 'react'

interface Card {
  id: number
  symbol: string
  flipped: boolean
  matched: boolean
}

const SYMBOLS = ['🎮', '🎯', '🎲', '🎪', '🎨', '🎭', '🎬', '🎸']

export function MemoryMatch() {
  const [cards, setCards] = useState<Card[]>([])
  const [flippedIndices, setFlippedIndices] = useState<number[]>([])
  const [moves, setMoves] = useState(0)
  const [won, setWon] = useState(false)
  const [canFlip, setCanFlip] = useState(true)

  useEffect(() => {
    initGame()
  }, [])

  const initGame = () => {
    const symbols = [...SYMBOLS, ...SYMBOLS]
    const shuffled = symbols.sort(() => Math.random() - 0.5)
    setCards(shuffled.map((symbol, i) => ({
      id: i,
      symbol,
      flipped: false,
      matched: false,
    })))
    setFlippedIndices([])
    setMoves(0)
    setWon(false)
    setCanFlip(true)
  }

  const handleCardClick = (index: number) => {
    if (!canFlip || cards[index].flipped || cards[index].matched || flippedIndices.includes(index)) {
      return
    }

    const newFlipped = [...flippedIndices, index]
    setFlippedIndices(newFlipped)

    setCards(prev => prev.map((card, i) =>
      i === index ? { ...card, flipped: true } : card
    ))

    if (newFlipped.length === 2) {
      setMoves(m => m + 1)
      setCanFlip(false)

      const [first, second] = newFlipped
      if (cards[first].symbol === cards[second].symbol) {
        // Match!
        setTimeout(() => {
          setCards(prev => prev.map((card, i) =>
            i === first || i === second ? { ...card, matched: true } : card
          ))
          setFlippedIndices([])
          setCanFlip(true)

          // Check win
          const allMatched = cards.every((c, i) =>
            i === first || i === second || c.matched
          )
          if (allMatched) {
            setWon(true)
          }
        }, 600)
      } else {
        // No match
        setTimeout(() => {
          setCards(prev => prev.map((card, i) =>
            i === first || i === second ? { ...card, flipped: false } : card
          ))
          setFlippedIndices([])
          setCanFlip(true)
        }, 1000)
      }
    }
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
      <div style={{ display: 'flex', gap: '2rem', fontSize: '1.25rem', fontWeight: 600 }}>
        <div>Moves: {moves}</div>
      </div>

      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(4, 1fr)',
        gap: '1rem',
        maxWidth: '400px',
      }}>
        {cards.map((card, index) => (
          <button
            key={card.id}
            onClick={() => handleCardClick(index)}
            disabled={!canFlip || card.matched}
            style={{
              width: '80px',
              height: '80px',
              borderRadius: '0.5rem',
              border: '2px solid var(--border)',
              backgroundColor: card.flipped || card.matched ? 'var(--accent)' : 'var(--bg-surface)',
              color: card.flipped || card.matched ? 'var(--bg-primary)' : 'transparent',
              fontSize: '2rem',
              cursor: canFlip && !card.matched ? 'pointer' : 'not-allowed',
              transition: 'all 0.3s',
              opacity: card.matched ? 0.5 : 1,
            }}
          >
            {(card.flipped || card.matched) ? card.symbol : '?'}
          </button>
        ))}
      </div>

      <button
        onClick={initGame}
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

      {won && (
        <div style={{
          fontSize: '1.5rem',
          fontWeight: 600,
          color: 'var(--accent)',
        }}>
          You Won in {moves} moves!
        </div>
      )}

      <div style={{
        fontSize: '0.875rem',
        color: 'var(--text-muted)',
        textAlign: 'center',
      }}>
        Find matching pairs · Match all cards to win
      </div>
    </div>
  )
}
