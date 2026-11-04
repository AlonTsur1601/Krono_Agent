# Krono Agent — repository structure

- native/Krono.Agent: C#/XAML Windows UI, accounts/database, browser, terminal,
  native game rendering and native Markdown.
- src/agent: provider adapters, agent execution and file tools, with unit tests.
- src/sidecar: private stdio worker and PTY lifetime management.
- src/db/schema.sql: authoritative local SQL schema; native build copies it.
- src/tests: cloud SQL ownership-policy tests.
- scripts: native build/publish/launch and real Windows UI integration fixture.
- assets/logo: authoritative SVG mark; the required native ICO lives in the Windows assets.
- supabase/migrations: cloud account schema and private attachment policies.
- docs/index.html: public project page and email-confirmation landing state.
- .kiro/specs: requirements, original design/tasks and honest takeover reviews.

Build outputs, model files, local credentials, databases and test artifacts are
excluded from git. Keep setup guidance in README.md rather than duplicate guides.
