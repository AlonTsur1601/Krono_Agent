# Krono Agent - Requirements

## R1: Authentication and Account Management

### R1.1: Multi-method Authentication
**Priority:** Critical  
**Description:** Support email/password, Google OAuth, and GitHub OAuth authentication.

**Acceptance Criteria:**
- ✓ Email/password registration and login with validation
- ✓ Password reset flow via email with secure token
- ✓ Google OAuth using system browser, PKCE, and validated callback
- ✓ GitHub OAuth using device flow or system browser with PKCE
- ✓ Authentication state persists across restarts
- ✓ Failed authentication shows clear error messages

**Verification:**
- Create account with email/password
- Reset password and verify receipt
- Login with Google account
- Login with GitHub account
- Close and reopen application, verify still authenticated

### R1.2: Offline Access
**Priority:** Critical  
**Description:** Support offline access to cached data after initial authentication.

**Acceptance Criteria:**
- ✓ After authentication, application works without network for local tasks
- ✓ Games remain playable offline
- ✓ Local models run without network
- ✓ Synchronization resumes automatically after reconnection
- ✓ Offline state clearly indicated in UI

**Verification:**
- Authenticate, disconnect network
- Start local chat with local model
- Play games
- Create local files
- Reconnect network and verify sync

### R1.3: Multi-Account Isolation
**Priority:** High  
**Description:** Support multiple accounts with complete data isolation.

**Acceptance Criteria:**
- ✓ Each account has separate local database
- ✓ Logout locks account data
- ✓ Switching accounts does not leak data
- ✓ Two accounts on same machine do not interfere

**Verification:**
- Login with account A, create chat
- Logout, login with account B
- Verify account B sees no account A data
- Repeat in reverse

### R1.4: Account Data Management
**Priority:** High  
**Description:** Provide account export and deletion.

**Acceptance Criteria:**
- ✓ Export downloads complete account data as structured format
- ✓ Delete account removes all cloud data and local references
- ✓ Deletion requires confirmation with typed account name
- ✓ Post-deletion cannot authenticate with deleted credentials

**Verification:**
- Export account data and verify content
- Delete account with confirmation
- Attempt login with deleted credentials (should fail)

---

## R2: Model Integration

### R2.1: Cloud Provider Adapters
**Priority:** Critical  
**Description:** Support OpenAI, Anthropic, Google, Azure, xAI, OpenRouter, Groq, Mistral, and custom OpenAI-compatible endpoints.

**Acceptance Criteria:**
- ✓ Each provider has setup UI with credential entry
- ✓ Connection testing before saving
- ✓ Model discovery or manual model ID entry
- ✓ Streaming responses with cancellation
- ✓ Clear authentication and quota errors
- ✓ Provider-specific capability metadata (tools, images, reasoning)

**Verification:**
- For each provider with test credentials:
  - Add credentials and test connection
  - Send message and receive streaming response
  - Cancel mid-stream
  - Verify error handling with invalid key

### R2.2: Local Models
**Priority:** Critical  
**Description:** Built-in Qwen 4B and 8B models via llama.cpp, no external installation required.

**Acceptance Criteria:**
- ✓ Onboarding offers local model download
- ✓ Shows model size, disk requirements, hardware guidance
- ✓ Download progress with pause/resume/cancel
- ✓ CPU inference works
- ✓ GPU acceleration when available with CPU fallback
- ✓ Idle inference process unloads
- ✓ Model removal supported

**Verification:**
- Fresh install, select download Qwen 4B
- Monitor download, pause, resume, complete
- Send message and receive response
- Monitor GPU/CPU usage
- Wait for idle unload (5 minutes)
- Remove model and verify deletion

### R2.3: Custom Endpoints
**Priority:** Medium  
**Description:** Support Ollama, LM Studio, and custom OpenAI-compatible endpoints.

**Acceptance Criteria:**
- ✓ URL input with connection testing
- ✓ Manual model ID entry when discovery fails
- ✓ Same streaming and cancellation as cloud providers

**Verification:**
- Connect to local Ollama instance
- Enter custom model ID
- Send message and verify response

### R2.4: Capability Metadata
**Priority:** High  
**Description:** Track and enforce model capabilities to avoid sending unsupported parameters.

**Acceptance Criteria:**
- ✓ Metadata includes: tools, images, reasoning, structured output, context limits, streaming
- ✓ UI only shows controls for supported capabilities
- ✓ Tool schemas not sent in Chat mode even if model supports them
- ✓ Vision controls hidden for text-only models
- ✓ Manual capability override for unknown models

**Verification:**
- Connect model without tool support
- Verify Agent mode disabled or shows limitation
- Connect vision model
- Verify image attachment enabled
- Switch to text-only model
- Verify image controls hidden

---

## R3: Execution Modes

### R3.1: Chat Mode
**Priority:** Critical  
**Description:** Conversational mode with no tool execution.

**Acceptance Criteria:**
- ✓ No tool schemas sent to model
- ✓ System instruction explains limitation
- ✓ Attachments and explicit context work
- ✓ Model cannot attempt tools

**Verification:**
- Capture request to model provider
- Verify zero tool definitions in schema
- Ask model to perform file operation
- Verify model explains it cannot

### R3.2: Plan Mode
**Priority:** High  
**Description:** Read-only investigation and planning mode.

**Acceptance Criteria:**
- ✓ Only read-only tools exposed
- ✓ File writes rejected
- ✓ Commands rejected or sandboxed
- ✓ Computer control writes rejected
- ✓ Clear indication of mode in UI

**Verification:**
- Activate Plan mode
- Request file read (allowed)
- Request file write (denied with explanation)
- Request command execution (denied or read-only commands allowed)

### R3.3: Agent Mode
**Priority:** Critical  
**Description:** Full execution using selected permission profile.

**Acceptance Criteria:**
- ✓ All tools available within permission boundary
- ✓ Streaming activity shows tool calls and results
- ✓ Cancellation stops execution
- ✓ File changes shown with diffs
- ✓ Approvals appear inline when required

**Verification:**
- Run agent task with Ask permissions
- Verify approval prompts for writes
- Run with Project permissions
- Verify scoped file operations allowed
- Run with Full computer permissions
- Verify desktop controls available

### R3.4: Goal Mode
**Priority:** High  
**Description:** Persistent multi-step work with checkpoints and budgets.

**Acceptance Criteria:**
- ✓ Set time budget (default 30 min) and call budget (default 100)
- ✓ Pause and resume with preserved state
- ✓ Progress tracking with checkpoints
- ✓ Stop after three attempts at same failing step
- ✓ Never silently exceed budget
- ✓ Cancellation at any point

**Verification:**
- Start Goal with 5-minute budget
- Pause after 2 minutes
- Verify state persists through restart
- Resume and complete or hit budget
- Start Goal with 10-call budget
- Verify stops at limit

---

## R4: Permission System

### R4.1: Permission Profiles
**Priority:** Critical  
**Description:** Three profiles: Ask, Project, Full computer.

**Acceptance Criteria:**
- ✓ Ask: prompts for all writes, commands, desktop control
- ✓ Project: allows scoped file operations in mapped directories
- ✓ Full computer: explicit activation with clear indication
- ✓ Effective permissions displayed before execution
- ✓ Profile selection persists per chat/project

**Verification:**
- Set Ask, verify prompts
- Set Project, verify scoped operations
- Set Full computer, verify activation confirmation

### R4.2: Approval Binding
**Priority:** Critical  
**Description:** Approvals bind to concrete actions; changes require new approval.

**Acceptance Criteria:**
- ✓ Approval shows exact file path, command, or action
- ✓ Changing target path invalidates approval
- ✓ Approval history visible
- ✓ Deny once vs. deny always options

**Verification:**
- Request file write to path A
- Approve
- Agent attempts write to path B
- Verify new approval required

### R4.3: Path Canonicalization
**Priority:** High  
**Description:** Paths canonicalized to handle symlinks and junctions.

**Acceptance Criteria:**
- ✓ Symlinks resolved before permission check
- ✓ Junctions resolved on Windows
- ✓ Attempts to escape project boundary via symlink denied

**Verification:**
- Create symlink outside project pointing inside
- Set Project permissions
- Attempt write via symlink path
- Verify allowed if target is within project

### R4.4: Plugin Permissions
**Priority:** High  
**Description:** Plugins cannot escalate permissions.

**Acceptance Criteria:**
- ✓ Plugin tools subject to same permission checks
- ✓ Plugin manifest declares required permissions
- ✓ User approves permission requests during installation

**Verification:**
- Install plugin requiring filesystem access
- Verify permission request during install
- Run plugin tool with Ask permissions
- Verify still prompts for writes

---

## R5: Tools

### R5.1: File Operations
**Priority:** Critical  
**Description:** List, read, search, create, edit, move, delete files.

**Acceptance Criteria:**
- ✓ Recursive directory listing with filters
- ✓ Read text, PDF, basic DOCX/XLSX
- ✓ Regex search with results and context
- ✓ Create files and directories
- ✓ Patch-based edits with pre-edit snapshots
- ✓ Move and rename
- ✓ Delete with confirmation
- ✓ Diff generation
- ✓ Snapshot restoration

**Verification:**
- List project files recursively
- Read text file, PDF, DOCX
- Search for pattern with grep
- Create new file
- Edit file with patch
- Generate diff
- Restore from snapshot
- Move file
- Delete with confirmation

### R5.2: Terminal
**Priority:** Critical  
**Description:** Real PTY with tabs, streaming I/O, interruption.

**Acceptance Criteria:**
- ✓ Multiple tabs
- ✓ PowerShell default on Windows
- ✓ Streaming stdout/stderr
- ✓ stdin support
- ✓ Ctrl+C interrupt
- ✓ Exit status
- ✓ Working directory
- ✓ Resize handling
- ✓ Distinguish user terminals from agent sessions

**Verification:**
- Open terminal tab
- Run long command
- Ctrl+C to interrupt
- Provide stdin
- Run command with non-zero exit
- Verify exit status shown
- Resize window and verify terminal adapts
- Agent runs command in separate session
- Verify user tab unaffected

### R5.3: Browser
**Priority:** Critical  
**Description:** Internal sandboxed browser with tabs and automation.

**Acceptance Criteria:**
- ✓ Multiple tabs
- ✓ Address bar with navigation and search
- ✓ Back/forward/reload/stop
- ✓ History and bookmarks
- ✓ Downloads
- ✓ Zoom and find
- ✓ Cookies and persistent profiles
- ✓ Private browsing mode
- ✓ DOM/accessibility inspection for automation
- ✓ Element actions (click, type, screenshot)
- ✓ Context isolation (websites cannot access IPC)
- ✓ Site permission prompts (location, camera, etc.)

**Verification:**
- Open internal browser tab
- Navigate to website
- Use back/forward
- Download file and verify
- Open private tab
- Verify cookies isolated
- Automation: inspect element tree
- Click element, type text
- Screenshot element
- Verify downloads appear in UI

### R5.4: External Browser Automation
**Priority:** Medium  
**Description:** Control external Chromium via managed profile or extension.

**Acceptance Criteria:**
- ✓ Launch Chromium with Krono profile
- ✓ Connect to running Chrome via extension (optional)
- ✓ Same automation capabilities as internal browser
- ✓ Never attach to personal profile without consent

**Verification:**
- Launch external Chromium
- Verify separate profile
- Automate navigation and interaction
- Close without affecting user's personal Chrome

### R5.5: Desktop Control
**Priority:** High  
**Description:** Screenshot, mouse, keyboard, app/window discovery.

**Acceptance Criteria:**
- ✓ Screenshot full screen or specific window
- ✓ Mouse move, click, drag
- ✓ Keyboard type and shortcuts
- ✓ Scroll
- ✓ App/window discovery and focus
- ✓ Clipboard access
- ✓ Visible active-control indicator
- ✓ Global emergency stop hotkey
- ✓ Pauses during Games foreground
- ✓ Windows: UI Automation
- ✓ macOS: Accessibility API (unverified until tested)
- ✓ Linux: X11 (unverified until tested)
- ✓ Wayland detection with unsupported message

**Verification (Windows):**
- Request screenshot
- Verify image captured
- Request mouse click at coordinates
- Verify click occurs
- Request keyboard type
- Verify text appears
- Discover running apps
- Focus specific window
- Trigger emergency stop
- Verify all automation halts
- Switch to Games
- Verify desktop control paused
- Switch back
- Verify resumed

### R5.6: Media Generation
**Priority:** Medium  
**Description:** Image generation/editing and video generation via provider APIs.

**Acceptance Criteria:**
- ✓ OpenAI image generation (DALL-E)
- ✓ Google image generation (Imagen)
- ✓ xAI video generation
- ✓ User-owned credentials
- ✓ Capability checks before enabling
- ✓ Shows provider, estimated cost, job status
- ✓ Cancellation where supported
- ✓ Preview and export

**Verification:**
- Configure OpenAI key
- Generate image
- Verify preview and download
- Configure xAI key
- Generate video
- Monitor job status
- Verify completion and preview

### R5.7: Scheduled Tasks
**Priority:** Medium  
**Description:** One-time and recurring automations.

**Acceptance Criteria:**
- ✓ Create one-time or recurring task
- ✓ Set timezone, next run, prompt, model, permissions
- ✓ Execution on designated device
- ✓ Background runner with opt-in
- ✓ Missed run handling (one catch-up)
- ✓ Duplicate prevention
- ✓ History and results
- ✓ Edit, pause, cancel

**Verification:**
- Create recurring task (every 5 minutes)
- Verify executions
- Close app before scheduled run
- Reopen after scheduled time
- Verify single catch-up execution
- Edit task schedule
- Verify next run updates
- Pause task
- Verify no execution
- Resume and verify execution resumes

---

## R6: Projects and Memory

### R6.1: Project Management
**Priority:** High  
**Description:** Projects with metadata, multiple local directories, and device-specific mappings.

**Acceptance Criteria:**
- ✓ Create project with name, icon, color, description
- ✓ Map one or multiple local directories
- ✓ Different folder mappings on different devices
- ✓ Project instructions and memory
- ✓ Project-specific connections and permissions
- ✓ Move conversations between projects

**Verification:**
- Create project with two local directories
- Set project instructions
- Create conversation in project
- Add project memory entry
- Move conversation to different project
- Verify context updated

### R6.2: Global and Project Memory
**Priority:** High  
**Description:** Editable memory with automatic proposals and acceptance flow.

**Acceptance Criteria:**
- ✓ Global memory editor
- ✓ Project memory editor
- ✓ Automatic memory proposals during conversations
- ✓ Proposals require acceptance
- ✓ Memory respects account and project boundaries
- ✓ Temporary side chats do not auto-create memory

**Verification:**
- Edit global memory
- Verify available across projects
- Edit project memory
- Verify scoped to project
- Conversation generates memory proposal
- Accept proposal
- Verify persisted
- Create side chat
- Verify does not create automatic memory

### R6.3: Synchronization
**Priority:** Critical  
**Description:** Sync chats, messages, projects, memories, settings, game scores.

**Acceptance Criteria:**
- ✓ Offline outbox with idempotent operations
- ✓ Optimistic UI updates
- ✓ Background sync worker
- ✓ Optimistic concurrency for edits
- ✓ Conflict detection with user resolution
- ✓ Tombstones for deletions
- ✓ Stable message/event IDs
- ✓ Two-client test shows sync
- ✓ Deleted data does not reappear

**Verification:**
- Client A: create chat, send message
- Client B: verify message appears
- Client A offline: send message
- Client A back online: verify sync
- Client A and B edit same memory entry
- Verify conflict shown with resolution UI
- Client A: delete chat
- Client B: verify deletion synced

---

## R7: Side Chats

### R7.1: Side Chat Creation
**Priority:** Medium  
**Description:** Open resizable side panel from normal conversation with snapshot of context.

**Acceptance Criteria:**
- ✓ Opens from any message or conversation
- ✓ Initialized with parent context snapshot at branching point
- ✓ References to attachments available
- ✓ Independent execution context
- ✓ Permissions cannot exceed parent
- ✓ Parent link shown

**Verification:**
- In conversation, open side chat from message
- Verify context includes prior messages
- Verify attached files referenced
- Set parent to Project permissions
- Verify side chat cannot select Full computer

### R7.2: Side Chat Expiry
**Priority:** Medium  
**Description:** Side chats expire 24 hours after last accepted user message or completed assistant message.

**Acceptance Criteria:**
- ✓ Timer shown in UI
- ✓ Tool events/heartbeats do not extend expiry
- ✓ "Keep as independent chat" removes expiry and preserves history
- ✓ On expiry: deny access, cancel active execution, delete data
- ✓ Cleanup on launch/reconnect
- ✓ Artifacts referenced by other chats preserved

**Verification:**
- Create side chat
- Wait 24 hours or advance system clock
- Verify access denied
- Create side chat
- Click "Keep as independent chat"
- Wait 24 hours
- Verify still accessible
- Create side chat with artifacts
- Reference artifact in main chat
- Let side chat expire
- Verify artifact still available

### R7.3: Content Transfer
**Priority:** Medium  
**Description:** Transfer messages or summaries from side chat to parent.

**Acceptance Criteria:**
- ✓ Select message or summary to transfer
- ✓ Preview transfer
- ✓ Queue for next safe boundary if parent active
- ✓ Do not rewrite parent messages
- ✓ Do not interrupt active tools
- ✓ Authorized file changes appear in parent activity

**Verification:**
- Side chat: generate useful summary
- Transfer to parent
- Verify appears in parent conversation
- Parent running agent task
- Attempt transfer
- Verify queued until task completes
- Side chat: edit file
- Verify appears in parent activity history

---

## R8: Games

### R8.1: Game Catalog
**Priority:** Critical  
**Description:** Exactly 24 complete games with original artwork.

**Acceptance Criteria:**
- ✓ All 24 games implemented and playable
- ✓ Original artwork, sounds, characters, layouts (no copied assets)
- ✓ Games page with search, favorites, recent
- ✓ Filters: All/Puzzle/Arcade/Strategy
- ✓ Miniature art and descriptions
- ✓ Responsive grid: 4/3/2/1 columns based on width
- ✓ Games available offline without active agent

**Game List:**
1. Minesweeper
2. Snake
3. Tic-Tac-Toe
4. Maze Chase (Pac-Man style)
5. Pong
6. Block Fit
7. Falling Blocks (Tetris style)
8. Sand Blocks
9. Flap (Flappy Bird style)
10. Darts
11. Jigsaw
12. Stack
13. Orbit
14. Color Gauntlet
15. Odd Color
16. 2048
17. Breakout
18. Memory Match
19. Sudoku
20. Solitaire
21. Connect Four
22. Reversi
23. Simon
24. Lights Out

**Verification:**
- Open Games page
- Verify 24 games present
- Search by name
- Favorite games and verify filter
- Filter by category
- Resize window and verify grid adapts
- Verify artwork is original

### R8.2: Game Implementation Requirements
**Priority:** Critical  
**Description:** Each game has complete rules, controls, pause, restart, serialization.

**Acceptance Criteria:**
- ✓ Full rules correctly implemented
- ✓ Appropriate controls (mouse/keyboard/touch)
- ✓ Pause, resume, restart
- ✓ Score/result tracking
- ✓ Settings persistence
- ✓ Save state for meaningful continuation
- ✓ Mute controls where applicable
- ✓ Instructions accessible

**Verification (per game):**
- Launch game
- Play through win or loss
- Verify rules enforced
- Pause and resume
- Verify state preserved
- Restart
- Verify reset to initial state
- Close during play
- Reopen and verify state restored
- Check settings
- Verify persist across sessions

### R8.3: Game-Specific Requirements
**Priority:** Critical  
**Description:** Special requirements for specific games.

**Minesweeper:**
- ✓ First click always safe

**Sudoku:**
- ✓ Unique solutions validated
- ✓ Offline puzzle bank

**Lights Out:**
- ✓ Solvable boards generated via legal moves from solved state

**Stack:**
- ✓ Perfect placement bonus: every 3 perfect restores 8% width, capped at original

**Color Gauntlet:**
- ✓ Documented perceptual color distance method (e.g., CIEDE2000 or Delta E)

**Simon:**
- ✓ Reduced-motion alternative (visual-only mode)

**Bot Games (Tic-Tac-Toe, Pong, Connect Four, Reversi, Maze Chase):**
- ✓ Deterministic bots, no network required
- ✓ Legal move validation
- ✓ Difficulty options where applicable

**Verification:**
- Minesweeper: first click is never mine
- Sudoku: verify solutions are unique via solver
- Lights Out: verify all boards solvable
- Stack: verify 3 perfect placements restore width
- Color Gauntlet: verify score calculation uses documented method
- Simon: enable reduced motion and verify mode switches
- Bots: verify legality, never invalid moves

### R8.4: Games and Agent Integration
**Priority:** Critical  
**Description:** Games remain responsive alongside agent execution with non-intrusive notifications.

**Acceptance Criteria:**
- ✓ Task strip visible during gameplay showing active chat/task
- ✓ Pending approvals visible
- ✓ Return to chat button
- ✓ Completion notification does not steal focus
- ✓ Gameplay never implies approval
- ✓ Returning to chat pauses and saves game
- ✓ Desktop control pauses during gameplay
- ✓ Blur pauses game and releases keyboard capture

**Verification:**
- Start agent task
- Switch to game
- Verify task strip shows status
- Agent requests approval
- Verify notification shown
- Continue playing
- Verify game responsive
- Task completes
- Verify notification appears without focus theft
- Return to chat
- Verify game paused
- Resume game
- Verify game continues from saved state

---

## R9: Plugins and Connections

### R9.1: Native Plugin Manifest
**Priority:** High  
**Description:** Plugin manifest with ID, version, components, permissions.

**Acceptance Criteria:**
- ✓ Manifest declares: ID, version, name, description
- ✓ Components: Skills, MCP servers, commands, hooks, UI
- ✓ Required permissions
- ✓ Optional sandboxed UI

**Verification:**
- Create test plugin with manifest
- Install plugin
- Verify components detected

### R9.2: MCP Support
**Priority:** High  
**Description:** Official MCP stdio and HTTP with lifecycle management.

**Acceptance Criteria:**
- ✓ MCP server discovery
- ✓ stdio and HTTP transports
- ✓ OAuth where applicable
- ✓ Timeouts and cancellation
- ✓ Process isolation

**Verification:**
- Install MCP plugin
- Verify server starts
- Call MCP tool
- Verify response
- Cancel operation
- Verify cancellation propagates
- Kill plugin process
- Verify cleanup

### R9.3: Compatibility Adapters
**Priority:** High  
**Description:** Import Codex, Claude Code, Antigravity plugins and standalone SKILL.md.

**Acceptance Criteria:**
- ✓ Detect and import .codex-plugin/plugin.json
- ✓ Detect and import .claude-plugin/plugin.json
- ✓ Detect and import Antigravity plugin.json
- ✓ Import standalone SKILL.md packages
- ✓ Import standalone MCP configuration
- ✓ Show compatibility per component (Supported/Needs configuration/Unsupported)
- ✓ Preview before import
- ✓ Do not auto-copy credentials

**Verification:**
- Prepare test Codex plugin
- Import and preview
- Verify component compatibility shown
- Install
- Verify functional components work
- Verify unsupported components do not break app
- Repeat for Claude Code plugin
- Repeat for standalone SKILL.md

### R9.4: Plugin Import Safety
**Priority:** Critical  
**Description:** Safe plugin import preventing traversal and unsafe symlinks.

**Acceptance Criteria:**
- ✓ Archive traversal prevention
- ✓ Symlink escape detection
- ✓ Scripts/hooks disabled until reviewed and approved
- ✓ Permission requests during installation
- ✓ Malformed manifest handling (does not crash app)

**Verification:**
- Create malicious archive with ../ paths
- Attempt import
- Verify rejection with error
- Create plugin with scripts
- Import
- Verify scripts disabled
- Review and approve
- Verify scripts now enabled
- Create malformed manifest
- Attempt import
- Verify graceful error, no crash

### R9.5: Working Connections
**Priority:** High  
**Description:** GitHub, Git, Google Drive, Google Calendar, Discord webhooks.

**Acceptance Criteria:**

**GitHub:**
- ✓ Device authorization or fine-grained token
- ✓ Repository, issue, PR reading
- ✓ Issue creation
- ✓ Minimal scopes

**Git:**
- ✓ Status, diff, history, staging, commit, branch operations
- ✓ Works with local repositories

**Google Drive:**
- ✓ Search, read, upload
- ✓ OAuth with development test users
- ✓ Minimal scopes

**Google Calendar:**
- ✓ Read, create, update events
- ✓ Same OAuth as Drive

**Discord:**
- ✓ Webhook sending
- ✓ User-configured URL

**General:**
- ✓ Connection writes obey permission system
- ✓ Disconnect and revoke instructions
- ✓ Clear scope explanations

**Verification:**
- GitHub: connect, read repo, read issue, create issue
- Git: status, stage, commit, branch
- Google Drive: search, read file, upload file
- Google Calendar: read events, create event
- Discord: configure webhook, send message
- For each: disconnect and verify revoke instructions

---

## R10: UI and Accessibility

### R10.1: Layout
**Priority:** Critical  
**Description:** Resizable sidebar, main content, right panel, terminal drawer.

**Acceptance Criteria:**
- ✓ Sidebar: resizable (default 248px), collapsible
- ✓ Main content workspace: chat/games/automations/etc.
- ✓ Right panel: resizable, for side chats/browser/files/diffs/artifacts
- ✓ Terminal drawer: resizable, bottom
- ✓ Layout preferences persist per account

**Verification:**
- Resize sidebar
- Verify snaps to minimum width
- Collapse sidebar
- Verify content expands
- Open right panel
- Resize
- Verify minimum/maximum widths
- Open terminal
- Resize height
- Logout and login
- Verify layout restored

### R10.2: Themes
**Priority:** High  
**Description:** Dark, Light, Halloween, Follow system themes with specified colors.

**Acceptance Criteria:**
- ✓ Dark theme colors as specified
- ✓ Light theme colors as specified
- ✓ Halloween theme colors as specified
- ✓ Follow system matches OS preference
- ✓ Smooth theme transitions
- ✓ All UI elements adapt to theme
- ✓ Readable contrast in all themes

**Verification:**
- Switch to each theme
- Verify colors match specification
- Check sidebar, content, panels, terminal, games
- Set to Follow system
- Change OS theme
- Verify app follows

### R10.3: Typography
**Priority:** High  
**Description:** Inter for UI, JetBrains Mono for code, with user font selection.

**Acceptance Criteria:**
- ✓ Inter for prose with system fallbacks
- ✓ JetBrains Mono for code with fallbacks
- ✓ Independent font/size for prose and code
- ✓ Defaults: prose 15px, code 13px
- ✓ Installed-font discovery
- ✓ Live preview in settings
- ✓ Code themes: GitHub Light/Dark, Dracula, Halloween
- ✓ Preferences apply to code blocks, editors, terminal
- ✓ Larger text does not break layouts

**Verification:**
- Check default fonts
- Change prose font and size
- Verify applied in chat
- Change code font and size
- Verify applied in code blocks, Monaco editor, terminal
- Select each code theme
- Verify syntax highlighting
- Increase text size to 24px
- Verify layout adapts without breaking

### R10.4: Logo and Branding
**Priority:** High  
**Description:** Original vector logo with hourglass/K negative space and pixel accent.

**Acceptance Criteria:**
- ✓ Logo is original (not emoji or stock asset)
- ✓ Two offset rounded geometric forms
- ✓ Hourglass through negative space
- ✓ Subtly forms K
- ✓ Small square pixel accent
- ✓ SVG format
- ✓ Monochrome SVG variant
- ✓ App icons at 16, 32, 256, 512, 1024px
- ✓ English wordmark
- ✓ Readable at all sizes
- ✓ Consistent geometry across themes

**Verification:**
- View logo in app
- Verify originality and design elements
- Check task bar icon (16px)
- Check dock/taskbar (32px)
- Check About dialog (256px)
- Verify readability
- Switch themes
- Verify logo adapts

### R10.5: Accessibility
**Priority:** Critical  
**Description:** Keyboard navigation, reduced motion, readable contrast, ARIA.

**Acceptance Criteria:**
- ✓ All interactive elements keyboard accessible
- ✓ Focus indicators visible
- ✓ Tab order logical
- ✓ Escape dismisses modals
- ✓ Reduced motion preference respected
- ✓ ARIA labels on controls
- ✓ WCAG AA contrast minimum
- ✓ Screen reader compatibility (basic)

**Verification:**
- Navigate entire app with keyboard only
- Verify all features accessible
- Check focus indicators visible
- Enable reduced motion
- Verify animations stop or simplify
- Use contrast checker on all theme text/background pairs
- Test with NVDA (Windows) or VoiceOver (macOS)

### R10.6: Responsive Design
**Priority:** High  
**Description:** Support 1024×768 minimum, 1920×1080 typical, 100-150% DPI.

**Acceptance Criteria:**
- ✓ Usable at 1024×768
- ✓ Optimal at 1280×800 and 1920×1080
- ✓ 100% and 150% DPI scaling
- ✓ Chat text max ~820px
- ✓ Code/tables can use more space
- ✓ Game grid adapts (4/3/2/1 columns)

**Verification:**
- Set window to 1024×768
- Verify all UI accessible
- Set to 1920×1080
- Verify layout uses space well
- Enable 150% DPI in Windows settings
- Verify text and controls scale
- Measure chat text width
- Verify ~820px max for paragraphs

---

## R11: Packaging and Distribution

### R11.1: Windows Build
**Priority:** Critical  
**Description:** Windows installer and portable build tested independently.

**Acceptance Criteria:**
- ✓ NSIS installer via electron-builder
- ✓ Portable ZIP build
- ✓ Includes all dependencies
- ✓ Installer creates Start Menu entry
- ✓ Installer creates Desktop shortcut (optional)
- ✓ Uninstaller works cleanly
- ✓ Tested on clean Windows 11 machine
- ✓ Launches successfully
- ✓ No missing DLL errors

**Verification:**
- Build installer on CI
- Download artifact
- Install on clean Windows 11 VM
- Launch and verify functionality
- Uninstall and verify cleanup
- Download portable build
- Extract and run
- Verify works without installation

### R11.2: macOS Build (Unverified)
**Priority:** Medium  
**Description:** DMG and .app build via CI, labeled unverified until tested.

**Acceptance Criteria:**
- ✓ DMG build via electron-builder
- ✓ .app bundle signed (if certificate available)
- ✓ Includes all dependencies
- ✓ CI artifact produced
- ⚠ Labeled "macOS build unverified" until tested on macOS

**Verification (when possible):**
- Build on macOS runner
- Download artifact
- Open DMG
- Drag to Applications
- Launch and verify

### R11.3: Linux Build (Unverified)
**Priority:** Medium  
**Description:** AppImage or deb/rpm build via CI, labeled unverified.

**Acceptance Criteria:**
- ✓ AppImage build via electron-builder
- ✓ Includes all dependencies
- ✓ CI artifact produced
- ⚠ Labeled "Linux build unverified" until tested on Linux

**Verification (when possible):**
- Build on Ubuntu runner
- Download artifact
- Set executable permission
- Run AppImage
- Verify functionality

### R11.4: GitHub Releases
**Priority:** Critical  
**Description:** Automated releases via GitHub Actions.

**Acceptance Criteria:**
- ✓ Release workflow triggered on version tag
- ✓ Builds for Windows, macOS, Linux
- ✓ Uploads artifacts to GitHub Release
- ✓ Release notes from CHANGELOG or commit messages
- ✓ Checksums for verification

**Verification:**
- Create version tag (e.g., v0.1.0)
- Push tag
- Verify CI triggers
- Verify release created
- Verify artifacts uploaded
- Download and verify checksums

### R11.5: Documentation Site
**Priority:** High  
**Description:** Small site via GitHub Pages with setup and download links.

**Acceptance Criteria:**
- ✓ Hosted on GitHub Pages
- ✓ Landing page with overview
- ✓ Setup instructions with screenshots
- ✓ Download links to latest releases
- ✓ Game catalog page
- ✓ Live URL works
- ✓ Updates reflected after changes

**Verification:**
- Push site to gh-pages branch
- Verify live at https://<user>.github.io/<repo>
- Check all links work
- Update download links
- Verify updates appear

---

## R12: Verification and Testing

### R12.1: Unit Tests
**Priority:** High  
**Description:** Vitest for critical logic paths.

**Acceptance Criteria:**
- ✓ Provider adapters tested with contract tests
- ✓ Permission enforcement tested
- ✓ Tool input validation tested
- ✓ Game logic tested (win/loss, rules, bots)
- ✓ Synchronization logic tested
- ✓ Runs in CI

**Verification:**
- Run npm test
- Verify >80% coverage for critical modules
- Verify CI runs tests on every push

### R12.2: Integration Tests
**Priority:** High  
**Description:** Playwright Electron tests for desktop flows.

**Acceptance Criteria:**
- ✓ Authentication flow tested
- ✓ Model request and response tested
- ✓ Tool execution tested
- ✓ Permission approval tested
- ✓ Game launch and interaction tested
- ✓ Runs on Windows in CI
- ✓ Uses test fixtures

**Verification:**
- Run npm run test:e2e
- Verify tests pass
- Verify runs on Windows in CI
- Add new test for critical flow
- Verify test fails when feature broken

### R12.3: Manual Verification Checklist
**Priority:** Critical  
**Description:** Documented manual tests covering all requirements.

**Acceptance Criteria:**
- ✓ Checklist covers all acceptance criteria
- ✓ Executed before release
- ✓ Results documented with evidence
- ✓ Blockers identified clearly

**Verification:**
- Create checklist in .kiro/specs
- Before release, execute all items
- Document results and screenshots
- Sign off on checklist

---

## Requirements Summary

**Total Requirements:** 60+ acceptance criteria across 12 major requirement groups

**Critical Path:**
1. R1: Authentication → R2: Models → R3: Modes → R4: Permissions → R5: Tools
2. R8: Games (parallel development)
3. R6: Projects/Memory → R7: Side Chats → R9: Plugins/Connections
4. R10: UI/Accessibility → R11: Packaging → R12: Verification

**Estimated Complexity:**
- High: R2 (providers), R5 (tools), R8 (games), R10 (UI)
- Medium: R1, R3, R4, R6, R9
- Low: R7, R11, R12
