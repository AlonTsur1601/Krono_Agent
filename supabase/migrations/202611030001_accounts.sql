-- Krono account-owned cloud data. Apply to a NEW Supabase project.
-- Local paths, credentials and browser profiles must never enter these tables.
begin;

create table public.projects (
  id text not null, account_id uuid not null references auth.users(id) on delete cascade,
  name text not null, icon text, color text, description text,
  created_at bigint not null, updated_at bigint not null, version integer not null default 1,
  deleted_at bigint, primary key(account_id, id), check(version > 0)
);
create table public.chats (
  id text not null, account_id uuid not null references auth.users(id) on delete cascade,
  project_id text, name text not null default 'New Chat',
  pinned boolean not null default false, archived boolean not null default false,
  created_at bigint not null, updated_at bigint not null, version integer not null default 1,
  deleted_at bigint, primary key(account_id, id), check(version > 0),
  foreign key(account_id, project_id) references public.projects(account_id, id)
);
create table public.messages (
  id text not null, account_id uuid not null references auth.users(id) on delete cascade,
  chat_id text not null, role text not null check(role in ('user','assistant','system')),
  content text not null, created_at bigint not null, version integer not null default 1,
  deleted_at bigint, primary key(account_id, id), check(version > 0),
  foreign key(account_id, chat_id) references public.chats(account_id, id) on delete cascade
);
create table public.memories (
  id text not null, account_id uuid not null references auth.users(id) on delete cascade,
  project_id text, content text not null, created_at bigint not null,
  updated_at bigint not null, version integer not null default 1, deleted_at bigint,
  primary key(account_id, id), check(version > 0),
  foreign key(account_id, project_id) references public.projects(account_id, id)
);
create table public.account_settings (
  account_id uuid not null references auth.users(id) on delete cascade,
  key text not null, value jsonb not null, version integer not null default 1,
  updated_at bigint not null, primary key(account_id,key), check(version > 0)
);
create table public.game_scores (
  id text not null, account_id uuid not null references auth.users(id) on delete cascade,
  game_id text not null, score double precision not null, achieved_at bigint not null,
  primary key(account_id,id)
);
create table public.game_saves (
  account_id uuid not null references auth.users(id) on delete cascade,
  game_id text not null, state jsonb not null, saved_at bigint not null,
  version integer not null default 1, primary key(account_id,game_id), check(version > 0)
);

alter table public.projects enable row level security;
alter table public.projects force row level security;
revoke all on public.projects from anon;
grant select, insert, update, delete on public.projects to authenticated;
create policy account_owner on public.projects for all to authenticated
using (account_id = (select auth.uid()))
with check (account_id = (select auth.uid()));

alter table public.chats enable row level security;
alter table public.chats force row level security;
revoke all on public.chats from anon;
grant select, insert, update, delete on public.chats to authenticated;
create policy account_owner on public.chats for all to authenticated
using (account_id = (select auth.uid()))
with check (account_id = (select auth.uid()));

alter table public.messages enable row level security;
alter table public.messages force row level security;
revoke all on public.messages from anon;
grant select, insert, update, delete on public.messages to authenticated;
create policy account_owner on public.messages for all to authenticated
using (account_id = (select auth.uid()))
with check (account_id = (select auth.uid()));

alter table public.memories enable row level security;
alter table public.memories force row level security;
revoke all on public.memories from anon;
grant select, insert, update, delete on public.memories to authenticated;
create policy account_owner on public.memories for all to authenticated
using (account_id = (select auth.uid()))
with check (account_id = (select auth.uid()));

alter table public.account_settings enable row level security;
alter table public.account_settings force row level security;
revoke all on public.account_settings from anon;
grant select, insert, update, delete on public.account_settings to authenticated;
create policy account_owner on public.account_settings for all to authenticated
using (account_id = (select auth.uid()))
with check (account_id = (select auth.uid()));

alter table public.game_scores enable row level security;
alter table public.game_scores force row level security;
revoke all on public.game_scores from anon;
grant select, insert, update, delete on public.game_scores to authenticated;
create policy account_owner on public.game_scores for all to authenticated
using (account_id = (select auth.uid()))
with check (account_id = (select auth.uid()));

alter table public.game_saves enable row level security;
alter table public.game_saves force row level security;
revoke all on public.game_saves from anon;
grant select, insert, update, delete on public.game_saves to authenticated;
create policy account_owner on public.game_saves for all to authenticated
using (account_id = (select auth.uid()))
with check (account_id = (select auth.uid()));

-- Private attachments: object names must begin with the authenticated user UUID.
insert into storage.buckets(id,name,public,file_size_limit)
values ('krono-attachments','krono-attachments',false,26214400);
create policy krono_attachment_select on storage.objects for select to authenticated
using (bucket_id = 'krono-attachments' and (storage.foldername(name))[1] = (select auth.uid()::text));
create policy krono_attachment_insert on storage.objects for insert to authenticated
with check (bucket_id = 'krono-attachments' and (storage.foldername(name))[1] = (select auth.uid()::text));
create policy krono_attachment_update on storage.objects for update to authenticated
using (bucket_id = 'krono-attachments' and (storage.foldername(name))[1] = (select auth.uid()::text))
with check (bucket_id = 'krono-attachments' and (storage.foldername(name))[1] = (select auth.uid()::text));
create policy krono_attachment_delete on storage.objects for delete to authenticated
using (bucket_id = 'krono-attachments' and (storage.foldername(name))[1] = (select auth.uid()::text));
create index messages_chat_idx on public.messages(account_id,chat_id,created_at);
create index chats_recent_idx on public.chats(account_id,updated_at);
commit;
