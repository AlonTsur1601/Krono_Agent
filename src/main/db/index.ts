import Database from 'better-sqlite3'
import { app } from 'electron'
import path from 'path'
import { MIGRATIONS } from '../../db/schema'

export class DatabaseManager {
  private db: Database.Database | null = null
  private currentAccountId: string | null = null

  getDbPath(accountId: string): string {
    const userDataPath = app.getPath('userData')
    return path.join(userDataPath, 'databases', `${accountId}.db`)
  }

  open(accountId: string): Database.Database {
    if (this.db && this.currentAccountId === accountId) {
      return this.db
    }

    if (this.db) {
      this.db.close()
    }

    const dbPath = this.getDbPath(accountId)
    const fs = require('fs')
    const dir = path.dirname(dbPath)
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true })
    }

    this.db = new Database(dbPath)
    this.currentAccountId = accountId

    // Enable WAL mode for better concurrency
    this.db.pragma('journal_mode = WAL')
    
    // Run migrations
    this.runMigrations()

    return this.db
  }

  private runMigrations() {
    if (!this.db) return

    // Create migrations tracking table
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS migrations (
        version INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        applied_at INTEGER NOT NULL
      )
    `)

    const appliedMigrations = this.db
      .prepare('SELECT version FROM migrations')
      .all() as { version: number }[]

    const appliedVersions = new Set(appliedMigrations.map(m => m.version))

    for (const migration of MIGRATIONS) {
      if (!appliedVersions.has(migration.version)) {
        console.log(`Applying migration ${migration.version}: ${migration.name}`)
        this.db.exec(migration.sql)
        this.db
          .prepare('INSERT INTO migrations (version, name, applied_at) VALUES (?, ?, ?)')
          .run(migration.version, migration.name, Date.now())
      }
    }
  }

  close() {
    if (this.db) {
      this.db.close()
      this.db = null
      this.currentAccountId = null
    }
  }

  getDb(): Database.Database {
    if (!this.db) {
      throw new Error('Database not opened. Call open() first.')
    }
    return this.db
  }
}

export const dbManager = new DatabaseManager()
