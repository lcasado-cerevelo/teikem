import type { OfflineStorage, OutboxRecord, OutboxStatus } from './types';

// Web storage. expo-sqlite doesn't run in the browser, so on web we keep the
// outbox in memory (web is not the production target — drivers use Android) and
// persist the react-query cache in localStorage. This keeps web from crashing
// and lets you preview screens in Chrome.

const outbox: OutboxRecord[] = [];

function safeLocalStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null;
  } catch {
    return null;
  }
}

export const storage: OfflineStorage = {
  async init() {
    /* no-op on web */
  },

  async insertOutbox(rec) {
    outbox.push({ ...rec });
  },

  async getById(id) {
    const item = outbox.find((o) => o.id === id);
    return item ? { ...item } : null;
  },

  async listPending(limit) {
    return outbox
      .filter((o) => o.status === 'pending')
      .sort((a, b) => a.createdAt - b.createdAt)
      .slice(0, limit)
      .map((o) => ({ ...o }));
  },

  async countByStatus(status: OutboxStatus) {
    return outbox.filter((o) => o.status === status).length;
  },

  async updateOutbox(id, fields) {
    const item = outbox.find((o) => o.id === id);
    if (!item) return;
    if (fields.status !== undefined) item.status = fields.status;
    if (fields.retryCount !== undefined) item.retryCount = fields.retryCount;
    if (fields.lastError !== undefined) item.lastError = fields.lastError;
    item.updatedAt = fields.updatedAt ?? Date.now();
  },

  async clearOutbox() {
    outbox.length = 0;
  },

  async kvGet(key) {
    return safeLocalStorage()?.getItem(key) ?? null;
  },

  async kvSet(key, value) {
    safeLocalStorage()?.setItem(key, value);
  },

  async kvDel(key) {
    safeLocalStorage()?.removeItem(key);
  },
};
