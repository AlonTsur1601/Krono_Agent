# Krono Agent — implementation status

Reviewed 2026-11-05. The Windows UI has been migrated from Electron/React to
native C#/WinUI 3. This is not completion of the full original product scope.

## Current architecture

- Renamed product executable: KronoAgent.exe, with shared native typography,
  surfaces, motion and live dark/light/Halloween resources.
- Bundled Rubik text (including Hebrew), Space Grotesk headings and JetBrains
  Mono code fonts, with their OFL licenses; no system font installation needed.
- Native shell, account forms, chat, Markdown, project/memory forms, settings,
  terminal surface and game rendering. No Electron or React runtime.
- Embedded WebView2 is used only for browsing, with per-account profiles,
  private tabs, navigation, history/bookmarks, find, zoom and downloads.
- Private bundled Node worker over stdio preserves the existing provider
  adapters, tool validation, project file boundaries, approvals and cancellation.
- Existing per-account SQLite databases and account-service.json paths are retained.
- Native resizable side conversations copy the transcript/project context at a
  conversation or message branch, run independently with no tools, and expire
  24 hours after accepted user/completed assistant messages. Kept branches become
  normal chats; previewed transfers queue until the parent's run is finished.
- Native sessions/keys use Windows DPAPI. Configure them once in the new UI;
  existing legacy encrypted files are not deleted.

## Verification

- 28 agent/provider/file-boundary/SQL tests pass. Removed Electron-specific
  auth/approval tests were replaced by real native UI flows, not marked passing.
- Windows UI Automation fixture exercises registration/sign-in controls,
  project creation and scoped memories, streamed model response with Chat tools
  omitted, project reassignment, approved file write (no write before approval),
  twenty-four game initializations and board moves, real PTY output 42, WebView2 page
  navigation and absence of host/Node APIs, process restart, manual logout,
  and rejection of a remotely revoked refresh session on restart.
- Side-chat fixtures verify independent completion while a parent stream waits,
  snapshot scope, omitted tools, durable queued transfer, promotion, active expiry
  cancellation/deletion, startup cleanup, and chat use after manual re-login.
- Ollama, LM Studio and custom OpenAI-compatible endpoints now have native
  setup, discovery/manual model IDs, connection testing, streaming, cancellation
  and opt-in tool support. Real HTTP and native fixtures cover the protocol and
  credential separation; actual local inference is not yet accepted on hardware.
- Azure OpenAI v1, xAI, Groq, Mistral and OpenRouter profiles share an authoritative
  worker/native catalog, with credential isolation, discovery/manual IDs and
  explicit per-model tool opt-in. Native fixtures exercise all five profiles;
  HTTP tests classify access errors, quota/retry hints and provider outages.
- The fixture uses local auth/provider services. It is not live Supabase/model
  acceptance. Browser permissions/downloads and extended game balancing still
  need additional acceptance beyond the implemented paths.
- Native build and self-contained Windows x64 folder publish succeed locally.
  An isolated published folder passed the full native fixture after explicit
  inclusion of compiled XAML resources. GitHub native CI passed on dab084b;
  expanded published-app Windows CI also passed on 31e4825. That run failed
  overall because GitHub could not provision its macOS ARM runner; a supported
  Intel runner now replaces that infrastructure leg. All three CI legs and Pages
  passed on 6b3624c, including the published Windows fixture (see .kiro review).
- npm audit: zero vulnerabilities after removing legacy UI/build dependencies
  and validating the remaining modern minimatch callers against patched 11.0.4.
- NuGet audit: no vulnerable direct/transitive packages reported.

## Remaining product requirements

- Live owner auth/provider and production sync acceptance, attachment synchronization,
  OAuth, closed-app scheduled execution, side-chat attachment references/sync, plugins/import,
  app connections, bundled local inference/model downloads, media generation,
  agent browser/computer-use tools and durable terminal jobs,
  automatic memory proposals remain incomplete.
- GitHub Copilot integration, capability-adaptive composer controls, dictation,
  image/PDF/binary attachments and durable goal execution remain incomplete. GitHub Models
  retired on 2026-07-30; its discontinued API is not offered as a working profile.
- macOS/Linux desktop UIs are not implemented after the owner's explicit
  choice of native Windows UI. Agent unit checks remain cross-platform.
- Runtime signing, automatic updates and a production installer are not verified.

Initial specs/work used Kiro Crew. Native migration and subsequent implementation
are by Codex, authorized by the owner. See .kiro review logs for provenance.

## Latest verified increment

- Bounded agent PowerShell commands with detailed Ask approval, cancellation,
  timeout, process-tree termination and known-secret output redaction. Project,
  Chat and Plan omit command execution. This is not an OS sandbox or durable job
  manager; detached background services remain unsupported.
- Native run/approval/response activity while playing, with return-to-chat pausing
  the current game. Versioned local SQLite saves now restore all twenty-four games paused; optional account sync also includes game saves.
- Cached chat/terminal typography updates preserve separate text/code fonts.
- 34 core tests passed. Published native fixture 97jxma passed actual command
  approval/execution, game/response coexistence and cached font-role checks,
  alongside account, side-chat, twelve-game, PTY and WebView2 checks.

- Native Reversi, Sudoku, Stack and Color Gauntlet passed rules and actual UI
  checks, including move legality, unique offline puzzles, perfect placement,
  D65 CIE76 scoring and restart restoration. Worker events now drain on the UI
  thread in stream order; the fast multi-fragment response is verified in both
  the transcript and SQLite (native fixture YRaW9P).

- Maze Chase, Flap, Darts and Jigsaw bring the playable catalog to twenty.
  Published fixture 6suxlx verifies actual movement/flight/throws/swaps, all
  twenty saves, restart/finish flows and native minimize pause.

- All 24 specified games are playable in the native library. The last four are
  Block Fit, Falling Blocks, Sand Blocks and Klondike Solitaire. Search,
  categories, favorites, recent games and personal bests are implemented.
- Every card uses a WinUI bitmap rendered from that game's real board renderer.
  Save choice appears only after opening a game that has a compatible save.
  The redesigned play surfaces share a score HUD, responsive board framing and
  motion that honors the Windows animation preference. Native debug fixture
  WRYJzT passed the 24-game flows. Audio, additional bot difficulty options and
  desktop-control coordination still need work.

- Account sync over the existing Supabase tables covers normal chats/messages,
  projects/instructions, memories, portable appearance and game data. Enable
  it per device in Settings. Durable outbox, CAS edits and tombstones passed
  the two-device REST/SQLite fixture; native UI also verified enabling sync
  and choosing the cloud copy of a conflicting memory (Aj138w). Production
  credentials/data and file attachment sync have not been accepted.

- Opt-in one-time, interval and daily schedules run on this device while
  Krono is open and signed in. The local scheduler records one missed run,
  prevents duplicate claims, handles timezone/DST and keeps result history.
  An OS service that runs while Krono is closed is not installed.
- The composer accepts up to four bounded text/code snapshots. File content
  becomes part of the message; source paths are not transmitted. Original
  binary files, images and PDFs are outside this increment.
