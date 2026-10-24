import * as SQLite from 'expo-sqlite';
import type { OfflineStorage, OutboxRecord, OutboxStatus, ScanOrderRecord } from './types';

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
        CREATE TABLE IF NOT EXISTS scan_order (
          tenant INTEGER NOT NULL,
          gen INTEGER NOT NULL,
          public_id TEXT NOT NULL,
          order_number TEXT NOT NULL,
          waybill TEXT,
          status TEXT NOT NULL,
          trip_code TEXT,
          trip_id TEXT,
          pick_up INTEGER NOT NULL DEFAULT 0,
          PRIMARY KEY (tenant, gen, public_id)
        );
        CREATE TABLE IF NOT EXISTS scan_piece (
          tenant INTEGER NOT NULL,
          gen INTEGER NOT NULL,
          number TEXT NOT NULL,
          order_id TEXT NOT NULL,
          actions TEXT NOT NULL DEFAULT '',
          missing TEXT,
          PRIMARY KEY (tenant, gen, number, order_id)
        );
        CREATE INDEX IF NOT EXISTS idx_scan_piece_number ON scan_piece(tenant, number);
      `);
      return db;
    })();
  }
  return dbPromise;
}

/** La generación CONFIRMADA de la descarga de una compañía (la que se consulta); la que se está bajando no cuenta hasta que se confirma. */
async function committedGen(db: SQLite.SQLiteDatabase, tenantId: number): Promise<number | null> {
  const row = await db.getFirstAsync<{ value: string }>(`SELECT value FROM kv WHERE key = ?`, `scan.gen.${tenantId}`);
  const n = row ? Number(row.value) : NaN;
  return Number.isFinite(n) ? n : null;
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

  async scanNewGeneration(tenantId) {
    const db = await openDb();
    const gen = Date.now();
    await db.runAsync(`DELETE FROM scan_piece WHERE tenant = ? AND gen = ?`, tenantId, gen);
    await db.runAsync(`DELETE FROM scan_order WHERE tenant = ? AND gen = ?`, tenantId, gen);
    return gen;
  },

  async scanInsertOrders(tenantId, gen, orders) {
    const db = await openDb();
    await db.withTransactionAsync(async () => {
      const insOrder = await db.prepareAsync(
        `INSERT OR REPLACE INTO scan_order (tenant, gen, public_id, order_number, waybill, status, trip_code, trip_id, pick_up) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      );
      const insPiece = await db.prepareAsync(
        `INSERT OR REPLACE INTO scan_piece (tenant, gen, number, order_id, actions, missing) VALUES (?, ?, ?, ?, ?, ?)`,
      );
      try {
        for (const o of orders) {
          await insOrder.executeAsync(tenantId, gen, o.publicId, o.orderNumber, o.waybill, o.statusCode, o.tripCode, o.tripPublicId, o.pickUp ? 1 : 0);
          for (const p of o.pieces) await insPiece.executeAsync(tenantId, gen, p.number, o.publicId, p.actions.join(','), p.missing);
        }
      } finally {
        await insOrder.finalizeAsync();
        await insPiece.finalizeAsync();
      }
    });
  },

  async scanCommit(tenantId, gen) {
    const db = await openDb();
    await db.withTransactionAsync(async () => {
      await db.runAsync(`INSERT INTO kv (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value`, `scan.gen.${tenantId}`, String(gen));
      await db.runAsync(`DELETE FROM scan_piece WHERE tenant = ? AND gen <> ?`, tenantId, gen);
      await db.runAsync(`DELETE FROM scan_order WHERE tenant = ? AND gen <> ?`, tenantId, gen);
    });
  },

  async scanAbort(tenantId, gen) {
    const db = await openDb();
    await db.runAsync(`DELETE FROM scan_piece WHERE tenant = ? AND gen = ?`, tenantId, gen);
    await db.runAsync(`DELETE FROM scan_order WHERE tenant = ? AND gen = ?`, tenantId, gen);
  },

  async scanFindPiece(tenantId, number) {
    const db = await openDb();
    const gen = await committedGen(db, tenantId);
    if (gen == null) return [];
    const cur = { gen };
    const rows = await db.getAllAsync<{ order_id: string }>(`SELECT order_id FROM scan_piece WHERE tenant = ? AND gen = ? AND number = ?`, tenantId, cur.gen, number);
    const result: ScanOrderRecord[] = [];
    for (const r of rows) {
      const o = await db.getFirstAsync<{ public_id: string; order_number: string; waybill: string | null; status: string; trip_code: string | null; trip_id: string | null; pick_up: number }>(
        `SELECT public_id, order_number, waybill, status, trip_code, trip_id, pick_up FROM scan_order WHERE tenant = ? AND gen = ? AND public_id = ?`, tenantId, cur.gen, r.order_id);
      if (!o) continue;
      const ps = await db.getAllAsync<{ number: string; actions: string; missing: string | null }>(`SELECT number, actions, missing FROM scan_piece WHERE tenant = ? AND gen = ? AND order_id = ? ORDER BY number`, tenantId, cur.gen, r.order_id);
      result.push({
        publicId: o.public_id, orderNumber: o.order_number, waybill: o.waybill, statusCode: o.status, tripCode: o.trip_code, tripPublicId: o.trip_id, pickUp: o.pick_up === 1,
        pieces: ps.map((p) => ({ number: p.number, actions: p.actions ? p.actions.split(',') : [], missing: p.missing })),
      });
    }
    return result.map((order) => ({ order }));
  },

  async scanRecord(tenantId, orderPublicId, pieceNumber, action, newStatus) {
    const db = await openDb();
    const gen = await committedGen(db, tenantId);
    if (gen == null) return;
    const cur = { gen };
    await db.withTransactionAsync(async () => {
      const p = await db.getFirstAsync<{ actions: string }>(`SELECT actions FROM scan_piece WHERE tenant = ? AND gen = ? AND number = ? AND order_id = ?`, tenantId, cur.gen, pieceNumber, orderPublicId);
      if (p) {
        const set = new Set(p.actions ? p.actions.split(',') : []);
        set.add(action);
        await db.runAsync(`UPDATE scan_piece SET actions = ?, missing = NULL WHERE tenant = ? AND gen = ? AND number = ? AND order_id = ?`, [...set].join(','), tenantId, cur.gen, pieceNumber, orderPublicId);
      }
      if (newStatus) await db.runAsync(`UPDATE scan_order SET status = ? WHERE tenant = ? AND gen = ? AND public_id = ?`, newStatus, tenantId, cur.gen, orderPublicId);
    });
  },

  async scanOrderCount(tenantId) {
    const db = await openDb();
    const gen = await committedGen(db, tenantId);
    if (gen == null) return 0;
    const cur = { gen };
    const row = await db.getFirstAsync<{ n: number }>(`SELECT COUNT(*) AS n FROM scan_order WHERE tenant = ? AND gen = ?`, tenantId, cur.gen);
    return row?.n ?? 0;
  },

  async scanClear(tenantId) {
    const db = await openDb();
    if (tenantId == null) {
      await db.runAsync(`DELETE FROM scan_piece`);
      await db.runAsync(`DELETE FROM scan_order`);
      await db.runAsync(`DELETE FROM kv WHERE key LIKE 'scan.%'`);
    } else {
      await db.runAsync(`DELETE FROM scan_piece WHERE tenant = ?`, tenantId);
      await db.runAsync(`DELETE FROM scan_order WHERE tenant = ?`, tenantId);
      await db.runAsync(`DELETE FROM kv WHERE key LIKE ?`, `scan.%.${tenantId}`);
    }
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
