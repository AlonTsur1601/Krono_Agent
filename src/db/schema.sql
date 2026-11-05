-- Accounts
CREATE TABLE IF NOT EXISTS accounts (
  id TEXT PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  created_at INTEGER NOT NULL
);

-- Chats
CREATE TABLE IF NOT EXISTS chats (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  project_id TEXT,
  name TEXT,
  pinned INTEGER NOT NULL DEFAULT 0,
  archived INTEGER NOT NULL DEFAULT 0,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);

-- Messages
CREATE TABLE IF NOT EXISTS messages (
  id TEXT PRIMARY KEY,
  chat_id TEXT NOT NULL,
  role TEXT NOT NULL CHECK(role IN ('user', 'assistant', 'system')),
  content TEXT,
  created_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (chat_id) REFERENCES chats(id)
);

-- Projects
CREATE TABLE IF NOT EXISTS projects (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  name TEXT NOT NULL,
  icon TEXT,
  color TEXT,
  description TEXT,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);

-- Project Directories (device-specific)
CREATE TABLE IF NOT EXISTS project_directories (
  id TEXT PRIMARY KEY,
  project_id TEXT NOT NULL,
  device_id TEXT NOT NULL,
  path TEXT NOT NULL,
  FOREIGN KEY (project_id) REFERENCES projects(id)
);

-- Memories
CREATE TABLE IF NOT EXISTS memories (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  project_id TEXT,
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  deleted_at INTEGER,
  FOREIGN KEY (account_id) REFERENCES accounts(id),
  FOREIGN KEY (project_id) REFERENCES projects(id)
);

-- Sync Outbox
CREATE TABLE IF NOT EXISTS outbox (
  id TEXT PRIMARY KEY,
  operation TEXT NOT NULL CHECK(operation IN ('insert', 'update', 'delete')),
  table_name TEXT NOT NULL,
  record_id TEXT NOT NULL,
  payload TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  retries INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS sync_checkpoints (
  table_name TEXT NOT NULL, record_id TEXT NOT NULL, revision INTEGER NOT NULL,
  payload TEXT NOT NULL, PRIMARY KEY(table_name, record_id)
);

CREATE TABLE IF NOT EXISTS sync_conflicts (
  table_name TEXT NOT NULL, record_id TEXT NOT NULL, cloud_revision INTEGER NOT NULL,
  cloud_payload TEXT, local_payload TEXT NOT NULL, detected_at INTEGER NOT NULL,
  PRIMARY KEY(table_name, record_id)
);

CREATE TABLE IF NOT EXISTS scheduled_tasks (
  id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id),
  name TEXT NOT NULL, prompt TEXT NOT NULL, provider TEXT NOT NULL, model TEXT NOT NULL,
  mode TEXT NOT NULL, permissions TEXT NOT NULL, project_id TEXT,
  recurrence TEXT NOT NULL, time_zone TEXT NOT NULL, interval_minutes INTEGER NOT NULL,
  clock_minutes INTEGER NOT NULL, next_run INTEGER NOT NULL, state TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS scheduled_runs (
  id TEXT PRIMARY KEY, task_id TEXT NOT NULL REFERENCES scheduled_tasks(id),
  due_at INTEGER NOT NULL, started_at INTEGER NOT NULL, heartbeat_at INTEGER NOT NULL,
  finished_at INTEGER, state TEXT NOT NULL, chat_id TEXT REFERENCES chats(id), error TEXT,
  UNIQUE(task_id,due_at)
);
CREATE INDEX IF NOT EXISTS scheduled_tasks_due ON scheduled_tasks(account_id,state,next_run);

-- Game Scores
CREATE TABLE IF NOT EXISTS game_scores (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  game_id TEXT NOT NULL,
  score INTEGER NOT NULL,
  achieved_at INTEGER NOT NULL,
  synced INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);

-- Game Saves
CREATE TABLE IF NOT EXISTS game_saves (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  game_id TEXT NOT NULL,
  state TEXT NOT NULL,
  saved_at INTEGER NOT NULL,
  synced INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (account_id) REFERENCES accounts(id)
);

-- Settings
CREATE TABLE IF NOT EXISTS settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

-- Indexes
-- Temporary local branches are excluded from normal chat lists and future sync.
CREATE TABLE IF NOT EXISTS side_chats (
  chat_id TEXT PRIMARY KEY REFERENCES chats(id) ON DELETE CASCADE,
  parent_chat_id TEXT NOT NULL REFERENCES chats(id),
  context TEXT NOT NULL DEFAULT '{}',
  expires_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_side_chats_expiry ON side_chats(expires_at);
CREATE TABLE IF NOT EXISTS side_transfers (
  id TEXT PRIMARY KEY,
  parent_chat_id TEXT NOT NULL REFERENCES chats(id),
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_chats_account ON chats(account_id);
CREATE INDEX IF NOT EXISTS idx_messages_chat ON messages(chat_id);
CREATE INDEX IF NOT EXISTS idx_projects_account ON projects(account_id);
CREATE INDEX IF NOT EXISTS idx_memories_account ON memories(account_id);
CREATE INDEX IF NOT EXISTS idx_outbox_created ON outbox(created_at);
CREATE INDEX IF NOT EXISTS idx_game_scores_account ON game_scores(account_id, game_id);
