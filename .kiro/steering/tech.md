# Krono Agent - Technical Specification

## Stack

### Core Platform
- **Electron** with TypeScript
- **React** with **Vite** for UI
- **Tailwind CSS** for styling
- **Radix UI** primitives for accessible components

### State Management
- **Zustand** for transient UI state
- **TanStack Query** for server state
- **SQLite** with migrations for local persistence and sync outbox
- **Supabase** (Auth, PostgreSQL, private Storage, RLS) for cloud data

### Specialized Libraries
- **xterm.js** + **node-pty** for terminal
- **Monaco** for code editing and diff review
- **Canvas 2D** + **Web Workers** for games requiring isolation
- **Zod** for IPC and tool input validation

### Testing
- **Vitest** for focused logic tests
- **Playwright** Electron tests for desktop flows

### Packaging
- **electron-builder** for installers
- **GitHub Releases** for distribution

### Native Inference
- **llama.cpp** sidecar for local models
- **whisper.cpp** for dictation

## Architecture Constraints

### Process Separation

**Main (Trusted):**
- IPC validation
- Permission enforcement
- Credential storage (OS keychain)
- File operations
- Process lifecycle

**Renderer (Application):**
- UI components
- No direct filesystem or credential access
- Validated IPC only

**Agent Execution:**
- Separate context for tool execution
- Budget tracking
- Event streaming

**Browser Web Contents:**
- Sandboxed WebContentsView
- Context isolation enabled
- No Node integration for remote content
- Web security enforced

**Plugin/MCP Processes:**
- stdio or HTTP isolation
- Timeout and cancellation
- Process-level separation

**Inference Processes:**
- llama.cpp sidecar
- whisper.cpp sidecar
- Authenticated loopback or private IPC
- Unload when idle

**Games:**
- Canvas 2D rendering
- Web Workers for physics/simulation
- No blocking main thread

### Security Boundaries

1. **Secrets never enter:**
   - Model context
   - Renderer processes
   - Logs
   - Terminal output
   - Repository commits
   - Exports

2. **RLS enforcement:**
   - Every account-owned table
   - Private storage buckets
   - No service-role key in desktop app

3. **Permission system:**
   - Tool calls checked against profile
   - Plugins cannot escalate
   - Path canonicalization
   - Approval binding to concrete actions

4. **Browser isolation:**
   - WebContentsView for internal tabs
   - Separate profile for automation
   - Explicit popup/permission prompts
   - No IPC access from websites

5. **Credential management:**
   - OS credential store
   - Opaque references
   - Connection testing without exposure
   - Revoke instructions

### Data Flow

**Chat Message → Execution:**
```
User input (renderer)
  → IPC with validation (main)
  → Permission check (main)
  → Model request (agent context)
  → Tool schema injection if mode allows
  → Streaming response
  → Tool calls validated
  → Permission approval if needed
  → Tool execution (isolated)
  → Result → model
  → Completion → event stream
  → UI update (renderer)
```

**Synchronization:**
```
Local change
  → SQLite with tx
  → Outbox entry
  → Optimistic UI update
  → Background sync worker
  → Supabase with RLS check
  → Conflict detection
  → Resolution or user prompt
  → Tombstone for deletions
```

**Game State:**
```
User input → game module
  → Canvas rendering
  → State update (worker if heavy)
  → Score/progress → local DB
  → Settings → sync outbox
  → Serialize on pause
  → Restore on resume
```

## Verification Rules

### Build-Time
- TypeScript strict mode
- Zod schemas for all IPC messages
- ESLint rules for security patterns
- Vitest coverage for critical paths

### Runtime Verification Requirements

**Authentication:**
- ✓ Clean install → login → offline access → logout → lock
- ✓ Password reset flow
- ✓ Google OAuth with PKCE
- ✓ GitHub OAuth with device flow
- ✓ Two-account isolation (separate local databases)
- ✓ Two-client synchronization (same account, conflict resolution)

**Models:**
- ✓ Local model download, inference, cancellation, CPU fallback
- ✓ Provider contract tests (each adapter)
- ✓ Real smoke test for supplied credentials
- ✓ Chat mode sends zero tool schemas (request-level evidence)
- ✓ Plan mode rejects mutations (deny file write, command with side effects)

**Permissions:**
- ✓ Ask profile prompts for all writes/commands/desktop
- ✓ Project profile allows scoped files, still prompts for commands
- ✓ Full computer shows explicit activation
- ✓ Path canonicalization handles symlinks/junctions
- ✓ Archive traversal rejection
- ✓ Changed actions need new approval

**Tools:**
- ✓ Real file edit, diff generation, snapshot restore
- ✓ Terminal stdin, streaming output, exit status, Ctrl+C
- ✓ Internal browser tabs, cookies, navigation, downloads
- ✓ External browser connection with profile
- ✓ Windows desktop: screenshot, focus, mouse, keyboard, emergency stop
- ✓ Desktop control pauses during gameplay
- ✓ Media generation (where credentials available)

**Side Chats:**
- ✓ Independent context with parent link
- ✓ Transfer message to parent
- ✓ 24-hour expiry after last activity
- ✓ Promotion to independent chat
- ✓ Cleanup of expired data

**Automations:**
- ✓ Scheduled execution on designated device
- ✓ Missed-run handling (at most one catch-up)
- ✓ Duplicate prevention with leases
- ✓ Permission enforcement (no escalation)

**Plugins:**
- ✓ Import fixtures with partial compatibility
- ✓ Malformed manifest handling
- ✓ Hook remain disabled until reviewed
- ✓ Process isolation for MCP

**Connections:**
- ✓ GitHub repo read, issue creation
- ✓ Git operations with real diffs
- ✓ Google Drive/Calendar actions when authorized

**Games:**
- ✓ All 24 games: controls, rules, win/loss, pause, restart, serialize
- ✓ Minesweeper first-click safety
- ✓ Bot legality (Connect Four, Reversi, Tic-Tac-Toe)
- ✓ Sudoku unique solutions
- ✓ Lights Out solvable generation
- ✓ Stack restoration bonus geometry
- ✓ Color Gauntlet perceptual distance method
- ✓ Simon reduced-motion alternative

**UI:**
- ✓ 1024×768, 1280×800, 1920×1080 layouts
- ✓ 100% and 150% DPI scaling
- ✓ All themes render correctly
- ✓ Keyboard navigation complete
- ✓ Larger fonts do not break layouts
- ✓ Reduced motion respected

**Release:**
- ✓ Windows installer tested independently
- ✓ Portable build tested independently
- ✓ CI builds for macOS/Linux (mark unverified runtime if not tested)

### Never Accept Without Evidence

- Build success ≠ runtime behavior
- Tests passing ≠ actual provider working
- UI rendering ≠ accessibility compliance
- Development mode ≠ production package behavior
- Mocked integration ≠ real connection

## Technology Decisions

### Why Electron?
- Cross-platform desktop target
- Terminal, filesystem, credential store access
- Process isolation model
- Established packaging and update mechanisms

### Why Zustand + TanStack Query?
- Minimal boilerplate
- Clear separation: UI state vs. server state
- React 19 compatibility

### Why SQLite + Supabase?
- Local-first with offline capability
- Structured sync with RLS
- No MongoDB/cloud-only dependency
- Migrations and schema validation

### Why llama.cpp?
- Embedded inference without Ollama installation
- Active maintenance
- GGUF compatibility
- CPU and GPU acceleration

### Why Canvas 2D (not WebGL)?
- Simpler for 2D games
- Better compatibility
- Adequate performance for these game types
- Easier deterministic testing

### Why Not...?

**Tauri:** Less mature for complex IPC patterns, credential management.

**PWA:** No desktop automation, no native terminal, no credential store.

**Custom backend:** Unnecessary complexity; Supabase provides auth, RLS, storage.

**Unity/Godot for games:** Embedding complexity, bundle size, process overhead.

**WebGL for 2D games:** Overkill, compatibility issues, harder to test.

## Platform-Specific Considerations

### Windows
- PowerShell default shell
- UI Automation for desktop control
- Windows credential store
- electron-builder NSIS installer

### macOS
- zsh default shell
- Accessibility API for desktop control
- Keychain for credentials
- electron-builder DMG + app
- Notarization requirements
- Unsigned Chromium security prompts (document)

### Linux
- bash default shell
- X11 desktop automation (Wayland unsupported detection)
- libsecret for credentials
- AppImage or deb/rpm
- Unverified until tested

## Dependencies

Pin all dependencies with exact versions.
Lock native runtime versions.
Document required Node.js version.
Test dependency updates before merging.

Use official model repositories:
- Qwen/Qwen3-4B-GGUF (Q4_K_M)
- Qwen/Qwen3-8B-GGUF (Q4_K_M)

Verify download checksums.
Include license and attribution files.

## Performance Targets

- Application launch: < 3s cold, < 1s warm
- Model response first token: < 500ms local, < 2s cloud
- Terminal keystroke latency: < 50ms
- Game frame rate: 60 FPS stable
- UI interaction: < 100ms feedback
- File diff generation: < 200ms for typical files
- Browser tab switch: < 100ms

Measure on representative hardware:
- CPU: Intel i5-8th gen or equivalent
- RAM: 8 GB
- Storage: SSD

## Logging and Observability

- Structured logs with levels
- Separate files: main, renderer, agent, games, plugins
- No secrets in logs
- Rotation and size limits
- User-accessible log directory
- Diagnostic export for support

## Build and CI

- GitHub Actions for Windows/macOS/Linux
- Matrix builds with Node 22
- Linting, type checking, Vitest
- Playwright tests on Windows
- Artifact upload to GitHub Releases
- Version from package.json + git tag
