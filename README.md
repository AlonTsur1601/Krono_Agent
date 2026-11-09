# Krono Agent

Native Windows agent workspace built with C# / WinUI 3. Chat, projects, settings,
Markdown and games use native XAML controls. Only the embedded web browser uses
WebView2. The agent runs in a private local Node worker over stdio, without an
Electron host or a localhost HTTP control server.

The UI migration is distinct from completion of the original product scope.
See [implementation status](IMPLEMENTATION_STATUS.md) and [.kiro requirements](.kiro/specs/krono-agent/requirements.md).

## Develop on Windows

For Windows x64, [download the Krono Agent installer](https://github.com/AlonTsur1601/Krono_Agent/releases/latest/download/Krono-Agent-Setup-x64.exe). The installer works offline and lets you choose the installation folder and add a desktop shortcut named **Krono Agent**.

To build from source, install Node 24+ and .NET 10 SDK, then:

```powershell
npm ci
npm run dev
```

`npm run build` builds the native app and bundles its agent runtime.
`npm run package:win` publishes a Windows x64 folder under `release/Krono-Agent`.
Keep all files together. WebView2 Evergreen Runtime is required for browsing.
Launch `KronoAgent.exe` from that folder. The executable name has no dot separator.

```powershell
npm run typecheck
npm run lint
npm test
npm run test:native
```

Agent unit tests run on Windows/macOS/Linux. Native UI testing uses Windows UI
Automation against an isolated local account/provider fixture, real SQLite,
DPAPI, PowerShell PTY and WebView2. This does not establish live Supabase or
provider acceptance. macOS/Linux desktop interfaces are not implemented.

Existing local account databases and account-service.json remain under
`%APPDATA%/krono-agent`. The native app uses its own Windows DPAPI-protected
session and provider-key files; sign in and configure provider keys once in the
new interface. Previous encrypted files are not deleted.

Initial specifications and development used Kiro Crew; owner-authorized native
migration and subsequent fixes are by Codex. MIT license; dependency notices
are included with the runtime bundle.

---

## Account service setup

1. Create a Supabase project and run `supabase/migrations/202611030001_accounts.sql` in its SQL Editor. Verify that row-level security is enabled on the account tables and that the `krono-attachments` bucket is private.
2. Enable the Email provider and email confirmation. Set the Site URL to `https://alontsur1601.github.io/Krono_Agent/`. For public signups, configure an SMTP provider and check its sending limits.
3. Create `%APPDATA%\krono-agent\account-service.json` with the project URL and public publishable key. The JSON properties are `url` and `anonKey`. Keep private keys and database credentials out of this file. Restart Krono after saving. If both `SUPABASE_URL` and `SUPABASE_ANON_KEY` are set, they take precedence.
4. Register in Krono and confirm your email. Keep the app open on the confirmation screen to let it finish signing in. Check Spam if the message does not appear.
5. To generate a branded template, run `node supabase/auth-email.mjs confirmation`. Copy the generated subject and HTML into the matching Supabase email template. Keep the template variables intact.

## Workspace setup

- In Settings, choose a model provider, save its API key locally, test the connection, and select a model. For Ollama, LM Studio, or an OpenAI-compatible server, start the server and enter its base URL including `/v1`. Enable tool calling only for models that support it.
- In Chat mode, the agent has no tools. Ask permissions request approval for changes and commands. Project permissions restrict file tools to the project. Full access runs tools with your Windows account permissions.
- Use **Account sync** to synchronize regular chats, projects, memories, appearance, and game progress. Provider credentials, local folders, permissions, and temporary side chats remain on the device.
- Scheduled tasks run while Krono is open and signed in. Create a schedule with a prompt, provider, model, permissions, and start time. The history links to each result.
- You can attach up to four text or code files, with a limit of 64 KB each.
