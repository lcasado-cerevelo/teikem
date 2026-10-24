// Platform-neutral storage contract. Native uses SQLite (db.ts); web uses an
// in-memory queue + localStorage (db.web.ts). Metro picks the right file.

export type OutboxStatus = 'pending' | 'done' | 'failed';
export type OutboxMethod = 'POST' | 'PUT' | 'DELETE';

export interface OutboxRecord {
  id: string;
  kind: string;
  endpoint: string;
  method: OutboxMethod;
  payload: string; // JSON string
  isMultipart: boolean;
  status: OutboxStatus;
  retryCount: number;
  lastError: string | null;
  createdAt: number;
  updatedAt: number;
}

export interface OfflineStorage {
  init(): Promise<void>;

  // Outbox (durable write queue)
  insertOutbox(rec: OutboxRecord): Promise<void>;
  getById(id: string): Promise<OutboxRecord | null>;
  listPending(limit: number): Promise<OutboxRecord[]>;
  countByStatus(status: OutboxStatus): Promise<number>;
  updateOutbox(
    id: string,
    fields: Partial<Pick<OutboxRecord, 'status' | 'retryCount' | 'lastError' | 'updatedAt'>>,
  ): Promise<void>;
  clearOutbox(): Promise<void>;

  // Key-value (react-query persisted cache)
  kvGet(key: string): Promise<string | null>;
  kvSet(key: string, value: string): Promise<void>;
  kvDel(key: string): Promise<void>;
}
