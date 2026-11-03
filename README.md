# Krono Agent

Desktop agent workspace with 24 games to play while the agent works.

## Overview

Krono Agent combines the utility of modern agent applications (Codex, Claude Code, Copilot, Goose) with a polished collection of small games. While the agent executes long-running tasks—file operations, terminal commands, web research, desktop automation—users can engage with high-quality games. Completion notifications arrive without interrupting gameplay.

**Key Features:**
- Multi-provider model integration (OpenAI, Anthropic, Google, Azure, xAI, OpenRouter, Groq, Mistral, Ollama, LM Studio, custom endpoints)
- Built-in local models (Qwen 4B and 8B) without external installation
- Four execution modes: Chat (no tools), Plan (read-only), Agent (authorized execution), Goal (persistent multi-step)
- Permission system: Ask, Project-scoped, Full computer
- Comprehensive tools: files, terminal, browser, computer control, media generation, scheduling
- 24 complete, playable games across puzzle/arcade/strategy genres
- Cloud accounts with offline operation
- Projects with multiple local directories
- Side chats with 24-hour expiry
- Automations (one-time and recurring)
- Plugin system with Codex/Claude Code/Antigravity compatibility

## Installation

### Windows

Download the installer from [GitHub Releases](https://github.com/AlonTsur1601/Krono_Agent/releases):

1. Download `Krono-Agent-Setup-x.x.x.exe`
2. Run the installer
3. Follow the setup wizard
4. Launch Krono Agent from the Start Menu or Desktop shortcut

**Portable Version:**
Download `Krono-Agent-x.x.x-win.zip`, extract, and run `Krono Agent.exe`.

### macOS (Unverified)

CI builds available but not tested on macOS. Download `Krono-Agent-x.x.x.dmg` from releases.

### Linux (Unverified)

CI builds available but not tested on Linux. Download `Krono-Agent-x.x.x.AppImage` from releases.

## Setup

### 1. Create Account

On first launch, create a Krono account using:
- Email/password
- Google OAuth
- GitHub OAuth

### 2. Choose Model

Select one of:
- **Download local model:** Qwen 4B (smaller, faster) or Qwen 8B (larger, better)
- **Connect cloud provider:** Add API key for OpenAI, Anthropic, Google, etc.
- **Connect local endpoint:** Ollama or LM Studio URL

### 3. Configure Permissions

Choose default permission profile:
- **Ask:** Approve every write, command, or desktop action
- **Project:** Auto-approve file operations in project directories
- **Full computer:** Explicit activation for broader capabilities

### 4. Explore

- **Chat:** Start conversations in Chat, Plan, Agent, or Goal mode
- **Games:** Browse 24 games—play while the agent works
- **Projects:** Create projects with local directories
- **Automations:** Schedule recurring tasks
- **Connections:** Add GitHub, Google Drive, Calendar, Discord
- **Plugins:** Import Codex/Claude Code plugins

## Documentation

- [Setup Guide](docs/setup.md) - Detailed installation and configuration
- [Connections](docs/connections.md) - Provider and plugin setup
- [Games](docs/games.md) - Game catalog with rules
- [Architecture](docs/architecture.md) - Technical overview

## Development

### Requirements

- Node.js 20+
- Windows 11 (primary), macOS, or Linux

### Build from Source

```bash
# Clone repository
git clone https://github.com/AlonTsur1601/Krono_Agent.git
cd Krono_Agent

# Install dependencies
npm install

# Run in development
npm run dev

# Build
npm run build

# Package for Windows
npm run package:win
```

### Project Structure

```
Krono_Agent/
├── .kiro/              # Kiro Crew artifacts
├── src/
│   ├── main/           # Electron main process
│   ├── renderer/       # React UI
│   ├── agent/          # Agent execution
│   ├── games/          # 24 game modules
│   ├── sidecar/        # Native processes (llama.cpp, whisper.cpp)
│   └── shared/         # Shared types/schemas
├── assets/             # Logo, game art, sounds
├── docs/               # Documentation
└── tests/              # Unit and integration tests
```

### Testing

```bash
# Unit tests
npm test

# Integration tests
npm run test:e2e

# Linting
npm run lint
```

## Built With

- **Electron** + TypeScript
- **React** + Vite
- **Tailwind CSS** + Radix UI
- **Zustand** + TanStack Query
- **SQLite** + Supabase
- **llama.cpp** for local inference
- **xterm.js** + node-pty for terminal
- **Monaco** for code editing

## Games

24 complete games with original artwork:

**Puzzle:** Minesweeper, Tic-Tac-Toe, Block Fit, 2048, Sudoku, Solitaire, Memory Match, Jigsaw, Lights Out

**Arcade:** Snake, Maze Chase, Pong, Falling Blocks, Sand Blocks, Flap, Darts, Stack, Breakout

**Strategy:** Connect Four, Reversi, Color Gauntlet, Odd Color, Orbit, Simon

See [Games Documentation](docs/games.md) for rules and instructions.

## License

MIT License - see [LICENSE](LICENSE) for details.

Third-party licenses and model attributions in THIRD_PARTY.txt.

## Contributing

This is a Kiroween 2026 project. Contributions welcome after initial release.

## Links

- [GitHub Repository](https://github.com/AlonTsur1601/Krono_Agent)
- [Releases](https://github.com/AlonTsur1601/Krono_Agent/releases)
- [Documentation Site](https://AlonTsur1601.github.io/Krono_Agent)
- [Issue Tracker](https://github.com/AlonTsur1601/Krono_Agent/issues)

---

Built with [Kiro Crew](https://kiro.ai) for Kiroween 2026.
