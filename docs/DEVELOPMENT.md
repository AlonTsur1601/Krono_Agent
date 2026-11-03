# Krono Agent - Development Guide

## Prerequisites

- Node.js 20+
- Git
- Windows 11 (primary target), macOS, or Linux

## Setup

```bash
# Clone repository
git clone https://github.com/AlonTsur1601/Krono_Agent.git
cd Krono_Agent

# Install dependencies
npm install

# Run in development mode
npm run dev
```

The app will launch with hot-reload enabled. Changes to renderer code reload automatically.

## Project Structure

```
src/
├── main/           # Electron main process (Node.js)
│   ├── index.ts    # Entry point
│   ├── ipc/        # IPC handlers
│   ├── db/         # Database manager
│   └── permissions/# Permission system
├── renderer/       # React UI (browser)
│   ├── App.tsx     # Root component
│   ├── features/   # Feature modules
│   ├── stores/     # Zustand stores
│   └── layouts/    # Layout components
├── agent/          # Agent execution context
├── games/          # Game modules
├── shared/         # Shared types/schemas
└── preload/        # Preload script
```

## Architecture

**Process Separation:**
- Main: Trusted Node.js process (file I/O, database, IPC validation)
- Renderer: Sandboxed browser context (React UI, no direct system access)
- Preload: Bridge exposing validated IPC to renderer

**State Management:**
- Zustand for UI state (theme, auth, etc.)
- TanStack Query for server state (coming soon)
- SQLite for local persistence

**IPC Pattern:**
```typescript
// Renderer calls main via IPC
const result = await window.api.invoke('auth:login', { email, password })

// Main validates with Zod and executes
ipcMain.handle('auth:login', async (_event, data) => {
  const { email, password } = AuthLoginSchema.parse(data)
  // ... authentication logic
})
```

## Available Scripts

```bash
# Development
npm run dev          # Start dev server with hot reload

# Building
npm run build        # Build for production
npm run build:main   # Build main process only

# Linting & Testing
npm run lint         # Lint code
npm run lint:fix     # Fix linting issues
npm test             # Run unit tests
npm run test:watch   # Run tests in watch mode
npm run test:e2e     # Run integration tests

# Packaging
npm run package:win    # Build Windows installer
npm run package:mac    # Build macOS DMG
npm run package:linux  # Build Linux AppImage
```

## Adding a New Feature

### 1. Add Types

Create types in `src/shared/types/` for data structures used across processes.

### 2. Add Schema

Create Zod schema in `src/shared/schemas/` for IPC validation.

### 3. Add IPC Handler

Create handler in `src/main/ipc/`:

```typescript
import { ipcMain } from 'electron'
import { YourSchema } from '../../shared/schemas'

export function setupYourHandlers() {
  ipcMain.handle('your:action', async (_event, data) => {
    const validated = YourSchema.parse(data)
    // Execute action
    return result
  })
}
```

Register in `src/main/index.ts`:
```typescript
import { setupYourHandlers } from './ipc/your'

app.whenReady().then(() => {
  setupYourHandlers()
  // ...
})
```

### 4. Add Store (if needed)

Create Zustand store in `src/renderer/stores/`:

```typescript
import { create } from 'zustand'

interface YourState {
  data: YourData | null
  loadData: () => Promise<void>
}

export const useYourStore = create<YourState>((set) => ({
  data: null,
  loadData: async () => {
    const data = await window.api.invoke('your:action')
    set({ data })
  },
}))
```

### 5. Add UI Component

Create component in `src/renderer/features/your/`:

```typescript
import { useYourStore } from '@/stores/yourStore'

export function YourComponent() {
  const { data, loadData } = useYourStore()
  // ... component logic
}
```

## Adding a Game

1. Create game file in `src/renderer/features/games/YourGame.tsx`
2. Implement game logic with Canvas 2D
3. Add game metadata to `src/games/index.ts`
4. Create thumbnail at `assets/games/yourgame-thumb.png`

Game template:
```typescript
export function YourGame() {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [gameState, setGameState] = useState<'idle' | 'playing' | 'paused' | 'gameover'>('idle')
  
  // Game loop, input handling, rendering
  
  return (
    <div>
      <canvas ref={canvasRef} />
      {/* Controls */}
    </div>
  )
}
```

## Database

SQLite with migrations. Per-account databases in `userData/databases/`.

**Adding a migration:**

1. Add to `src/db/schema.ts`:
```typescript
export const MIGRATIONS = [
  // existing migrations...
  {
    version: 2,
    name: 'add_your_table',
    sql: `CREATE TABLE your_table (...)`,
  },
]
```

**Querying:**

```typescript
import { dbManager } from '../db'

const db = dbManager.getDb()
const rows = db.prepare('SELECT * FROM accounts').all()
```

## Testing

**Unit tests** with Vitest:
```typescript
// yourModule.test.ts
import { describe, it, expect } from 'vitest'

describe('YourModule', () => {
  it('should do something', () => {
    expect(yourFunction()).toBe(expected)
  })
})
```

**Integration tests** with Playwright (TODO):
```typescript
import { test, expect } from '@playwright/test'

test('authentication flow', async ({ page }) => {
  // Test e2e flow
})
```

## Debugging

**Renderer:**
- DevTools open automatically in dev mode
- Use React DevTools extension
- Console logs visible in DevTools

**Main Process:**
- Logs to terminal where `npm run dev` was run
- Use `console.log()` in main process code
- Attach Node.js debugger with VS Code

**VS Code Launch Config:**
```json
{
  "type": "node",
  "request": "launch",
  "name": "Electron Main",
  "runtimeExecutable": "${workspaceFolder}/node_modules/.bin/electron",
  "program": "${workspaceFolder}/dist/main/index.js"
}
```

## Troubleshooting

**Build fails:**
- Delete `node_modules` and `dist`
- Run `npm install` again
- Check Node.js version (need 20+)

**Database locked:**
- Close all running instances
- Delete `userData/databases/*.db-wal` files

**IPC not working:**
- Check schema validation (common cause of silent failures)
- Verify handler is registered in `src/main/index.ts`
- Check browser console for errors

**Hot reload not working:**
- Restart dev server
- Check Vite config
- Ensure file is in `src/renderer/`

## Contributing

1. Follow existing code style (Prettier handles formatting)
2. Add tests for new features
3. Update documentation
4. Run linter before committing

## License

MIT - see LICENSE file
