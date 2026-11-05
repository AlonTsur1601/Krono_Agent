> 2026-11-04 owner-authorized change: the Windows interface is now C#/WinUI 3. Original Electron/React examples below are historical implementation guidance; current stack and paths are in .kiro/steering/tech.md and structure.md. Product requirements remain applicable.

> 2026-11-05 Codex continuation: native local/custom model endpoints, twelve native games and expiring side-conversation implementation are recorded in review-2026-11-05.md. Historical checkboxes below do not certify the full product; remaining scope is in IMPLEMENTATION_STATUS.md.

> 02:00 continuation: native provider contracts and local project/memory management are implemented and fixture-tested. See review-2026-11-04.md. Full cloud/provider/game/release acceptance remains incomplete.

> Continued 2026-11-04: see review-2026-11-04.md for dependency upgrades, user terminal implementation and actual verification. Full milestones remain incomplete.

> Supabase preparation 2026-11-03: migration, ownership policies, private bucket and setup guide implemented; embedded PostgreSQL isolation tests pass. Cloud deployment and live account/sync acceptance remain pending user project creation.

> Takeover review 2026-11-03: checklist entries below are not proof of acceptance. See review-2026-11-03.md and IMPLEMENTATION_STATUS.md for audited status. Product remains incomplete.

# Krono Agent - Implementation Tasks

## Milestone A: Foundation and Shell

**Goal:** Repository structure, .kiro documents, design system, logo, authentication shell, basic UI layout.

### A1: Repository Setup
- [x] Clone repository
- [x] Create .kiro directory structure
- [x] Write product.md, tech.md, structure.md
- [x] Write requirements.md, design.md, tasks.md
- [ ] Create .gitignore (node_modules, dist, .env.local, etc.)
- [ ] Create package.json with dependencies
- [ ] Create tsconfig.json and tsconfig.node.json
- [ ] Create electron-builder.yml
- [ ] Create vite.config.ts
- [ ] Create tailwind.config.js
- [ ] Set up ESLint and Prettier configs
- [ ] Create initial README.md
- [ ] Commit and push .kiro files

**Requirements:** Foundation for all other work  
**Verification:** npm install works, TypeScript compiles

### A2: Design Tokens and Theme System
- [ ] Create src/renderer/styles/tokens.css with CSS variables
- [ ] Implement Dark theme colors
- [ ] Implement Light theme colors
- [ ] Implement Halloween theme colors
- [ ] Create theme provider component
- [ ] Add system theme detection
- [ ] Implement theme persistence

**Requirements:** R10.2  
**Verification:** Switch themes, verify colors match spec

### A3: Logo Design and Assets
- [ ] Design original logo SVG (hourglass/K negative space, pixel accent)
- [ ] Create monochrome variant
- [ ] Create wordmark
- [ ] Generate app icons (16, 32, 64, 128, 256, 512, 1024)
- [ ] Create ICO (Windows)
- [ ] Create ICNS (macOS)
- [ ] Verify readability at all sizes

**Requirements:** R10.4  
**Verification:** Visual inspection at 16px, 32px, 256px

### A4: Main Application Shell
- [ ] Create Electron main process entry (src/main/index.ts)
- [ ] Create preload script (src/preload/index.ts)
- [ ] Create renderer entry (src/renderer/main.tsx)
- [ ] Set up React with Vite
- [ ] Create App.tsx with basic routing
- [ ] Implement window creation with saved bounds
- [ ] Add development mode DevTools

**Requirements:** Foundation  
**Verification:** npm run dev launches window

### A5: Layout Components
- [ ] Create Sidebar component (resizable, collapsible)
- [ ] Create MainContent area
- [ ] Create RightPanel component (resizable)
- [ ] Create TerminalDrawer component (resizable)
- [ ] Implement layout persistence per account
- [ ] Add keyboard shortcuts for layout

**Requirements:** R10.1  
**Verification:** Resize all panels, logout/login, verify restored

### A6: Authentication UI
- [ ] Create Supabase project and configure
- [ ] Implement AuthForm component (email/password)
- [ ] Add Google OAuth button with PKCE
- [ ] Add GitHub OAuth button with device flow
- [ ] Implement password reset flow
- [ ] Create AuthShell wrapper
- [ ] Handle authentication errors

**Requirements:** R1.1  
**Verification:** Register, login, reset password, OAuth flows

### A7: Local Database Setup
- [ ] Install better-sqlite3
- [ ] Create database schema (accounts, chats, messages, etc.)
- [ ] Implement migration system
- [ ] Create database service with transactions
- [ ] Implement per-account database isolation

**Requirements:** R1.3  
**Verification:** Multiple accounts create separate databases

### A8: Supabase Schema and RLS
- [ ] Create Supabase migrations
- [ ] Implement accounts table with RLS
- [ ] Implement chats table with RLS
- [ ] Implement messages table with RLS
- [ ] Implement projects table with RLS
- [ ] Implement memories table with RLS
- [ ] Test RLS policies with two accounts

**Requirements:** R1.3, R6.3  
**Verification:** Account A cannot access account B data

---

## Milestone B: Models and Chat

**Goal:** Model provider integration, local inference, streaming chat, execution modes.

### B1: Provider Adapter Interface
- [ ] Define ProviderAdapter TypeScript interface
- [ ] Define ModelCapabilities interface
- [ ] Define CompletionRequest/Response types
- [ ] Create adapter registry
- [ ] Implement adapter selection logic

**Requirements:** R2  
**Verification:** Type checking passes

### B2: Cloud Provider Adapters
- [ ] Implement OpenAI adapter (official SDK)
- [ ] Implement Anthropic adapter (official SDK)
- [ ] Implement Google Gemini adapter (official SDK)
- [ ] Implement Azure OpenAI adapter
- [ ] Implement xAI adapter (OpenAI-compatible)
- [ ] Implement OpenRouter adapter
- [ ] Implement Groq adapter
- [ ] Implement Mistral adapter
- [ ] Implement custom endpoint adapter

**Requirements:** R2.1  
**Verification:** Contract tests for each adapter

### B3: Local Model Infrastructure
- [ ] Build or download llama.cpp binaries
- [ ] Implement llama.cpp sidecar process manager
- [ ] Create authenticated loopback API
- [ ] Implement model download with progress
- [ ] Download Qwen 4B and 8B GGUF files
- [ ] Verify checksums and licenses
- [ ] Implement CPU inference
- [ ] Detect and use GPU acceleration with CPU fallback
- [ ] Implement idle unload (5 min timeout)

**Requirements:** R2.2  
**Verification:** Download model, send message, verify response

### B4: Ollama and LM Studio Support
- [ ] Implement Ollama adapter (OpenAI-compatible)
- [ ] Implement LM Studio adapter (OpenAI-compatible)
- [ ] Add manual model ID entry
- [ ] Test connection with local instances

**Requirements:** R2.3  
**Verification:** Connect to Ollama, send message

### B5: Model Configuration UI
- [ ] Create Connections page
- [ ] Create provider setup cards
- [ ] Implement credential input with visibility toggle
- [ ] Add connection testing
- [ ] Implement model discovery UI
- [ ] Add manual model ID input
- [ ] Show capability badges (tools, vision, reasoning)

**Requirements:** R2.1, R2.4  
**Verification:** Add OpenAI key, test connection, see models

### B6: Chat UI
- [ ] Create ChatView component
- [ ] Create MessageList with virtualization
- [ ] Create UserMessage component
- [ ] Create AssistantMessage component with Markdown
- [ ] Implement streaming text rendering
- [ ] Add code highlighting (Prism or Highlight.js)
- [ ] Add copy buttons for code blocks
- [ ] Implement edit-and-resend branching

**Requirements:** R3  
**Verification:** Send message, see streaming response

### B7: Composer
- [ ] Create Composer component
- [ ] Implement multiline textarea with Markdown
- [ ] Add ModelSelector dropdown
- [ ] Add ModeSelector (Chat/Plan/Agent/Goal)
- [ ] Add PermissionSelector
- [ ] Add AttachmentButton
- [ ] Implement drag-and-drop file attachment
- [ ] Add Send button with loading state

**Requirements:** R3  
**Verification:** Select model, mode, attach file, send

### B8: Execution Modes
- [ ] Implement Chat mode (no tool schemas)
- [ ] Implement Plan mode (read-only tools)
- [ ] Implement Agent mode (full tools)
- [ ] Implement Goal mode (persistent, budgets)
- [ ] Add mode-specific system instructions
- [ ] Create budget configuration UI for Goal mode

**Requirements:** R3.1, R3.2, R3.3, R3.4  
**Verification:** Capture requests, verify tool schemas per mode

### B9: Activity Stream
- [ ] Define event types (text, status, tool_call, approval, usage)
- [ ] Implement event emitter in agent context
- [ ] Create ActivitySummary component
- [ ] Create ToolCallEvent component
- [ ] Create ApprovalRequest component
- [ ] Add progress indicators
- [ ] Implement cancellation

**Requirements:** R3.3  
**Verification:** Run agent task, see tool calls and results

### B10: Streaming and Cancellation
- [ ] Implement streaming response handling
- [ ] Add cancellation tokens
- [ ] Create Stop button UI
- [ ] Handle provider cancellation APIs
- [ ] Clean up on cancellation

**Requirements:** R2.1  
**Verification:** Start long response, cancel mid-stream

---

## Milestone C: Tools and Integration

**Goal:** File operations, terminal, browser, desktop control, media generation.

### C1: Tool Infrastructure
- [ ] Define ToolDefinition interface
- [ ] Create tool registry
- [ ] Implement Zod input validation
- [ ] Create ExecutionContext
- [ ] Implement approval flow
- [ ] Add progress emission

**Requirements:** R5  
**Verification:** Tool validates input, executes, returns result

### C2: File Operation Tools
- [ ] Implement list_files with glob filters
- [ ] Implement read_file (text, PDF, DOCX, XLSX)
- [ ] Implement search_files with regex
- [ ] Implement write_file with pre-edit snapshot
- [ ] Implement edit_file with patch application
- [ ] Implement move_file
- [ ] Implement delete_file with confirmation
- [ ] Implement create_snapshot
- [ ] Implement restore_snapshot
- [ ] Add diff generation

**Requirements:** R5.1  
**Verification:** Execute each tool, verify results

### C3: Permission System
- [ ] Implement path canonicalization (symlinks, .., ~)
- [ ] Create Ask permission checker (prompts for all mutations)
- [ ] Create Project permission checker (scoped files)
- [ ] Create Full computer permission checker
- [ ] Implement approval UI with action details
- [ ] Store approved actions with target binding
- [ ] Add "Deny once" vs "Deny always" options

**Requirements:** R4.1, R4.2, R4.3  
**Verification:** Set each profile, verify enforcement

### C4: Terminal Integration
- [ ] Install node-pty and xterm.js
- [ ] Create PTY process manager
- [ ] Implement terminal tabs
- [ ] Create TerminalTab component with xterm.js
- [ ] Add PowerShell default on Windows
- [ ] Implement execute_command tool
- [ ] Add stdin support (send_stdin tool)
- [ ] Implement interrupt (Ctrl+C)
- [ ] Add resize handling
- [ ] Distinguish user vs agent terminals

**Requirements:** R5.2  
**Verification:** Open terminal, run command, send stdin, interrupt

### C5: Internal Browser
- [ ] Create BrowserPanel component
- [ ] Implement WebContentsView tabs
- [ ] Add address bar with navigation
- [ ] Implement back/forward/reload/stop
- [ ] Add history and bookmarks
- [ ] Implement downloads
- [ ] Add zoom and find
- [ ] Create cookie management
- [ ] Implement persistent profiles
- [ ] Add private browsing mode
- [ ] Enforce context isolation and web security

**Requirements:** R5.3  
**Verification:** Navigate, download file, private tab

### C6: Browser Automation
- [ ] Implement browser_inspect (DOM/accessibility tree)
- [ ] Implement browser_click (element or coordinates)
- [ ] Implement browser_type
- [ ] Implement browser_screenshot
- [ ] Add browser_cookies tool
- [ ] Add browser_history tool (back/forward)
- [ ] Test with real websites

**Requirements:** R5.3  
**Verification:** Automate form fill, screenshot, verify

### C7: External Browser Automation
- [ ] Launch Chromium with Krono profile
- [ ] Implement Chrome extension connection (optional)
- [ ] Add same automation tools for external browser
- [ ] Never attach to personal profile without consent

**Requirements:** R5.4  
**Verification:** Launch external browser, automate, verify separate profile

### C8: Desktop Control (Windows)
- [ ] Implement Windows UI Automation bindings
- [ ] Create desktop screenshot tool
- [ ] Implement mouse control (move, click, drag)
- [ ] Implement keyboard control (type, shortcuts)
- [ ] Add scroll tool
- [ ] Implement app/window discovery
- [ ] Add focus window tool
- [ ] Implement clipboard access
- [ ] Add active-control indicator overlay
- [ ] Create global emergency stop hotkey
- [ ] Implement pause during Games foreground

**Requirements:** R5.5  
**Verification:** Screenshot, click, type, emergency stop

### C9: Desktop Control (macOS) - Unverified
- [ ] Implement macOS Accessibility API bindings
- [ ] Port all desktop tools to macOS
- [ ] Test on macOS (if available)
- [ ] Mark as unverified if not tested

**Requirements:** R5.5  
**Verification:** Mark verified only if tested on macOS

### C10: Desktop Control (Linux) - Unverified
- [ ] Implement X11 automation bindings
- [ ] Detect Wayland and show unsupported message
- [ ] Port all desktop tools to Linux/X11
- [ ] Test on Linux (if available)
- [ ] Mark as unverified if not tested

**Requirements:** R5.5  
**Verification:** Mark verified only if tested on Linux

### C11: Whisper Dictation
- [ ] Build or download whisper.cpp
- [ ] Implement whisper sidecar
- [ ] Add model download (base, small, medium)
- [ ] Create MicButton component
- [ ] Implement audio recording
- [ ] Add local transcription
- [ ] Implement OpenAI Whisper API option
- [ ] Show recording/processing states

**Requirements:** Spec §5  
**Verification:** Record audio, see transcription

### C12: Media Generation
- [ ] Implement OpenAI image generation (DALL-E)
- [ ] Implement Google image generation (Imagen)
- [ ] Implement xAI video generation
- [ ] Add credential configuration
- [ ] Show provider and estimated cost
- [ ] Implement job status tracking
- [ ] Add preview and download
- [ ] Handle cancellation where supported

**Requirements:** R5.6  
**Verification:** Generate image with OpenAI key, verify preview

---

## Milestone D: Projects, Memory, Sync, Side Chats, Automations

**Goal:** Project management, memory system, synchronization, side chats, scheduled tasks.

### D1: Project Management
- [ ] Create Project model and schema
- [ ] Create ProjectList sidebar component
- [ ] Implement create project dialog
- [ ] Add icon/color picker
- [ ] Implement multiple directory mapping
- [ ] Create project instructions editor
- [ ] Add move chat to project action
- [ ] Implement project-scoped connections

**Requirements:** R6.1  
**Verification:** Create project, map directories, move chat

### D2: Memory System
- [ ] Create Memory model and schema
- [ ] Implement global memory editor
- [ ] Implement project memory editor
- [ ] Add automatic memory proposals during chat
- [ ] Create acceptance flow for proposals
- [ ] Enforce account and project boundaries
- [ ] Prevent auto-memory from side chats

**Requirements:** R6.2  
**Verification:** Edit memory, verify scoped, accept proposal

### D3: Synchronization Worker
- [ ] Implement outbox pattern in SQLite
- [ ] Create background sync worker
- [ ] Implement idempotent operations with stable IDs
- [ ] Add optimistic concurrency (version checking)
- [ ] Implement conflict detection
- [ ] Create conflict resolution UI
- [ ] Handle tombstones for deletions
- [ ] Add retry with exponential backoff
- [ ] Test two-client synchronization

**Requirements:** R6.3  
**Verification:** Two clients, create/edit/delete, verify sync and conflicts

### D4: Side Chat Implementation
- [ ] Create SideChat model and schema
- [ ] Implement side chat creation from message
- [ ] Add context snapshot at branch point
- [ ] Create side chat UI in right panel
- [ ] Show parent link and expiry timer
- [ ] Implement 24-hour expiry logic
- [ ] Add "Keep as independent chat" action
- [ ] Implement content transfer to parent
- [ ] Queue transfers during active parent execution
- [ ] Implement expiry cleanup (deny access, delete data)
- [ ] Preserve artifacts referenced elsewhere

**Requirements:** R7.1, R7.2, R7.3  
**Verification:** Create side chat, transfer message, let expire, verify cleanup

### D5: Automation System
- [ ] Create Automation model and schema
- [ ] Implement automation creation UI
- [ ] Add schedule configuration (one-time, recurring, cron)
- [ ] Implement timezone selection
- [ ] Add prompt, model, permission configuration
- [ ] Create device-owned runner with opt-in
- [ ] Implement execution lease acquisition
- [ ] Add missed run detection with catch-up logic
- [ ] Create automation history view
- [ ] Add pause, resume, delete actions
- [ ] Implement notification on completion

**Requirements:** R5.7  
**Verification:** Create recurring task, verify execution, miss one, verify catch-up

---

## Milestone E: Plugins and Connections

**Goal:** Plugin system, MCP support, compatibility adapters, working connections.

### E1: Plugin Manifest and Registry
- [ ] Define PluginManifest schema (Zod)
- [ ] Create plugin registry
- [ ] Implement plugin discovery in user/project directories
- [ ] Add enable/disable per plugin
- [ ] Create plugin logs viewer

**Requirements:** R9.1  
**Verification:** Create test plugin, verify detection

### E2: MCP Server Support
- [ ] Implement MCP stdio transport
- [ ] Implement MCP HTTP transport
- [ ] Add process lifecycle management
- [ ] Implement tool discovery from MCP servers
- [ ] Add OAuth flow where applicable
- [ ] Implement timeouts and cancellation
- [ ] Add process isolation

**Requirements:** R9.2  
**Verification:** Install MCP plugin, call tool, verify response

### E3: Plugin Import System
- [ ] Create plugin import UI
- [ ] Implement folder import
- [ ] Implement archive import (zip, tar.gz)
- [ ] Add Git URL import
- [ ] Implement archive traversal prevention
- [ ] Detect symlink escapes
- [ ] Preview components and permissions before install
- [ ] Show compatibility status (Supported/Needs config/Unsupported)
- [ ] Copy to Krono-managed storage
- [ ] Keep scripts/hooks disabled until approved

**Requirements:** R9.3, R9.4  
**Verification:** Import test archive, verify safety checks

### E4: Compatibility Adapters
- [ ] Implement Codex plugin adapter (.codex-plugin/plugin.json)
- [ ] Implement Claude Code adapter (.claude-plugin/plugin.json)
- [ ] Implement Antigravity adapter (plugin.json)
- [ ] Add standalone SKILL.md import
- [ ] Add standalone MCP configuration import
- [ ] Show component compatibility matrix

**Requirements:** R9.3  
**Verification:** Import Codex plugin, verify components

### E5: GitHub Connection
- [ ] Implement GitHub device authorization flow
- [ ] Add fine-grained token option
- [ ] Implement repository read tool
- [ ] Implement issue read tool
- [ ] Implement PR read tool
- [ ] Implement issue creation tool
- [ ] Use minimal scopes
- [ ] Add disconnect and revoke instructions

**Requirements:** R9.5  
**Verification:** Connect GitHub, read repo, create issue

### E6: Git Connection
- [ ] Implement git status tool
- [ ] Implement git diff tool
- [ ] Implement git log tool
- [ ] Implement git staging (add, reset)
- [ ] Implement git commit tool
- [ ] Implement git branch operations
- [ ] Test with local repository

**Requirements:** R9.5  
**Verification:** Run each git command, verify results

### E7: Google Drive Connection
- [ ] Implement Google OAuth (desktop, test users)
- [ ] Add Drive API scopes (minimal)
- [ ] Implement search tool
- [ ] Implement read file tool
- [ ] Implement upload file tool
- [ ] Handle public verification requirements

**Requirements:** R9.5  
**Verification:** Connect Drive, search, read, upload

### E8: Google Calendar Connection
- [ ] Use same OAuth as Drive
- [ ] Add Calendar API scopes
- [ ] Implement read events tool
- [ ] Implement create event tool
- [ ] Implement update event tool

**Requirements:** R9.5  
**Verification:** Connect Calendar, read events, create event

### E9: Discord Webhook Connection
- [ ] Create Discord connection UI
- [ ] Add webhook URL configuration
- [ ] Implement send message tool
- [ ] Respect permission system for writes

**Requirements:** R9.5  
**Verification:** Configure webhook, send message, verify in Discord

---

## Milestone F: Games

**Goal:** All 24 complete games with original assets, responsive gameplay.

### F1: Game Infrastructure
- [ ] Define GameModule interface
- [ ] Create game registry
- [ ] Implement game mount/unmount lifecycle
- [ ] Add pause/resume/restart
- [ ] Implement serialize/deserialize
- [ ] Create game settings storage
- [ ] Add score tracking

**Requirements:** R8.2  
**Verification:** Test lifecycle with simple game

### F2: Games Catalog Page
- [ ] Create GamesView component
- [ ] Implement game grid (4/3/2/1 columns responsive)
- [ ] Add search input
- [ ] Add category filters (All/Puzzle/Arcade/Strategy)
- [ ] Add favorites filter
- [ ] Create game card components with thumbnails
- [ ] Show factual descriptions

**Requirements:** R8.1  
**Verification:** Resize window, verify grid adapts

### F3: Game Player Shell
- [ ] Create GamePlayer component
- [ ] Add canvas rendering container
- [ ] Implement game controls area
- [ ] Create TaskStrip (shows active chat/approvals during gameplay)
- [ ] Add Return to chat button
- [ ] Implement pause on blur
- [ ] Release keyboard capture on pause

**Requirements:** R8.4  
**Verification:** Play game while agent runs, verify task strip

### F4-F27: Individual Games (24 games)

Each game task includes:
- [ ] Design original artwork/sprites
- [ ] Implement game rules
- [ ] Add appropriate controls (mouse/keyboard)
- [ ] Implement win/loss logic
- [ ] Add pause/resume/restart
- [ ] Implement state serialization
- [ ] Add settings (difficulty, sound, etc.)
- [ ] Create instructions
- [ ] Add sound effects (original or licensed)
- [ ] Test thoroughly

**F4: Minesweeper**
- [ ] Implement grid with mines
- [ ] Ensure first click always safe
- [ ] Add flag mechanism
- [ ] Implement flood fill for zeros
- [ ] Win/loss detection

**F5: Snake**
- [ ] Implement grid movement
- [ ] Add food spawning
- [ ] Growth logic
- [ ] Speed progression
- [ ] Collision detection

**F6: Tic-Tac-Toe**
- [ ] Implement 3×3 grid
- [ ] Add minimax bot (perfect play)
- [ ] Add difficulty options (easy with errors)
- [ ] Win/draw detection

**F7: Maze Chase**
- [ ] Design original maze layout
- [ ] Create original character sprites
- [ ] Implement pellet collection
- [ ] Add 4 ghosts with distinct AI (chase, ambush, patrol, random)
- [ ] Implement power-ups and vulnerability
- [ ] Add scatter mode

**F8: Pong**
- [ ] Implement paddle physics
- [ ] Create predictive bot with limited speed
- [ ] Add ball physics with angle variation
- [ ] Score to 11 wins
- [ ] Speed progression

**F9: Block Fit**
- [ ] Implement 8×8 board
- [ ] Generate random polyomino pieces (trays of 3)
- [ ] Add drag placement (no rotation)
- [ ] Implement row/column clearing
- [ ] Loss detection (no fit)

**F10: Falling Blocks**
- [ ] Implement 10×20 board
- [ ] Add 7 tetrominoes
- [ ] Implement SRS rotation
- [ ] Add hold and queue (next 3)
- [ ] Soft and hard drop
- [ ] Line clearing
- [ ] Speed progression

**F11: Sand Blocks**
- [ ] Implement colored tetrominoes
- [ ] Convert to grains on landing
- [ ] Physics simulation in Web Worker
- [ ] Clear connected horizontal spans
- [ ] Loss detection

**F12: Flap**
- [ ] Design original bird/character
- [ ] Implement gravity and flap physics
- [ ] Add obstacle generation
- [ ] Precise collision detection
- [ ] Score tracking

**F13: Darts**
- [ ] Create dartboard rendering
- [ ] Implement aiming with mouse
- [ ] Add timed accuracy mechanic
- [ ] Implement 301 game rules
- [ ] Bust and exact-zero logic

**F14: Jigsaw**
- [ ] Create original image sets (3 themes)
- [ ] Generate interlocking pieces (3×3, 4×4, 5×5)
- [ ] Implement drag and snap
- [ ] Optional local image load
- [ ] Win detection

**F15: Stack**
- [ ] Implement moving blocks
- [ ] Add overhang trimming
- [ ] Perfect placement bonus (3 consecutive → +8% width)
- [ ] Loss detection (too small/miss)
- [ ] Height score

**F16: Orbit**
- [ ] Implement rotating pointer
- [ ] Add target arc and bullseye
- [ ] Click timing
- [ ] Dynamic difficulty (speed increase)
- [ ] Score: 1 for target, 3 for bullseye

**F17: Color Gauntlet**
- [ ] Show 5 colors sequentially
- [ ] Implement HSL sliders
- [ ] Calculate perceptual distance (CIEDE2000 or Delta E)
- [ ] Show comparison after each
- [ ] Total score (lower better)

**F18: Odd Color**
- [ ] Generate grid with one different tile
- [ ] Increase N and reduce difference on success
- [ ] Accessible mode (larger difference)
- [ ] Loss on wrong click

**F19: 2048**
- [ ] Implement 4×4 grid
- [ ] Add swipe/arrow controls
- [ ] Merge logic (powers of 2)
- [ ] Win at 2048 (continue option)
- [ ] Loss detection (no moves)

**F20: Breakout**
- [ ] Implement paddle and ball physics
- [ ] Create original brick layouts (multiple levels)
- [ ] Add power-ups
- [ ] Lives system
- [ ] Win/loss detection

**F21: Memory Match**
- [ ] Create original card art
- [ ] Implement grid (4×4, 6×6, 8×8)
- [ ] Flip and match logic
- [ ] Moves and time tracking

**F22: Sudoku**
- [ ] Create validated puzzle bank (unique solutions)
- [ ] Implement 9×9 grid with regions
- [ ] Add notes (pencil marks)
- [ ] Conflict highlighting
- [ ] Win detection (valid complete)

**F23: Solitaire**
- [ ] Implement Klondike rules
- [ ] Draw one card
- [ ] Tableau and foundations
- [ ] Undo system
- [ ] Win detection

**F24: Connect Four**
- [ ] Implement 7×6 grid
- [ ] Add minimax bot (alpha-beta, depth 6)
- [ ] Evaluate center, threats, connections
- [ ] Win detection (4 in line)
- [ ] Draw detection

**F25: Reversi**
- [ ] Implement 8×8 grid
- [ ] Add alpha-beta bot (depth 5-7)
- [ ] Evaluate corners, mobility, position
- [ ] Flip logic
- [ ] Pass and end game logic

**F26: Simon**
- [ ] Implement audiovisual sequence
- [ ] Add sound generation
- [ ] Visual-only mode (reduced motion)
- [ ] Sequence playback and input
- [ ] Loss detection

**F27: Lights Out**
- [ ] Implement 5×5 grid
- [ ] Toggle cell + neighbors logic
- [ ] Generate solvable boards (reverse from solved)
- [ ] Win detection (all off)
- [ ] Optimal move tracking

**Requirements:** R8.1, R8.2, R8.3  
**Verification:** Each game playable, pause works, serialize/restore works

### F28: Game Integration Testing
- [ ] Test all 24 games launch
- [ ] Verify pause/resume for each
- [ ] Test serialization for each
- [ ] Play each game during agent execution
- [ ] Verify TaskStrip shows status
- [ ] Verify no focus theft on completion
- [ ] Test emergency stop during gameplay
- [ ] Verify desktop control paused during games

**Requirements:** R8.4  
**Verification:** Manual playthrough with concurrent agent tasks

### F29: Game Assets
- [ ] Create all original artwork
- [ ] Create all sound effects
- [ ] Verify no copied assets
- [ ] Add attribution for licensed assets (if any)
- [ ] Optimize asset sizes

**Requirements:** R8.1  
**Verification:** Visual inspection, file size check

---

## Milestone G: Packaging, Testing, Documentation

**Goal:** Release builds, comprehensive testing, documentation site, publication.

### G1: Unit Tests
- [ ] Write provider adapter contract tests
- [ ] Write permission enforcement tests
- [ ] Write tool input validation tests
- [ ] Write game logic tests (rules, win/loss)
- [ ] Write sync logic tests (conflict detection)
- [ ] Write bot legality tests
- [ ] Achieve >80% coverage for critical modules

**Requirements:** R12.1  
**Verification:** npm test passes, coverage report

### G2: Integration Tests
- [ ] Set up Playwright Electron
- [ ] Write authentication flow tests
- [ ] Write model request/response tests
- [ ] Write tool execution tests
- [ ] Write permission approval tests
- [ ] Write game lifecycle tests
- [ ] Run tests in CI

**Requirements:** R12.2  
**Verification:** npm run test:e2e passes in CI

### G3: Windows Packaging
- [ ] Configure electron-builder for Windows
- [ ] Create NSIS installer
- [ ] Create portable ZIP build
- [ ] Add Start Menu and Desktop shortcuts
- [ ] Test installer on clean Windows 11 VM
- [ ] Verify uninstaller cleanup
- [ ] Generate checksums

**Requirements:** R11.1  
**Verification:** Independent test on clean Windows 11

### G4: macOS Packaging (Unverified)
- [ ] Configure electron-builder for macOS
- [ ] Create DMG build
- [ ] Create .app bundle
- [ ] Sign if certificate available
- [ ] Mark as unverified if not tested on macOS

**Requirements:** R11.2  
**Verification:** CI build succeeds; mark unverified if not tested

### G5: Linux Packaging (Unverified)
- [ ] Configure electron-builder for Linux
- [ ] Create AppImage build
- [ ] Mark as unverified if not tested on Linux

**Requirements:** R11.3  
**Verification:** CI build succeeds; mark unverified if not tested

### G6: GitHub Actions CI
- [ ] Create ci.yml workflow (lint, type check, test)
- [ ] Create release.yml workflow (build, package, upload)
- [ ] Add matrix builds (Windows, macOS, Linux)
- [ ] Trigger on tag push (v*)
- [ ] Upload artifacts to GitHub Releases
- [ ] Generate release notes

**Requirements:** R11.4  
**Verification:** Push tag, verify release created with artifacts

### G7: Manual Verification Checklist
- [ ] Create comprehensive checklist covering all requirements
- [ ] Execute on clean Windows 11 install
- [ ] Document results with screenshots
- [ ] Test authentication flows
- [ ] Test two-account isolation
- [ ] Test two-client synchronization
- [ ] Test all provider connections
- [ ] Test Chat mode sends no tools
- [ ] Test Plan mode rejects mutations
- [ ] Test permission enforcement
- [ ] Test all file operations
- [ ] Test terminal I/O
- [ ] Test browser tabs and automation
- [ ] Test desktop control with emergency stop
- [ ] Test media generation (if credentials)
- [ ] Test side chat expiry
- [ ] Test automation execution
- [ ] Test plugin import
- [ ] Test all connection actions
- [ ] Play all 24 games
- [ ] Test game serialization
- [ ] Test UI at multiple resolutions and DPI
- [ ] Test keyboard navigation
- [ ] Test reduced motion
- [ ] Test all themes

**Requirements:** R12.3  
**Verification:** Completed checklist with evidence

### G8: Accessibility Testing
- [ ] Test keyboard navigation (entire app)
- [ ] Verify focus indicators visible
- [ ] Test with NVDA (Windows)
- [ ] Test with VoiceOver (macOS, if available)
- [ ] Verify WCAG AA contrast in all themes
- [ ] Test reduced motion preference

**Requirements:** R10.5  
**Verification:** Keyboard-only navigation successful, screen reader usable

### G9: Performance Testing
- [ ] Measure cold launch time
- [ ] Measure warm launch time
- [ ] Measure model TTFB (local and cloud)
- [ ] Measure terminal keystroke latency
- [ ] Measure game frame rates (all 24)
- [ ] Measure file diff generation time
- [ ] Document results
- [ ] Optimize if below targets

**Requirements:** Design doc performance targets  
**Verification:** All metrics meet or exceed targets

### G10: Documentation
- [ ] Write comprehensive README.md
- [ ] Create setup.md (installation, configuration)
- [ ] Create connections.md (provider and plugin setup)
- [ ] Create games.md (game catalog with rules)
- [ ] Create architecture.md (technical overview)
- [ ] Add screenshots to docs
- [ ] Include troubleshooting section
- [ ] Add contribution guidelines (if open source)

**Requirements:** Spec §13  
**Verification:** Documentation complete and accurate

### G11: Documentation Site
- [ ] Create index.html (landing page)
- [ ] Create setup.html (installation guide)
- [ ] Create games.html (game catalog)
- [ ] Create download.html (links to releases)
- [ ] Add CSS styling (consistent with app)
- [ ] Deploy to GitHub Pages
- [ ] Verify live URL works
- [ ] Test all links

**Requirements:** R11.5  
**Verification:** Site live and functional

### G12: Third-Party Notices
- [ ] Collect all dependency licenses
- [ ] Add model licenses (Qwen)
- [ ] Add llama.cpp license
- [ ] Add whisper.cpp license
- [ ] Create NOTICES.md or THIRD_PARTY.txt
- [ ] Include in packaged builds

**Requirements:** Spec §13  
**Verification:** All licenses documented

### G13: Release Preparation
- [ ] Update CHANGELOG.md
- [ ] Bump version in package.json
- [ ] Review all requirements against implementation
- [ ] Identify any unverified features
- [ ] Document blockers
- [ ] Create version tag
- [ ] Push tag to trigger release

**Requirements:** R11.4  
**Verification:** Tag pushed, CI creates release

### G14: Publication
- [ ] Verify GitHub Release published
- [ ] Download and test each artifact independently
- [ ] Update documentation site with download links
- [ ] Test live download links
- [ ] Verify checksums
- [ ] Prepare Kiroween submission (if eligible)
  - [ ] Check current official rules
  - [ ] Keep genuine .kiro artifacts
  - [ ] Document Kiro Crew usage
  - [ ] Disclose reused code
  - [ ] Prepare English submission text
  - [ ] Write test instructions
  - [ ] Create demo script (<3 minutes, real Windows behavior)
  - [ ] Do not fabricate recordings
  - [ ] Do not auto-submit

**Requirements:** Spec §13  
**Verification:** Release downloadable, site updated

---

## Progress Tracking

**Current Status:** Milestone A in progress (A1-A3 partial)

**Completion Criteria:**
- All tasks checked off
- All requirements verified with evidence
- Manual verification checklist completed
- Release artifacts tested independently
- Documentation site live

**Blocking Issues:** (none yet)

**Deferred Items:** (none - all features in scope for first complete release)

**Notes:**
- Implementation may proceed in parallel across milestones where dependencies allow
- Continuous integration and testing throughout
- .kiro documents updated as decisions are made
- This task list is living documentation

---

## Quick Reference: Requirement to Task Mapping

- R1.1 → A6
- R1.2 → A6, A7
- R1.3 → A7, A8
- R1.4 → (in account management UI, no specific task yet)
- R2.1 → B2, B5
- R2.2 → B3
- R2.3 → B4
- R2.4 → B5
- R3.1-R3.4 → B8
- R4.1-R4.4 → C3
- R5.1 → C2
- R5.2 → C4
- R5.3 → C5, C6
- R5.4 → C7
- R5.5 → C8, C9, C10
- R5.6 → C12
- R5.7 → D5
- R6.1 → D1
- R6.2 → D2
- R6.3 → D3
- R7.1-R7.3 → D4
- R8.1 → F2, F4-F27
- R8.2 → F1, F4-F27
- R8.3 → F4-F27
- R8.4 → F3, F28
- R9.1 → E1
- R9.2 → E2
- R9.3 → E3, E4
- R9.4 → E3
- R9.5 → E5-E9
- R10.1 → A5
- R10.2 → A2
- R10.3 → (implemented throughout)
- R10.4 → A3
- R10.5 → G8
- R10.6 → (tested in G7)
- R11.1 → G3
- R11.2 → G4
- R11.3 → G5
- R11.4 → G6
- R11.5 → G11
- R12.1 → G1
- R12.2 → G2
- R12.3 → G7
