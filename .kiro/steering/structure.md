# Krono Agent - Repository Structure

## Current Structure

```
Krono_Agent/
├── .git/
├── .kiro/
│   ├── steering/
│   │   ├── product.md
│   │   ├── tech.md
│   │   └── structure.md (this file)
│   └── specs/
│       └── krono-agent/
│           ├── requirements.md
│           ├── design.md
│           └── tasks.md
├── LICENSE (MIT, preserved)
└── (to be created...)
```

## Planned Structure

```
Krono_Agent/
├── .github/
│   ├── workflows/
│   │   ├── ci.yml                 # Build + test matrix
│   │   └── release.yml            # Package and publish
│   └── ISSUE_TEMPLATE/
│
├── .kiro/                         # Kiro Crew artifacts (tracked)
│   ├── steering/
│   └── specs/
│
├── docs/
│   ├── setup.md                   # Installation and configuration
│   ├── connections.md             # Provider and plugin setup
│   ├── games.md                   # Game catalog
│   └── architecture.md            # Technical overview
│
├── src/
│   ├── main/                      # Electron main process
│   │   ├── index.ts
│   │   ├── ipc/                   # IPC handlers with Zod validation
│   │   ├── permissions/           # Permission enforcement
│   │   ├── credentials/           # OS keychain integration
│   │   ├── sync/                  # Supabase sync worker
│   │   └── native/                # Platform adapters
│   │       ├── windows.ts
│   │       ├── macos.ts
│   │       └── linux.ts
│   │
│   ├── renderer/                  # Application UI
│   │   ├── index.html
│   │   ├── main.tsx
│   │   ├── App.tsx
│   │   ├── components/            # Reusable UI components
│   │   ├── features/              # Feature-specific modules
│   │   │   ├── auth/
│   │   │   ├── chat/
│   │   │   ├── games/
│   │   │   ├── terminal/
│   │   │   ├── browser/
│   │   │   ├── projects/
│   │   │   ├── automations/
│   │   │   ├── plugins/
│   │   │   ├── settings/
│   │   │   └── connections/
│   │   ├── layouts/               # Sidebar, panels, shell
│   │   ├── stores/                # Zustand state
│   │   ├── hooks/                 # React hooks
│   │   ├── lib/                   # Utilities
│   │   └── styles/                # Tailwind config, global CSS
│   │
│   ├── agent/                     # Agent execution context
│   │   ├── runtime.ts             # Agent loop
│   │   ├── modes.ts               # Chat/Plan/Agent/Goal modes
│   │   ├── tools/                 # Tool implementations
│   │   ├── providers/             # Model provider adapters
│   │   └── events.ts              # Event streaming
│   │
│   ├── preload/                   # Preload scripts
│   │   └── index.ts
│   │
│   ├── sidecar/                   # Native processes
│   │   ├── llama/                 # llama.cpp integration
│   │   ├── whisper/               # whisper.cpp integration
│   │   └── mcp/                   # MCP server lifecycle
│   │
│   ├── games/                     # Game modules
│   │   ├── index.ts               # Registry
│   │   ├── types.ts               # GameModule interface
│   │   ├── minesweeper/
│   │   ├── snake/
│   │   ├── tic-tac-toe/
│   │   ├── maze-chase/
│   │   ├── pong/
│   │   ├── block-fit/
│   │   ├── falling-blocks/
│   │   ├── sand-blocks/
│   │   ├── flap/
│   │   ├── darts/
│   │   ├── jigsaw/
│   │   ├── stack/
│   │   ├── orbit/
│   │   ├── color-gauntlet/
│   │   ├── odd-color/
│   │   ├── 2048/
│   │   ├── breakout/
│   │   ├── memory-match/
│   │   ├── sudoku/
│   │   ├── solitaire/
│   │   ├── connect-four/
│   │   ├── reversi/
│   │   ├── simon/
│   │   └── lights-out/
│   │
│   ├── shared/                    # Types and schemas
│   │   ├── types/
│   │   ├── schemas/               # Zod schemas
│   │   └── constants.ts
│   │
│   └── db/                        # Database setup
│       ├── migrations/            # SQLite migrations
│       └── schema.ts
│
├── supabase/                      # Supabase project config
│   ├── migrations/
│   ├── config.toml
│   └── seed.sql
│
├── assets/                        # Static assets
│   ├── logo/
│   │   ├── logo.svg
│   │   ├── logo-mono.svg
│   │   ├── wordmark.svg
│   │   └── icons/                 # App icons (16-1024px)
│   ├── games/                     # Game artwork
│   ├── sounds/                    # UI and game sounds
│   └── themes/                    # Theme-specific assets
│
├── tests/
│   ├── unit/                      # Vitest
│   ├── integration/               # Playwright Electron
│   └── fixtures/
│
├── scripts/
│   ├── download-models.ts         # Model download helper
│   ├── build-llama.ts             # llama.cpp build automation
│   └── verify-release.ts          # Release artifact verification
│
├── site/                          # GitHub Pages documentation site
│   ├── index.html
│   ├── setup.html
│   ├── games.html
│   └── download.html
│
├── .gitignore
├── .eslintrc.js
├── package.json
├── tsconfig.json
├── tsconfig.node.json
├── vite.config.ts
├── tailwind.config.js
├── electron-builder.yml
├── LICENSE
└── README.md
```

## Module Boundaries

### Main Process (`src/main/`)
**Responsibilities:**
- IPC validation and routing
- Permission checking
- Credential storage/retrieval
- File system operations
- Process lifecycle (terminal, browser, sidecars)
- Native platform integration
- Synchronization coordination

**Not Allowed:**
- Direct UI rendering
- Model business logic
- Game implementation

### Renderer (`src/renderer/`)
**Responsibilities:**
- UI components and layouts
- User input handling
- State management (Zustand)
- Server state (TanStack Query)
- Theme rendering
- Accessibility

**Not Allowed:**
- Direct file system access
- Direct credential access
- Bypassing IPC validation

### Agent Context (`src/agent/`)
**Responsibilities:**
- Model provider communication
- Tool execution coordination
- Mode enforcement (Chat/Plan/Agent/Goal)
- Budget tracking
- Event streaming
- Approval requests

**Not Allowed:**
- Direct UI updates (use events)
- Permission enforcement (delegates to main)

### Games (`src/games/`)
**Responsibilities:**
- Self-contained game logic
- Canvas rendering
- Input handling
- State serialization/deserialization
- Score tracking
- Bot AI

**Not Allowed:**
- Accessing agent context
- Network requests
- File system writes outside save API
- Blocking main thread for computation

### Sidecars (`src/sidecar/`)
**Responsibilities:**
- Native binary orchestration
- Process isolation
- Authenticated communication
- Resource cleanup

**Not Allowed:**
- Accessing main database directly
- Credential storage

## File Naming Conventions

- Components: `PascalCase.tsx`
- Hooks: `use*.ts`
- Utilities: `camelCase.ts`
- Types: `*.types.ts`
- Schemas: `*.schema.ts`
- Tests: `*.test.ts` or `*.spec.ts`
- Constants: `UPPER_SNAKE_CASE`

## Import Structure

Enforce clean dependency direction:
```
shared → games
shared → sidecar
shared → agent → main → renderer
```

No circular dependencies.
Use path aliases configured in tsconfig:
```
@/components
@/features
@/lib
@/stores
@/agent
@/games
@/shared
```

## Configuration Files

- `.env.example` - Environment template (no secrets)
- `.env.local` - Local development (gitignored)
- `electron-builder.yml` - Packaging config
- `vite.config.ts` - Build and dev server
- `tailwind.config.js` - Theme and design tokens
- `tsconfig.json` - Main TypeScript config
- `tsconfig.node.json` - Node.js scripts

## Asset Organization

**Logo variants:**
- Full color (theme-aware)
- Monochrome
- Wordmark
- App icons (ICO, ICNS, PNG 16-1024)

**Game assets per game:**
- Artwork (SVG preferred)
- Sounds (MP3 or OGG)
- Sprites if needed

**Themes:**
- CSS variables in Tailwind config
- Dark/Light/Halloween/System

## Documentation Updates

This file is updated as the repository structure evolves.

**Update triggers:**
- New top-level directory
- New major module
- Changed module responsibilities
- New configuration file
- Architectural decision affecting structure

**Change log:**
- 2026-11-03: Initial structure planning
