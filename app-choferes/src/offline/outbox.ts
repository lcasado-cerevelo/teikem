import * as Crypto from 'expo-crypto';
import { storage } from './db';
import type { OutboxMethod, OutboxRecord, OutboxStatus } from './types';

export type { OutboxRecord as OutboxItem, OutboxStatus, OutboxMethod };

export interface EnqueueInput {
  kind: string;
  endpoint: string;
  method?: OutboxMethod;
  payload: unknown;
  isMultipart?: boolean;
}

// Add a write to the durable queue. Returns the UUID (idempotency key).
export async function enqueue(input: EnqueueInput): Promise<string> {
  const id = Crypto.randomUUID();
  const now = Date.now();
  await storage.insertOutbox({
    id,
    kind: input.kind,
    endpoint: input.endpoint,
    method: input.method ?? 'POST',
    payload: JSON.stringify(input.payload),
    isMultipart: input.isMultipart ?? false,
    status: 'pending',
    retryCount: 0,
    lastError: null,
    createdAt: now,
    updatedAt: now,
  });
  return id;
}

export function listPending(limit = 25): Promise<OutboxRecord[]> {
  return storage.listPending(limit);
}

// Final status of one queued write (to tell "sent" vs "still queued").
export function getStatus(id: string): Promise<OutboxStatus | null> {
  return storage.getById(id).then((r) => r?.status ?? null);
}

// Full record of one queued write (status + lastError) for result feedback.
export function getItem(id: string): Promise<OutboxRecord | null> {
  return storage.getById(id);
}

export function countByStatus(status: OutboxStatus): Promise<number> {
  return storage.countByStatus(status);
}

export function markDone(id: string): Promise<void> {
  return storage.updateOutbox(id, { status: 'done', updatedAt: Date.now() });
}

// Transient failure: stays 'pending' so it retries later.
export function bumpRetry(id: string, error: string, retryCount: number): Promise<void> {
  return storage.updateOutbox(id, { retryCount: retryCount + 1, lastError: error, updatedAt: Date.now() });
}

// Permanent failure (4xx): won't auto-retry.
export function markFailed(id: string, error: string): Promise<void> {
  return storage.updateOutbox(id, { status: 'failed', lastError: error, updatedAt: Date.now() });
}

export function requeue(id: string): Promise<void> {
  return storage.updateOutbox(id, { status: 'pending', updatedAt: Date.now() });
}

// Shipments closed locally (delivery or exception) whose write hasn't reached
// the server yet. Server-fetched "open shipments" lists must EXCLUDE these:
// until the outbox drains, the server still (wrongly) reports them as open,
// and letting that answer through re-adds a completed order to the list.
export async function pendingClosedShipmentIds(): Promise<Set<number>> {
  const items = await storage.listPending(200);
  const ids = new Set<number>();
  for (const item of items) {
    if (item.kind !== 'dispatch' && item.kind !== 'exception') continue;
    try {
      const payload = JSON.parse(item.payload) as { fields?: Record<string, unknown> };
      const sid = Number(payload.fields?.ShipmentID);
      if (Number.isFinite(sid)) ids.add(sid);
    } catch {
      // malformed payload — never block the list over it
    }
  }
  return ids;
}
