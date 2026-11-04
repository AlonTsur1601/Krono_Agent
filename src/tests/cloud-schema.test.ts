import { PGlite } from '@electric-sql/pglite'
import { readFileSync } from 'fs'
import { describe, it, expect } from 'vitest'

describe('cloud SQL ownership policies (embedded PostgreSQL)', () => {
  it('isolates two users, rejects spoofing and cross-account references, and protects attachments', async () => {
    const db = new PGlite()
    const a = '11111111-1111-4111-8111-111111111111'
    const b = '22222222-2222-4222-8222-222222222222'
    try {
      // Supabase-provided schemas/functions are minimal test doubles; policy SQL is production SQL.
      await db.exec(`
        create role anon; create role authenticated;
        create schema auth; create schema storage;
        create table auth.users(id uuid primary key);
        insert into auth.users values ('${a}'),('${b}');
        create function auth.uid() returns uuid language sql stable as
          $$ select nullif(current_setting('request.jwt.claim.sub', true),'')::uuid $$;
        create table storage.buckets(id text primary key,name text,public boolean,file_size_limit bigint);
        create table storage.objects(id text primary key,bucket_id text,name text);
        alter table storage.objects enable row level security;
        create function storage.foldername(name text) returns text[] language sql immutable as
          $$ select (string_to_array(name,'/'))[1:array_length(string_to_array(name,'/'),1)-1] $$;
        grant usage on schema public,auth,storage to authenticated,anon;
        grant select,insert,update,delete on storage.objects to authenticated;
      `)
      await db.exec(readFileSync('supabase/migrations/202611030001_accounts.sql', 'utf8'))
      await db.exec(`set role authenticated; select set_config('request.jwt.claim.sub','${a}',false);`)
      await db.exec(`insert into public.chats(id,account_id,name,created_at,updated_at) values ('a-chat','${a}','A',1,1)`)
      await db.exec(`insert into storage.objects values ('a-file','krono-attachments','${a}/file.txt')`)
      await db.exec(`select set_config('request.jwt.claim.sub','${b}',false)`)
      expect((await db.query('select * from public.chats')).rows).toHaveLength(0)
      expect((await db.query('select * from storage.objects')).rows).toHaveLength(0)
      await expect(db.exec(`insert into public.chats(id,account_id,created_at,updated_at) values ('spoof','${a}',1,1)`)).rejects.toThrow()
      await expect(db.exec(`insert into public.messages(id,account_id,chat_id,role,content,created_at) values ('bad-ref','${b}','a-chat','user','test',1)`)).rejects.toThrow()
      await expect(db.exec(`insert into storage.objects values ('spoof','krono-attachments','${a}/spoof.txt')`)).rejects.toThrow()
      await db.exec(`insert into public.chats(id,account_id,created_at,updated_at) values ('b-chat','${b}',1,1)`)
      await expect(db.exec(`update public.chats set account_id='${a}' where id='b-chat'`)).rejects.toThrow()
      await db.exec('set role anon')
      await expect(db.query('select * from public.chats')).rejects.toThrow()
    } finally { await db.close() }
  }, 30000)
})
