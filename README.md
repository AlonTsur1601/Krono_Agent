# Krono Agent

An independent desktop agent workspace with games to play while tasks run.

**Development status: incomplete. No production release is available yet.**
The target includes 24 games, providers, local inference, projects, plugins,
computer/browser tools, side chats and account synchronization. These are
requirements, not claims that all features already work.

See [audited implementation status](IMPLEMENTATION_STATUS.md) and the
[tracked requirements](.kiro/specs/krono-agent/requirements.md).

## Development

Node.js 20+ and native build tooling may be required for Electron modules.

```powershell
npm ci
npm run build
npm test
npx tsc --noEmit
npm run lint
```

Launch the built desktop application using `npx electron .`.
For Vite development, build the main/preload first, then run `npm run dev`.

Authentication requires a real Supabase project. Follow the
[Supabase setup guide](docs/SUPABASE_SETUP.md), which covers the SQL migration,
private Storage policies, email setup and local account-service.json config.
Environment configuration remains supported. Never use a service-role key.
Missing configuration produces an explicit error.
Provider credentials are entered in app settings after authentication and
stored encrypted using the OS. Live authentication and provider acceptance
require external configuration and remain unverified.

## Verification boundaries

Local tests cover Chat tool omission, Plan mutation rejection, approvals,
tool-result continuation and project path/junction boundaries. An Electron
smoke test confirmed launch, preload and the unconfigured-service error.
Eight existing game components remain under review; sixteen are absent.
No installer, cross-platform runtime, full app, or live integrations have
passed acceptance yet. Dependency audit findings remain to be remediated.

## Development provenance

Initial work and specifications were created using Kiro Crew. The owner
subsequently authorized Codex to review, fix and continue implementation.
The .kiro review log distinguishes the takeover from the original work.

MIT license. Do not redistribute model weights or third-party assets
without their required license notices.
