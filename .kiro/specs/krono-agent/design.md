# Krono Agent - Design Document

## Architecture Overview

Krono Agent uses a multi-process Electron architecture with strict separation between trusted main process, UI renderer, agent execution context, isolated web contents, game workers, and native sidecars.

```
┌─────────────────────────────────────────────────────────────────┐
│                         Main Process (Trusted)                   │
│  - IPC validation (Zod schemas)                                 │
│  - Permission enforcement                                        │
│  - Credential management (OS keychain)                           │
│  - File operations                                               │
│  - Process lifecycle (terminal, browser, MCP, inference)        │
│  - Sync coordination                                             │
│  - Native platform adapters                                      │
└────────────┬────────────────────────────────────────────────────┘
             │
     ┌───────┴───────┬─────────────┬──────────────┬────────────┐
     │               │             │              │            │
┌────▼────┐   ┌─────▼──────┐ ┌───▼────┐  ┌──────▼──────┐  ┌─▼──────┐
│Renderer │   │Agent Context│ │Browser │  │MCP Servers  │  │Sidecars│
│(React)  │   │            │  │(WebView)│  │(stdio/HTTP) │  │llama.cpp│
│         │   │- Provider  │  │        │  │             │  │whisper  │
│- UI     │   │  adapters  │  │- Tabs  │  │- Tools      │  │         │
│- State  │   │- Tool exec │  │- Context│  │- OAuth      │  │- Auth   │
│- Events │   │- Budget    │  │  isol. │  │- Lifecycle  │  │  loopback│
└─────────┘   │- Streaming │  └────────┘  └─────────────┘  └────────┘
              │- Events    │
              └────────────┘
```

### Data Flow: Chat to Execution

```
1. User types message in composer (renderer)
2. IPC to main with Zod validation
3. Main checks permissions
4. Agent context receives request
5. Agent loads model provider adapter
6. Provider streams response
   ├─ If Chat mode: no tool schemas sent
   ├─ If Plan mode: read-only tools only
   └─ If Agent/Goal: all permitted tools
7. Model returns tool calls
8. Agent validates with Zod
9. Main checks permission (may prompt user)
10. Tool executor runs (filesystem/terminal/browser/desktop)
11. Result streamed back to model
12. Model continues or completes
13. Events streamed to renderer
14. UI updates activity view
```

### Synchronization Flow

```
Local Change:
1. User action (renderer)
2. Optimistic UI update
3. SQLite write with tx
4. Outbox entry created
5. Background sync worker triggered
6. Worker reads outbox
7. Supabase API call with RLS
8. Success: remove from outbox
9. Conflict: detect with version check
10. Conflict UI for resolution

Remote Change (subscription):
1. Supabase realtime notification
2. Sync worker receives event
3. Check version/tombstone
4. SQLite update
5. Notify renderer
6. UI updates
```

## UI Design

### Color System

**Dark Theme:**
```css
--bg-primary: #111316
--bg-sidebar: #15181D
--bg-surface: #1B2027
--border: #2B323C
--text-primary: #F1F3F5
--text-muted: #A5ADB8
--accent: #8FD7C7
--accent-hover: #A0E5D5
```

**Light Theme:**
```css
--bg-primary: #F6F7F9
--bg-surface: #FFFFFF
--bg-hover: #EBEDF0
--border: #D1D5DB
--text-primary: #171B22
--text-muted: #6B7280
--accent: #237D70
--accent-hover: #2A9685
```

**Halloween Theme:**
```css
--bg-primary: #15101D
--bg-surface: #21182C
--bg-hover: #2C1F3A
--border: #3D2F4F
--text-primary: #F7F0E7
--text-muted: #C4B8A8
--accent-pumpkin: #FF9B50
--accent-violet: #B89AFF
```

### Typography Scale

```css
--font-prose: 'Inter', system-ui, sans-serif
--font-code: 'JetBrains Mono', 'Consolas', monospace

--text-xs: 0.75rem     /* 12px */
--text-sm: 0.8125rem   /* 13px */
--text-base: 0.9375rem /* 15px - default prose */
--text-lg: 1.0625rem   /* 17px */
--text-xl: 1.25rem     /* 20px */
--text-2xl: 1.5rem     /* 24px */

--code-sm: 0.8125rem   /* 13px - default code */
--code-base: 0.875rem  /* 14px */
--code-lg: 0.9375rem   /* 15px */
```

### Spacing Rhythm (8px base)

```css
--space-1: 0.5rem   /* 8px */
--space-2: 1rem     /* 16px */
--space-3: 1.5rem   /* 24px */
--space-4: 2rem     /* 32px */
--space-6: 3rem     /* 48px */
--space-8: 4rem     /* 64px */
```

### Border Radii

```css
--radius-sm: 0.375rem   /* 6px - buttons, inputs */
--radius-md: 0.75rem    /* 12px - controls, chips */
--radius-lg: 1rem       /* 16px - cards, panels */
--radius-xl: 1.5rem     /* 24px - modals, game cards */
```

### Motion

```css
--duration-fast: 140ms
--duration-base: 180ms
--duration-slow: 240ms
--easing: cubic-bezier(0.4, 0, 0.2, 1)
```

Respect `prefers-reduced-motion`:
```css
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    transition-duration: 0.01ms !important;
  }
}
```

### Layout Dimensions

```
Sidebar:
- Default width: 248px
- Min width: 200px
- Max width: 400px
- Collapsible

Main Content:
- Chat text max width: ~820px
- Code blocks/tables: wider allowed

Right Panel:
- Default width: 400px
- Min width: 280px
- Max width: 600px

Terminal Drawer:
- Default height: 280px
- Min height: 120px
- Max height: 60% of window
```

### Component Hierarchy

```
App
├─ AuthShell (if not authenticated)
│  └─ AuthForm (email/password, OAuth buttons)
├─ MainShell (if authenticated)
   ├─ Sidebar
   │  ├─ NewChatButton
   │  ├─ SearchInput
   │  ├─ ProjectList (collapsible)
   │  │  └─ ChatList (nested)
   │  ├─ PinnedChats
   │  ├─ ArchivedChats
   │  ├─ NavLinks (Games, Automations, Connections, Plugins)
   │  ├─ AccountMenu
   │  └─ SettingsButton
   ├─ MainContent
   │  ├─ ChatView
   │  │  ├─ MessageList
   │  │  │  ├─ UserMessage
   │  │  │  ├─ AssistantMessage
   │  │  │  ├─ ToolCallEvent
   │  │  │  ├─ ApprovalRequest
   │  │  │  └─ ActivitySummary
   │  │  └─ Composer
   │  │     ├─ TextArea (Markdown)
   │  │     ├─ ModelSelector
   │  │     ├─ ModeSelector (Chat/Plan/Agent/Goal)
   │  │     ├─ PermissionSelector
   │  │     ├─ AttachmentButton
   │  │     ├─ MicButton (dictation)
   │  │     └─ SendButton
   │  ├─ GamesView
   │  │  ├─ GameCatalog (grid)
   │  │  └─ GamePlayer
   │  │     ├─ GameCanvas
   │  │     ├─ GameControls
   │  │     └─ TaskStrip (during active agent)
   │  ├─ AutomationsView
   │  ├─ ConnectionsView
   │  ├─ PluginsView
   │  └─ SettingsView
   ├─ RightPanel (optional)
   │  ├─ SideChat
   │  ├─ BrowserPanel
   │  ├─ FileViewer
   │  ├─ DiffViewer
   │  └─ ArtifactViewer
   └─ TerminalDrawer
      └─ TerminalTabs (xterm.js)
```

## Logo Design

### Requirements
- Two offset rounded geometric forms
- Negative space suggests hourglass
- Subtly forms letter K
- Small square pixel accent
- Original (not emoji/stock)
- Scalable vector (SVG)
- Readable at 16px minimum

### Conceptual Geometry

```
Form A: Rounded rectangle (vertical)
Position: Upper-left
Dimensions: ~40% height

Form B: Rounded rectangle (vertical, offset)
Position: Lower-right, overlapping
Dimensions: ~40% height

Negative Space: Formed between offset forms
Shape: Hourglass silhouette + K leg

Pixel Accent: Small filled square
Position: Middle-left or middle-right
Purpose: Tech/digital reference
```

### Variants Needed
1. `logo.svg` - Full color, theme-aware
2. `logo-mono.svg` - Monochrome for app icons
3. `wordmark.svg` - Logo + "Krono Agent" text
4. App icons: 16px, 32px, 64px, 128px, 256px, 512px, 1024px (PNG)
5. ICO (Windows): Multi-resolution container
6. ICNS (macOS): Multi-resolution container

## Game Design Specifications

### Common Interface

```typescript
interface GameModule {
  id: string
  name: string
  category: 'puzzle' | 'arcade' | 'strategy'
  description: string
  thumbnailUrl: string
  
  // Lifecycle
  mount(canvas: HTMLCanvasElement): void
  unmount(): void
  pause(): void
  resume(): void
  restart(): void
  
  // Input
  handleInput(event: InputEvent): void
  
  // State
  serialize(): GameState
  deserialize(state: GameState): void
  
  // Settings
  getSettings(): GameSettings
  updateSettings(settings: Partial<GameSettings>): void
  
  // Metadata
  isComplete(): boolean
  getScore(): number
  getResult(): 'win' | 'loss' | 'ongoing'
}

interface GameState {
  timestamp: number
  score: number
  [key: string]: any  // Game-specific state
}

interface GameSettings {
  soundEnabled: boolean
  difficulty?: 'easy' | 'medium' | 'hard'
  [key: string]: any  // Game-specific settings
}
```

### Individual Game Specifications

#### 1. Minesweeper
- Grid: 10×10 (easy), 16×16 (medium), 30×16 (hard)
- Mines: 10, 40, 99
- First click: always safe (0 neighboring mines)
- Controls: Left click reveal, right click flag
- Win: All non-mines revealed
- Loss: Mine revealed

#### 2. Snake
- Grid: 20×20
- Start length: 3
- Food: Random placement, not on snake
- Growth: +1 per food
- Speed: Increases every 5 foods
- Loss: Wall collision or self-collision
- Controls: Arrow keys or WASD

#### 3. Tic-Tac-Toe
- Grid: 3×3
- Player: X, Bot: O
- Bot: Minimax algorithm (perfect play)
- Difficulty: Easy (random errors), Medium (some lookahead), Hard (perfect)
- Win: 3 in row/column/diagonal
- Draw: Board full, no win

#### 4. Maze Chase
- Original maze layout (not Pac-Man map)
- Player character: Original design
- Ghosts: 4, each with distinct behavior
  - Chaser: Direct pursuit
  - Ambusher: Targets ahead of player
  - Patroller: Patrols zones
  - Random: Unpredictable movement
- Pellets: Clear all to win
- Power-ups: Temporary ghost vulnerability (10 seconds)
- Scatter mode: Ghosts retreat to corners periodically
- Loss: Ghost collision when vulnerable off

#### 5. Pong
- Paddle: Player-controlled (mouse or keys)
- Bot paddle: Predictive AI with limited speed
- Ball: Physics-based with angle variation
- Score: First to 11 wins
- Ball speed: Increases after every 5 volleys

#### 6. Block Fit
- Board: 8×8
- Pieces: Tray of 3, random polyomino shapes (1-5 blocks)
- Placement: Drag to board, no rotation
- Clear: Full row or column
- Loss: No remaining piece fits
- Score: Blocks placed + lines cleared

#### 7. Falling Blocks
- Board: 10×20
- Pieces: 7 tetrominoes (I, O, T, S, Z, L, J)
- Rotation: Yes (SRS system)
- Hold: 1 piece
- Queue: Next 3 visible
- Drop: Soft (accelerate), hard (instant)
- Clear: Full rows
- Loss: Piece locks above board
- Speed: Increases per level (every 10 lines)

#### 8. Sand Blocks
- Board: 10×16
- Pieces: Colored tetrominoes → individual grains on landing
- Physics: Grains fall via cellular automaton (Worker)
- Clear: Connected same-color region spanning left→right
- Loss: Stack reaches top
- Worker isolation: Physics in Web Worker

#### 9. Flap
- Original bird/character design
- Obstacles: Vertical gaps in pillars
- Controls: Space or click to flap
- Physics: Gravity, flap impulse
- Collision: Precise hitbox (not pixel-perfect)
- Score: Obstacles passed
- Loss: Collision with obstacle or ground

#### 10. Darts
- Target: Standard dartboard (sectors, bullseye)
- Aiming: Mouse position
- Throw: Timed click accuracy (moving reticle)
- Game: 301, subtract score
- Bust: Exceeding zero or landing on negative
- Win: Exact zero
- No double-out requirement

#### 11. Jigsaw
- Images: Original artwork sets (3 themes × 3 sizes)
- Sizes: 3×3 (9 pieces), 4×4 (16), 5×5 (25)
- Pieces: Irregular interlocking shapes
- Snapping: Pieces snap within threshold
- Optional: Load local image file
- Win: All pieces placed correctly

#### 12. Stack
- Blocks: Moving horizontally or depth-wise
- Placement: Click/tap to drop
- Overhang: Trimmed, block width decreases
- Perfect bonus: 3 consecutive perfect → restore 8% width (capped at original)
- Loss: Block too small or miss
- Score: Height achieved

#### 13. Orbit
- Rotating pointer around circle
- Target: Arc segment (e.g., 30°)
- Bullseye: Smaller arc within target (e.g., 10°)
- Click: Stop pointer
- Target hit: 1 point, new target, speed increases
- Bullseye hit: 3 points
- Miss: Game over
- Score: Total points

#### 14. Color Gauntlet
- Show: 5 colors sequentially (2s each)
- Recreate: HSL sliders (Hue, Saturation, Lightness)
- Score: Perceptual distance (Delta E or CIEDE2000)
- Rounds: 5
- Total score: Sum of all rounds (lower = better)
- Display: Original vs. recreated colors after each

#### 15. Odd Color
- Grid: NxN tiles (start 3×3)
- Colors: N²-1 same, 1 different
- Task: Click the different tile
- Progression: Increase N, decrease color difference
- Accessible mode: Larger difference maintained
- Loss: Wrong tile
- Score: Rounds completed

#### 16. 2048
- Grid: 4×4
- Tiles: Powers of 2 (2, 4, 8, ..., 2048, ...)
- Movement: Swipe/arrow keys
- Merge: Adjacent same-value tiles combine
- Win: Create 2048 tile (can continue)
- Loss: No valid moves
- Score: Sum of merges

#### 17. Breakout
- Paddle: Mouse or keyboard
- Ball: Physics-based with angle variation
- Bricks: Original layouts (multiple levels)
- Power-ups: Multi-ball, wider paddle, etc.
- Lives: 3
- Win: Clear all bricks
- Loss: Lose all lives

#### 18. Memory Match
- Grid: 4×4, 6×6, 8×8
- Cards: Pairs of images (original artwork)
- Flip: 2 cards per turn
- Match: Cards stay revealed
- No match: Cards flip back
- Win: All pairs matched
- Score: Moves + time

#### 19. Sudoku
- Grid: 9×9, standard regions
- Puzzle: Offline validated bank (unique solutions)
- Notes: Pencil marks
- Hints: Highlight conflicts
- Difficulty: Easy, Medium, Hard (by given count)
- Win: Valid complete fill
- Validation: Real-time conflict detection

#### 20. Solitaire (Klondike)
- Deck: Standard 52 cards
- Draw: One card
- Tableau: 7 piles
- Foundations: 4, Ace to King by suit
- Rules: Alternating colors, descending rank
- Undo: Full history
- Win: All cards in foundations

#### 21. Connect Four
- Grid: 7×6
- Players: Human vs. Bot
- Bot: Minimax with alpha-beta, depth limit 6
- Evaluation: Center control, threats, connections
- Win: 4 in row/column/diagonal
- Draw: Board full

#### 22. Reversi (Othello)
- Grid: 8×8
- Players: Human (Black) vs. Bot (White)
- Bot: Alpha-beta, depth 5-7
- Evaluation: Corners, mobility, position weight
- Rules: Flips opponent's pieces
- Pass: If no legal moves
- Win: Most pieces when both pass or board full

#### 23. Simon
- Sequence: Audiovisual (color + sound)
- Playback: Show full sequence
- Input: Click colors in order
- Progression: Add one per round
- Visual-only mode: Reduced-motion alternative
- Loss: Wrong input
- Score: Rounds completed

#### 24. Lights Out
- Grid: 5×5
- Lights: On/off toggle
- Action: Toggle cell + 4 orthogonal neighbors
- Win: All lights off
- Generation: Start solved, apply random toggles (ensures solvable)
- Optimal: Track move count

### Game Artwork Guidelines

- **Style:** Cohesive across all games
- **Palette:** Theme-aware (adapt to Dark/Light/Halloween)
- **Miniatures:** 240×180px cards, representative of gameplay
- **In-game:** Crisp at 1× and 2× pixel density
- **Format:** SVG where possible, PNG for raster
- **Attribution:** Original assets or open-licensed with attribution

### Game Audio Guidelines

- **Format:** MP3 or OGG, <100KB per file
- **Types:** UI sounds (click, success, fail), ambient (optional per game)
- **Mute:** Per-game setting
- **Volume:** Normalized, not jarring
- **Reduced motion:** Audio-only feedback when animations disabled

## Provider Adapter Interface

```typescript
interface ProviderAdapter {
  id: string
  name: string
  
  // Configuration
  configure(config: ProviderConfig): Promise<void>
  testConnection(): Promise<ConnectionTestResult>
  
  // Models
  listModels(): Promise<Model[]>
  getModelCapabilities(modelId: string): ModelCapabilities
  
  // Inference
  createCompletion(request: CompletionRequest): AsyncIterable<CompletionChunk>
  cancelCompletion(requestId: string): Promise<void>
}

interface ModelCapabilities {
  supportsTools: boolean
  supportsImages: boolean
  supportsReasoningEffort: boolean
  supportsStructuredOutput: boolean
  contextWindow: number
  maxOutputTokens: number
  streamingSupported: boolean
}

interface CompletionRequest {
  model: string
  messages: Message[]
  tools?: Tool[]
  temperature?: number
  maxTokens?: number
  reasoningEffort?: 'low' | 'medium' | 'high'
  stream?: boolean
}

interface CompletionChunk {
  type: 'text' | 'tool_call' | 'done' | 'error'
  content?: string
  toolCall?: ToolCall
  usage?: Usage
  error?: Error
}
```

### Provider Implementations

Each adapter handles:
- API authentication (key, OAuth)
- Request formatting
- Response parsing
- Error mapping
- Streaming
- Cancellation
- Rate limits

**Providers:**
1. OpenAI (official SDK)
2. Anthropic (official SDK)
3. Google Gemini (official SDK)
4. Azure OpenAI (official SDK with endpoint/deployment)
5. xAI (OpenAI-compatible)
6. OpenRouter (OpenAI-compatible)
7. Groq (OpenAI-compatible)
8. Mistral (official SDK)
9. Ollama (OpenAI-compatible)
10. LM Studio (OpenAI-compatible)
11. Custom (OpenAI-compatible with user URL)
12. Local (llama.cpp sidecar)

## Tool Implementation Pattern

```typescript
interface ToolDefinition {
  name: string
  description: string
  inputSchema: z.ZodSchema
  capabilities: ToolCapabilities
  
  execute(input: unknown, context: ExecutionContext): Promise<ToolResult>
  cancel?(executionId: string): Promise<void>
}

interface ToolCapabilities {
  requiresPermission: 'none' | 'read' | 'write' | 'execute' | 'desktop'
  canCancel: boolean
  longRunning: boolean
}

interface ExecutionContext {
  projectDirs: string[]
  permissions: PermissionProfile
  requestApproval(action: ApprovalRequest): Promise<ApprovalResult>
  emitProgress(progress: Progress): void
}

interface ToolResult {
  success: boolean
  output?: string
  error?: string
  artifacts?: Artifact[]
}
```

### Core Tools

**File Operations:**
- `list_files`: Recursive listing with glob filters
- `read_file`: Text, PDF, DOCX, XLSX readers
- `search_files`: Regex with context lines
- `write_file`: Create/overwrite with snapshot
- `edit_file`: Patch application with unified diff
- `move_file`: Move/rename with conflict check
- `delete_file`: With confirmation
- `create_snapshot`: Backup before edit
- `restore_snapshot`: Rollback to backup

**Terminal:**
- `execute_command`: PTY with streaming I/O
- `send_stdin`: Input to running command
- `interrupt_command`: Ctrl+C
- `list_terminals`: User vs. agent sessions

**Browser:**
- `browser_open`: New tab with URL
- `browser_navigate`: Go to URL in existing tab
- `browser_inspect`: DOM/accessibility tree
- `browser_click`: Element or coordinates
- `browser_type`: Input text
- `browser_screenshot`: Full page or element
- `browser_cookies`: Get/set cookies
- `browser_history`: Back/forward

**Desktop:**
- `desktop_screenshot`: Full screen or window
- `desktop_mouse`: Move, click, drag
- `desktop_keyboard`: Type, shortcuts
- `desktop_scroll`: Scroll in window
- `desktop_apps`: List running apps
- `desktop_focus`: Bring window to front
- `desktop_clipboard`: Read/write

**Media:**
- `generate_image`: Text to image
- `edit_image`: Image to image with mask
- `generate_video`: Text to video (xAI)

**Scheduling:**
- `create_automation`: One-time or recurring task
- `list_automations`: Current automations
- `pause_automation`: Disable without deleting
- `delete_automation`: Remove permanently

## Permission Enforcement

### Ask Profile
All mutations prompt:
- File writes
- File deletes
- Command execution
- Desktop control writes
- External API calls

### Project Profile
Scoped file operations allowed:
- Canonicalize paths
- Check against project directories
- Allow reads/writes inside scope
- Still prompt for commands and desktop

### Full Computer Profile
Explicit activation:
- Confirmation dialog with risk statement
- Visual indicator (banner or badge)
- All capabilities available

### Path Canonicalization

```typescript
function canonicalizePath(path: string, projectDirs: string[]): CheckResult {
  const resolved = resolvePath(path) // Handle ~, .., symlinks
  
  for (const projectDir of projectDirs) {
    if (resolved.startsWith(projectDir)) {
      return { allowed: true, scope: 'project' }
    }
  }
  
  return { allowed: false, reason: 'Outside project scope' }
}
```

### Approval Binding

```typescript
interface ApprovalRequest {
  id: string
  action: string // 'write_file', 'execute_command', etc.
  target: string // Path, command, URL
  details: Record<string, any>
}

interface ApprovalResult {
  approved: boolean
  applyToSimilar?: boolean // "Always allow for this session"
}

// Store approved actions with exact targets
// Changed target = new approval
```

## Synchronization Architecture

### Local Database Schema (SQLite)

```sql
CREATE TABLE accounts (
  id TEXT PRIMARY KEY,
  email TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE TABLE chats (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  project_id TEXT,
  name TEXT,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);

CREATE TABLE messages (
  id TEXT PRIMARY KEY,
  chat_id TEXT NOT NULL,
  role TEXT NOT NULL, -- 'user' | 'assistant' | 'system'
  content TEXT,
  created_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (chat_id) REFERENCES chats(id)
);

CREATE TABLE projects (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  name TEXT NOT NULL,
  icon TEXT,
  color TEXT,
  description TEXT,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);

CREATE TABLE project_directories (
  id TEXT PRIMARY KEY,
  project_id TEXT NOT NULL,
  device_id TEXT NOT NULL,
  path TEXT NOT NULL,
  FOREIGN KEY (project_id) REFERENCES projects(id)
);

CREATE TABLE memories (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  project_id TEXT, -- NULL = global
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (account_id) REFERENCES accounts(id),
  FOREIGN KEY (project_id) REFERENCES projects(id)
);

CREATE TABLE outbox (
  id TEXT PRIMARY KEY,
  operation TEXT NOT NULL, -- 'insert', 'update', 'delete'
  table_name TEXT NOT NULL,
  record_id TEXT NOT NULL,
  payload TEXT NOT NULL, -- JSON
  created_at INTEGER NOT NULL,
  retries INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE game_scores (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  game_id TEXT NOT NULL,
  score INTEGER NOT NULL,
  achieved_at INTEGER NOT NULL,
  synced INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);

CREATE TABLE game_saves (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  game_id TEXT NOT NULL,
  state TEXT NOT NULL, -- JSON
  saved_at INTEGER NOT NULL,
  synced INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);
```

### Supabase Schema (PostgreSQL)

```sql
-- Enable RLS on all tables
ALTER TABLE accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE chats ENABLE ROW LEVEL SECURITY;
ALTER TABLE messages ENABLE ROW LEVEL SECURITY;
-- ... (same tables as SQLite, with RLS policies)

-- RLS Policies
CREATE POLICY "Users can only access their own data"
  ON accounts FOR ALL
  USING (auth.uid() = id);

CREATE POLICY "Users can only access their own chats"
  ON chats FOR ALL
  USING (account_id = auth.uid());

-- ... (similar policies for all account-owned tables)
```

### Sync Worker Logic

```typescript
class SyncWorker {
  async syncOutbox() {
    const pending = await this.db.outbox.findAll()
    
    for (const entry of pending) {
      try {
        await this.syncEntry(entry)
        await this.db.outbox.delete(entry.id)
      } catch (error) {
        if (error.code === 'CONFLICT') {
          await this.handleConflict(entry, error)
        } else if (entry.retries < 3) {
          await this.db.outbox.incrementRetries(entry.id)
        } else {
          // Move to failed queue
          await this.db.failedOps.insert(entry)
          await this.db.outbox.delete(entry.id)
        }
      }
    }
  }
  
  async handleConflict(entry: OutboxEntry, error: ConflictError) {
    const local = await this.db[entry.table_name].find(entry.record_id)
    const remote = error.remoteVersion
    
    // Show conflict UI
    const resolution = await this.promptConflictResolution(local, remote)
    
    if (resolution.choice === 'keep_local') {
      await this.forceUpdate(entry, local.version + 1)
    } else if (resolution.choice === 'keep_remote') {
      await this.applyRemote(remote)
    } else {
      // User merged manually
      await this.applyMerged(resolution.merged)
    }
    
    await this.db.outbox.delete(entry.id)
  }
}
```

## Side Chat Implementation

```typescript
interface SideChat {
  id: string
  parentChatId: string
  branchPoint: string // Message ID
  contextSnapshot: Message[]
  createdAt: number
  expiresAt: number
  promoted: boolean
}

class SideChatManager {
  create(parentChatId: string, branchPoint: string): SideChat {
    const parent = this.getChat(parentChatId)
    const snapshot = parent.messages.filter(m => m.timestamp <= branchPoint.timestamp)
    
    const sideChat: SideChat = {
      id: generateId(),
      parentChatId,
      branchPoint,
      contextSnapshot: snapshot,
      createdAt: Date.now(),
      expiresAt: Date.now() + 24 * 60 * 60 * 1000,
      promoted: false
    }
    
    this.db.sideChats.insert(sideChat)
    return sideChat
  }
  
  async checkExpiry(sideChatId: string): Promise<boolean> {
    const sideChat = await this.db.sideChats.find(sideChatId)
    
    if (sideChat.promoted) {
      return false // Never expires
    }
    
    if (Date.now() > sideChat.expiresAt) {
      await this.expire(sideChatId)
      return true
    }
    
    return false
  }
  
  async expire(sideChatId: string) {
    // Cancel active execution
    await this.cancelActiveExecution(sideChatId)
    
    // Delete messages
    await this.db.messages.deleteWhere({ chat_id: sideChatId })
    
    // Delete unreferenced attachments
    await this.cleanupAttachments(sideChatId)
    
    // Mark as deleted
    await this.db.sideChats.update(sideChatId, { deleted_at: Date.now() })
  }
  
  async promote(sideChatId: string): Promise<Chat> {
    const sideChat = await this.db.sideChats.find(sideChatId)
    
    // Convert to regular chat
    const chat = await this.db.chats.insert({
      id: sideChatId,
      account_id: getCurrentAccountId(),
      name: `Promoted from ${sideChat.parentChatId}`,
      created_at: sideChat.createdAt,
      updated_at: Date.now()
    })
    
    // Mark as promoted
    await this.db.sideChats.update(sideChatId, { promoted: true })
    
    return chat
  }
  
  async transfer(sideChatId: string, messageId: string) {
    const sideChat = await this.db.sideChats.find(sideChatId)
    const message = await this.db.messages.find(messageId)
    const parent = await this.db.chats.find(sideChat.parentChatId)
    
    // Check if parent has active execution
    if (this.hasActiveExecution(parent.id)) {
      // Queue for next safe boundary
      await this.queueTransfer(parent.id, message)
    } else {
      // Transfer immediately
      await this.db.messages.insert({
        ...message,
        id: generateId(),
        chat_id: parent.id,
        created_at: Date.now()
      })
    }
  }
}
```

## Automation Implementation

```typescript
interface Automation {
  id: string
  name: string
  accountId: string
  deviceId: string // Designated execution device
  type: 'one-time' | 'recurring'
  schedule: Schedule
  timezone: string
  prompt: string
  modelId: string
  permissions: PermissionProfile
  projectId?: string
  enabled: boolean
  lastRun?: number
  nextRun: number
}

interface Schedule {
  type: 'cron' | 'interval' | 'datetime'
  expression: string // Cron expression or ISO datetime
}

class AutomationRunner {
  async runPending() {
    const automations = await this.db.automations.findDue()
    
    for (const automation of automations) {
      if (automation.deviceId !== this.deviceId) {
        continue // Not this device's job
      }
      
      if (!this.acquireLease(automation.id)) {
        continue // Another process has it
      }
      
      try {
        await this.execute(automation)
        await this.updateNextRun(automation)
      } finally {
        this.releaseLease(automation.id)
      }
    }
  }
  
  async handleMissed() {
    const missed = await this.db.automations.findMissed()
    
    for (const automation of missed) {
      // Only one catch-up execution
      if (this.shouldCatchUp(automation)) {
        await this.execute(automation)
      }
      await this.updateNextRun(automation)
    }
  }
  
  shouldCatchUp(automation: Automation): boolean {
    // Don't catch up if last run was recent
    if (automation.lastRun && Date.now() - automation.lastRun < 60000) {
      return false
    }
    
    // Only catch up if automation is important (user-configured flag)
    return automation.catchUpEnabled
  }
  
  async execute(automation: Automation) {
    const chat = await this.db.chats.create({
      account_id: automation.accountId,
      project_id: automation.projectId,
      name: `Automation: ${automation.name}`,
      created_at: Date.now()
    })
    
    const context: ExecutionContext = {
      chatId: chat.id,
      projectId: automation.projectId,
      permissions: automation.permissions,
      model: automation.modelId,
      budget: { time: 30 * 60 * 1000, calls: 100 }
    }
    
    await this.agent.run(automation.prompt, context)
    
    await this.db.automations.update(automation.id, {
      lastRun: Date.now()
    })
  }
}
```

## Error Handling and Failure Modes

### Network Failures
- Offline: Queue operations in outbox
- Reconnect: Automatic sync resume
- Partial sync: Retry with exponential backoff

### Provider Failures
- 401: Clear cached credentials, prompt re-auth
- 429: Show rate limit message, retry after delay
- 500: Retry with backoff, show provider error
- Timeout: Cancel, offer retry

### Permission Denied
- Show reason (outside scope, user denied)
- Offer alternative approach
- Do not retry automatically

### Tool Execution Failures
- Non-zero exit: Show exit code and stderr
- Timeout: Cancel, show timeout message
- Crash: Show error, offer retry
- Invalid input: Validate before execution

### Sync Conflicts
- Detect: Version mismatch
- Present: Side-by-side diff
- Resolve: User choice or merge
- Persist: Winning version

### Side Chat Expiry
- Warn: 1 hour before expiry
- Block: On expiry, deny access
- Cleanup: Delete data, notify user

### Desktop Control Emergency Stop
- Hotkey: Global hotkey (e.g., Ctrl+Shift+Esc or configurable)
- Cancel: All active desktop operations
- Pause: Agent execution
- Notify: Show stop confirmation

## Testing Strategy

### Unit Tests (Vitest)
- Provider adapters (contract tests)
- Tool validators (Zod schema checks)
- Permission checks (path canonicalization, approval binding)
- Game logic (rules, win/loss, bot legality)
- Sync logic (conflict detection, idempotency)

### Integration Tests (Playwright)
- Authentication flows (email, Google, GitHub, logout)
- Model request/response (streaming, cancellation)
- Tool execution (file ops, terminal, browser, desktop)
- Permission enforcement (Ask, Project, Full)
- Game lifecycle (launch, play, pause, serialize, restore)
- Sync (two-client test with conflict)

### Manual Verification
- Clean Windows install
- Model download and inference
- All 24 games playable
- Desktop control with emergency stop
- Side chat expiry and cleanup
- Automation execution and catch-up

### Performance Tests
- Launch time: Cold and warm
- Model TTFB: Local and cloud
- Terminal latency: Keystroke echo
- Game frame rate: All 24 games at 60 FPS
- File diff: Typical and large files

### Accessibility Tests
- Keyboard navigation: Complete app traversal
- Screen reader: NVDA (Windows), VoiceOver (macOS)
- Contrast: WCAG AA verification
- Reduced motion: Animation check

## Deployment and Release

### Build Pipeline (GitHub Actions)

```yaml
name: Build and Release
on:
  push:
    tags:
      - 'v*'

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '22'
      - run: npm ci
      - run: npm run build
      - run: npm run package:win
      - uses: actions/upload-artifact@v3
        with:
          name: windows-installer
          path: dist/*.exe
          
  build-macos:
    runs-on: macos-latest
    steps:
      # Similar to windows, package:mac
      
  build-linux:
    runs-on: ubuntu-latest
    steps:
      # Similar to windows, package:linux
      
  release:
    needs: [build-windows, build-macos, build-linux]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v3
      - uses: softprops/action-gh-release@v1
        with:
          files: |
            windows-installer/*
            macos-dmg/*
            linux-appimage/*
```

### Versioning

- Follow semantic versioning: MAJOR.MINOR.PATCH
- Git tags trigger releases
- package.json version is source of truth
- Installer includes version in filename

### Release Checklist

- [ ] Update CHANGELOG.md
- [ ] Bump version in package.json
- [ ] Run all tests locally
- [ ] Manual verification checklist completed
- [ ] Create Git tag
- [ ] Push tag to trigger CI
- [ ] Verify CI builds succeed
- [ ] Download and test each artifact
- [ ] Verify checksums
- [ ] Update documentation site with new download links
- [ ] Publish GitHub Release with notes
- [ ] Announce release (if applicable)

---

## Implementation Notes

This design document represents the complete target architecture. Implementation proceeds in milestones (see tasks.md) with continuous verification against these specifications.

All designs prioritize:
- User agency and control
- Data privacy and isolation
- Transparent behavior (no fabrication)
- Original creative work
- Cohesive polish

Changes to this design require updating this document and noting the decision rationale.
