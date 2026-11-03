# Supabase setup for Krono Agent

Status: migration and password-auth wiring are prepared. No cloud project has
been created or deployed by this repository. This guide does not claim that
sync, OAuth, password recovery, offline sessions or attachment upload UI are
already complete.

## 1. Create the service

1. Sign in at https://supabase.com/dashboard and create a new project.
2. Choose the intended region and a database password saved in your password
   manager. The desktop app does not need that database password.
3. Wait for project provisioning.
4. In the SQL Editor, run the COMPLETE file
   `supabase/migrations/202611030001_accounts.sql` from this repository.
   It targets a new project and runs in a transaction. Run it once; do not
   re-run after a successful commit or delete existing tables to silence errors.
5. Confirm that seven public tables exist: projects, chats, messages,
   memories, account_settings, game_scores, game_saves. Confirm RLS is enabled.
6. Confirm Storage contains `krono-attachments` with Public OFF. It accepts
   files up to 25 MiB. This is a per-file limit, not unlimited free storage.

The migration uses authenticated ownership policies and composite foreign
keys so a message cannot point to another user's chat. Storage object names
must start with the logged-in user's UUID, for example `<user-id>/<file-id>`.
The app does not upload project source directories, API credentials or
browser profiles.

## 2. Enable email authentication

In Authentication, enable the Email provider. Keep email confirmation enabled
for public use. Configure Site URL to an HTTPS page you control for email
confirmation. Krono currently supports password sign-in/sign-up; after
confirmation, return to the app and sign in. It does not yet implement a
native email callback or OAuth flow.

For public distribution, configure production SMTP and review Supabase's
email quotas. The default development mail service is not production delivery.

## 3. Configure the desktop app

Use the project's URL and publishable key (or legacy anon key) from the
project's connection/API settings. Never use a service-role key, `sb_secret_`
key, database password or connection string in Krono. The application rejects
recognized secret/service-role key formats.

For a normal Windows installation, place this file at:

`%APPDATA%/krono-agent/account-service.json`

Electron's userData directory is authoritative. If the installed application
uses a different package name/userData directory, use that directory instead.

File format (replace placeholders locally; never paste real credentials into
an agent conversation):

```json
{
  "url": "https://YOUR-PROJECT.supabase.co",
  "anonKey": "YOUR-PUBLISHABLE-OR-ANON-KEY"
}
```

These are client configuration values, not administrative credentials.
Do not commit your configured file. The application reads it in the main
process, not the renderer. Fully quit and restart Krono after changing it.

Alternatively set both SUPABASE_URL and SUPABASE_ANON_KEY in the process that
launches Krono. Both environment values take precedence over the JSON file.
The app does not automatically load `.env` files. Remote URLs require HTTPS;
HTTP is allowed only for loopback local development.

## 4. Verify the connection

1. Launch Krono and register a test email/password account.
2. Confirm the email and sign in. A confirmation-required message before
   confirmation is expected, not a successful login.
3. Verify the account in Supabase Authentication → Users.
4. Sign out. Attempt an incorrect password: it must be rejected.
5. Sign in again with the correct password.

Session restoration is not implemented yet, so restart currently requires
sign-in. Local data uses the authenticated Supabase user ID.

## 5. Cloud acceptance before release

The SQL has been executed in embedded PostgreSQL tests using minimal auth
and Storage schema test doubles. That proves SQL/policy behavior locally,
not a live Supabase deployment.

After creating the project, test with two real accounts and authenticated
user clients (never the SQL Editor's postgres/service role):
- A can insert/read its own project/chat/message.
- B cannot read/update/delete A's rows or insert rows owned by A.
- B cannot point a message to A's chat.
- Anonymous clients cannot read any account table.
- A can upload/download under its own UUID in the private bucket.
- B cannot list/download/overwrite A's objects or upload under A's UUID.
- Signed-out access to private objects fails.

The current application has not yet wired cloud record sync or attachment
upload. Creating these tables alone does not enable those product features.
Do not mark live authentication, two-device sync, OAuth or attachments
verified until each actual application flow has been exercised.

## Troubleshooting

- Account service not configured: check file location or set BOTH env values.
- Invalid config: use exactly url and anonKey, valid JSON and HTTPS.
- Invalid API key: use the same project's publishable/anon key and URL.
- Confirmation required: confirm email, then sign in instead of registering again.
- Email not delivered: check SMTP, quotas, spam and Auth logs.
- RLS rejection for your own data: account_id must equal the authenticated
  user's UUID. Do not disable RLS as a fix.
- Missing table/bucket: verify migration succeeded in the intended project.

Official references:
- https://supabase.com/docs/guides/database/postgres/row-level-security
- https://supabase.com/docs/guides/storage/security/access-control
- https://supabase.com/docs/guides/auth/auth-email
