import type { PersistedClient, Persister } from '@tanstack/react-query-persist-client';
import { storage } from './db';

// react-query persister backed by the platform storage (SQLite on native,
// localStorage on web). Persisting the cache enables offline reads.
const KEY = 'rq-cache';

export const sqlitePersister: Persister = {
  async persistClient(client: PersistedClient) {
    await storage.kvSet(KEY, JSON.stringify(client));
  },
  async restoreClient() {
    const raw = await storage.kvGet(KEY);
    return raw ? (JSON.parse(raw) as PersistedClient) : undefined;
  },
  async removeClient() {
    await storage.kvDel(KEY);
  },
};
