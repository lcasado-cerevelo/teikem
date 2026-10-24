import type { OfflineStorage, OutboxRecord, OutboxStatus, ScanOrderRecord } from './types';

// Web storage. expo-sqlite doesn't run in the browser, so on web we keep the
// outbox in memory (web is not the production target — drivers use Android) and
// persist the react-query cache in localStorage. This keeps web from crashing
// and lets you preview screens in Chrome.

const outbox: OutboxRecord[] = [];
// Lote 216: la descarga del escaneo, en memoria (la web no es el destino de producción). Por compañía: generación vigente + las que se están bajando.
const scanGens = new Map<number, Map<number, ScanOrderRecord[]>>();
const scanCommitted = new Map<number, number>();
const currentGen = (tenantId: number): number | null => scanCommitted.get(tenantId) ?? null;

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

  async scanNewGeneration(tenantId) {
    const gens = scanGens.get(tenantId) ?? new Map<number, ScanOrderRecord[]>();
    scanGens.set(tenantId, gens);
    const gen = Date.now();
    gens.set(gen, []);
    return gen;
  },

  async scanInsertOrders(tenantId, gen, orders) {
    scanGens.get(tenantId)?.get(gen)?.push(...orders.map((o) => ({ ...o, pieces: o.pieces.map((p) => ({ ...p, actions: [...p.actions] })) })));
  },

  async scanCommit(tenantId, gen) {
    const gens = scanGens.get(tenantId);
    if (!gens) return;
    scanCommitted.set(tenantId, gen);
    for (const g of [...gens.keys()]) if (g !== gen) gens.delete(g);
  },

  async scanAbort(tenantId, gen) {
    scanGens.get(tenantId)?.delete(gen);
  },

  async scanFindPiece(tenantId, number) {
    const gen = currentGen(tenantId);
    if (gen == null) return [];
    const orders = scanGens.get(tenantId)?.get(gen) ?? [];
    return orders.filter((o) => o.pieces.some((p) => p.number === number)).map((order) => ({ order: { ...order, pieces: order.pieces.map((p) => ({ ...p, actions: [...p.actions] })) } }));
  },

  async scanRecord(tenantId, orderPublicId, pieceNumber, action, newStatus) {
    const gen = currentGen(tenantId);
    if (gen == null) return;
    const order = scanGens.get(tenantId)?.get(gen)?.find((o) => o.publicId === orderPublicId);
    if (!order) return;
    const piece = order.pieces.find((p) => p.number === pieceNumber);
    if (piece && !piece.actions.includes(action)) piece.actions.push(action);
    if (piece) piece.missing = null;
    if (newStatus) order.statusCode = newStatus;
  },

  async scanOrderCount(tenantId) {
    const gen = currentGen(tenantId);
    return gen == null ? 0 : (scanGens.get(tenantId)?.get(gen)?.length ?? 0);
  },

  async scanClear(tenantId) {
    if (tenantId == null) { scanGens.clear(); scanCommitted.clear(); }
    else { scanGens.delete(tenantId); scanCommitted.delete(tenantId); }
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
