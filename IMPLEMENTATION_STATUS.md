# Krono Agent — implementation status

Reviewed 2026-11-05. The Windows UI has been migrated from Electron/React to
native C#/WinUI 3. This is not completion of the full original product scope.

## Current architecture

- Renamed product executable: KronoAgent.exe, with shared native typography,
  surfaces, motion and live dark/light/Halloween resources.
- Native shell, account forms, chat, Markdown, project/memory forms, settings,
  terminal surface and game rendering. No Electron or React runtime.
- Embedded WebView2 is used only for browsing, with per-account profiles,
  private tabs, navigation, history/bookmarks, find, zoom and downloads.
- Private bundled Node worker over stdio preserves the existing provider
  adapters, tool validation, project file boundaries, approvals and cancellation.
- Existing per-account SQLite databases and account-service.json paths are retained.
- Native sessions/keys use Windows DPAPI. Configure them once in the new UI;
  existing legacy encrypted files are not deleted.

## Verification

- 19 agent/provider/file-boundary/SQL tests pass. Removed Electron-specific
  auth/approval tests were replaced by real native UI flows, not marked passing.
- Windows UI Automation fixture exercises registration/sign-in controls,
  project creation and scoped memories, streamed model response with Chat tools
  omitted, project reassignment, approved file write (no write before approval),
  eight game initializations and board moves, real PTY output 42, WebView2 page
  navigation and absence of host/Node APIs, process restart, manual logout,
  and rejection of a remotely revoked refresh session on restart.
- Ollama, LM Studio and custom OpenAI-compatible endpoints now have native
  setup, discovery/manual model IDs, connection testing, streaming, cancellation
  and opt-in tool support. Real HTTP and native fixtures cover the protocol and
  credential separation; actual local inference is not yet accepted on hardware.
- The fixture uses local auth/provider services. It is not live Supabase/model
  acceptance. Browser permissions/downloads and extended game balancing still
  need additional acceptance beyond the implemented paths.
- Native build and self-contained Windows x64 folder publish succeed locally.
  An isolated published folder passed the full native fixture after explicit
  inclusion of compiled XAML resources. GitHub native CI passed on dab084b;
  expanded published-app Windows CI also passed on 31e4825. That run failed
  overall because GitHub could not provision its macOS ARM runner; a supported
  Intel runner now replaces that infrastructure leg. All three CI legs and Pages
  passed on ae25dc5, including the published Windows fixture (see .kiro review).
- npm audit: zero vulnerabilities after removing legacy UI/build dependencies
  and validating the remaining modern minimatch callers against patched 11.0.4.
- NuGet audit: no vulnerable direct/transitive packages reported.

## Remaining product requirements

- Live owner auth/provider acceptance, cloud record/attachment synchronization,
  OAuth, scheduled tasks, side chats, plugins/import, app connections, local
  bundled local inference/model downloads, media generation, agent browser/terminal/computer-use tools,
  automatic memory proposals and sixteen additional games remain incomplete.
- macOS/Linux desktop UIs are not implemented after the owner's explicit
  choice of native Windows UI. Agent unit checks remain cross-platform.
- Runtime signing, automatic updates and a production installer are not verified.

Initial specs/work used Kiro Crew. Native migration and subsequent implementation
are by Codex, authorized by the owner. See .kiro review logs for provenance.
