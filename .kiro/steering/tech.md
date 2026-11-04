# Krono Agent — current technical direction

Owner authorized replacement of Electron/React with native Windows UI on 2026-11-04.
This supersedes original stack examples in the design and historical review logs.

- C#/.NET 10 + WinUI 3 / Windows App SDK 2.6.5: native shell, navigation, chat,
  Markdown (Markdig), projects, settings, games and terminal surface.
- WebView2 only for the browser, with per-account profiles and InPrivate tabs.
- Private Node 24 worker over inherited stdio: existing provider adapters,
  bounded tool loop, file boundaries, cancellation and one-action approvals.
- OpenAI/Anthropic/Google adapters plus Chat Completions endpoints for Ollama,
  LM Studio and custom servers. Custom endpoint tool support is opt-in; URLs
  require HTTPS except loopback HTTP, and credentials never belong in URLs.
- Microsoft.Data.Sqlite: same account-specific database paths/schema.
- Supabase Auth over HTTPS, Windows DPAPI for persistent sessions and keys.
- Windows x64 unpackaged, self-contained publish folder; no Electron runtime.
- Vitest agent/SQL tests + Windows UI Automation with local auth/provider
  fixtures. Actual cloud acceptance must be recorded separately.
- npm audit and NuGet vulnerability audit accompany release verification.

Full product requirements remain in requirements.md. Native migration does not
imply that missing games, cloud synchronization, plugins or desktop automation
have been implemented.
