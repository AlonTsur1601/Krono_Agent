# Krono Agent - Product Specification

## Purpose

Krono Agent is an English-language desktop agent workspace that distinguishes itself through a polished collection of 24 small games users can play while the agent works. The product combines the familiar utility of modern agent applications (Codex, Claude Code, Copilot, Goose) with an original visual identity that is sophisticated and playful.

## Core Value Proposition

While agents execute long-running tasks—file operations, terminal commands, web research, desktop automation—users can engage with high-quality games in the same application. Completion notifications arrive without interrupting gameplay, and users return to see completed work.

## Scope

### In Scope

**Agent Capabilities:**
- Multi-provider model integration (OpenAI, Anthropic, Google, Azure, xAI, OpenRouter, Groq, Mistral, Ollama, LM Studio, custom endpoints)
- Built-in local models (Qwen 4B and 8B) without requiring external installation
- Four execution modes: Chat (no tools), Plan (read-only), Agent (authorized execution), Goal (persistent multi-step)
- Permission system: Ask, Project-scoped, Full computer
- Comprehensive tools: files, terminal, browser, computer control, media generation, scheduling
- Real-time activity monitoring with approvals and cancellation

**Games:**
- Exactly 24 complete, playable games across puzzle/arcade/strategy genres
- Original artwork, sound, characters, and layouts
- Persistent settings, scores, and saved game states
- Responsive gameplay alongside agent execution
- Available offline and without active agent tasks

**Platform Features:**
- Cloud accounts with Supabase (Auth, PostgreSQL, Storage, RLS)
- Offline operation with synchronization
- Projects with multiple local directories
- Global and project-specific memory
- Side chats with 24-hour expiry
- Automations (one-time and recurring)
- Plugin system with Codex/Claude Code/Antigravity compatibility adapters
- Working connections: GitHub, Git, Google Drive, Google Calendar, Discord webhooks

**Design:**
- Dark, Light, Halloween, and Follow system themes
- Custom typography with Inter/JetBrains Mono and user font selection
- Original vector logo suggesting hourglass/K through negative space
- Resizable sidebar, content workspace, right panel, terminal drawer
- 8px spacing rhythm, restrained motion, accessible controls

### Out of Scope

- Multiplayer gaming
- Live leaderboards or social features
- Paid cloud infrastructure or automatic billing
- Frontier model hosting
- Full browser extension compatibility
- UAC secure-desktop interaction
- Automatic credential pooling or limit evasion
- Editor extensions or proprietary app connectors beyond adapters

## Target Audience

Developers and technical users who:
- Want an agent workspace with its own character
- Appreciate polished details and cohesive design
- Value productive downtime during long-running tasks
- Prefer local-first operation with optional cloud sync
- Need flexibility in model and provider choices

## Behavior Principles

1. **Transparency:** Show actual state. Never fabricate progress, thoughts, or waiting time.
2. **Control:** Explicit permissions. Approvals for writes and side effects. Emergency stop.
3. **Isolation:** Account boundaries, permission enforcement, process separation, expiring side chats.
4. **Persistence:** Synchronization with conflict detection, offline outbox, idempotent operations.
5. **Polish:** Original assets, coherent motion, accessible patterns, responsive layouts, legible small sizes.
6. **Playfulness:** Games as first-class feature with quality implementation, not demos or placeholders.

## Design Constraints

- No marketing copy or slogans in the application
- No stolen assets—original artwork and characters
- No fabricated AI reasoning displays
- No silent permission escalation
- No credentials in model context, logs, or renderer processes
- No gameplay interruption by completion notifications
- No focus theft during active game sessions
- Games must work offline
- Activity must reflect actual tool execution
- Build success does not substitute for runtime verification

## Success Criteria

A complete implementation means:
- Real authentication with offline access
- Real model requests and streaming
- Real tool execution within permission boundaries
- Real file changes with diffs and restoration
- Real terminal I/O and cancellation
- Real browser tabs and automation
- Real desktop control with emergency stop
- Real media generation where credentials exist
- Real synchronization with two-client test evidence
- 24 complete games with rules, bots, serialization
- Real plugin imports with compatibility reporting
- Real connection actions when authorized
- Packaging that installs and runs independently

## Platform Targets

- Primary: Windows 11 x64
- Portable architecture with CI for macOS and Linux
- Desktop automation: Windows UI Automation, macOS Accessibility, X11 (Wayland capabilities marked as unsupported where applicable)
- Only label an OS/capability as verified after testing on that platform
