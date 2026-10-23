import * as SQLite from 'expo-sqlite';
import type { OfflineStorage, OutboxRecord, OutboxStatus } from './types';

// Native (Android/iOS) storage backed by expo-sqlite.
// Metro resolves db.web.ts on web, so this file (and expo-sqlite) never loads there.

let dbPromise: Promise<SQLite.SQLiteDatabase> | null = null;

function openDb(): Promise<SQLite.SQLiteDatabase> {
  if (!dbPromise) {
    dbPromise = (async () => {
      const db = await SQLite.openDatabaseAsync('colavora.db');
      await db.execAsync(`
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS outbox (
          id TEXT PRIMARY KEY NOT NULL,
          kind TEXT NOT NULL,
          endpoint TEXT NOT NULL,
          method TEXT NOT NULL,
          payload TEXT NOT NULL,
          is_multipart INTEGER NOT NULL DEFAULT 0,
          status TEXT NOT NULL DEFAULT 'pending',
          retry_count INTEGER NOT NULL DEFAULT 0,
          last_error TEXT,
          created_at INTEGER NOT NULL,
          updated_at INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_outbox_status_created ON outbox(status, created_at);
        CREATE TABLE IF NOT EXISTS kv (
          key TEXT PRIMARY KEY NOT NULL,
          value TEXT NOT NULL
        );
      `);
      return db;
    })();
  }
  return dbPromise;
}

interface Row {
  id: string;
  kind: string;
  endpoint: string;
  method: OutboxRecord['method'];
  payload: string;
  is_multipart: number;
  status: OutboxStatus;
  retry_count: number;
  last_error: string | null;
  created_at: number;
  updated_at: number;
}

function mapRow(r: Row): OutboxRecord {
  return {
    id: r.id,
    kind: r.kind,
    endpoint: r.endpoint,
    method: r.method,
    payload: r.payload,
    isMultipart: r.is_multipart === 1,
    status: r.status,
    retryCount: r.retry_count,
    lastError: r.last_error,
    createdAt: r.created_at,
    updatedAt: r.updated_at,
  };
}

export const storage: OfflineStorage = {
  async init() {
    await openDb();
  },

  async insertOutbox(rec) {
    const db = await openDb();
    await db.runAsync(
      `INSERT INTO outbox (id, kind, endpoint, method, payload, is_multipart, status, retry_count, last_error, created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      rec.id,
      rec.kind,
      rec.endpoint,
      rec.method,
      rec.payload,
      rec.isMultipart ? 1 : 0,
      rec.status,
      rec.retryCount,
      rec.lastError,
      rec.createdAt,
      rec.updatedAt,
    );
  },

  async getById(id) {
    const db = await openDb();
    const row = await db.getFirstAsync<Row>(`SELECT * FROM outbox WHERE id = ?`, id);
    return row ? mapRow(row) : null;
  },

  async listPending(limit) {
    const db = await openDb();
    const rows = await db.getAllAsync<Row>(
      `SELECT * FROM outbox WHERE status = 'pending' ORDER BY created_at ASC LIMIT ?`,
      limit,
    );
    return rows.map(mapRow);
  },

  async countByStatus(status) {
    const db = await openDb();
    const row = await db.getFirstAsync<{ n: number }>(
      `SELECT COUNT(*) AS n FROM outbox WHERE status = ?`,
      status,
    );
    return row?.n ?? 0;
  },

  async updateOutbox(id, fields) {
    const db = await openDb();
    const sets: string[] = [];
    const values: (string | number | null)[] = [];
    if (fields.status !== undefined) {
      sets.push('status = ?');
      values.push(fields.status);
    }
    if (fields.retryCount !== undefined) {
      sets.push('retry_count = ?');
      values.push(fields.retryCount);
    }
    if (fields.lastError !== undefined) {
      sets.push('last_error = ?');
      values.push(fields.lastError);
    }
    sets.push('updated_at = ?');
    values.push(fields.updatedAt ?? Date.now());
    values.push(id);
    await db.runAsync(`UPDATE outbox SET ${sets.join(', ')} WHERE id = ?`, ...values);
  },

  async clearOutbox() {
    const db = await openDb();
    await db.runAsync(`DELETE FROM outbox`);
  },

  async kvGet(key) {
    const db = await openDb();
    const row = await db.getFirstAsync<{ value: string }>(`SELECT value FROM kv WHERE key = ?`, key);
    return row?.value ?? null;
  },

  async kvSet(key, value) {
    const db = await openDb();
    await db.runAsync(
      `INSERT INTO kv (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value`,
      key,
      value,
    );
  },

  async kvDel(key) {
    const db = await openDb();
    await db.runAsync(`DELETE FROM kv WHERE key = ?`, key);
  },
};
