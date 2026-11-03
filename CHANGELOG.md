# Changelog

All notable changes to Krono Agent will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Complete .kiro documentation (product, tech, structure, requirements, design, tasks)
- Electron + React + TypeScript foundation
- SQLite database with migrations
- Per-account database isolation
- Authentication system (mock, ready for Supabase)
- Theme system (Dark, Light, Halloween, System)
- Theme persistence
- Original logo design (hourglass/K with pixel accent)
- Snake game (complete with scoring, pause, game over)
- Main layout with sidebar navigation
- Zustand state management
- Zod-validated IPC
- GitHub Actions CI/CD workflows
- Development documentation

### Changed
- N/A

### Deprecated
- N/A

### Removed
- N/A

### Fixed
- N/A

### Security
- Context isolation enabled for renderer
- No Node integration in renderer
- Zod validation on all IPC messages
- Per-account database isolation

## [0.1.0] - 2026-11-03

### Added
- Initial repository setup
- Project structure and configuration
- Foundation for future development

---

## Version Numbering

- MAJOR: Breaking changes
- MINOR: New features (backward compatible)
- PATCH: Bug fixes (backward compatible)

Current Status: Pre-release (0.1.0)
First stable release target: 1.0.0
