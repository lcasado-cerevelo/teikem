import { AxiosError } from 'axios';
import { api } from '@config/axios';
import { bumpRetry, listPending, markDone, markFailed, type OutboxItem } from './outbox';

let active: Promise<void> | null = null;
let followUp: Promise<void> | null = null;

// Drain the queue. FIFO; stops at the first transient failure to keep order.
//
// Concurrency contract: a caller must get a run that STARTS at or after its
// call. Just sharing the in-flight promise isn't enough — an in-flight run
// already took its snapshot of pending items, so an item enqueued a moment
// later (e.g. "save" tapped right after returning from the camera, while the
// app-foreground catch-up drain is still running) would be silently skipped
// and sit queued until the next reconnect. So: if a run is active, coalesce
// all waiters onto ONE follow-up run that begins when the active one ends.
export function drainOutbox(): Promise<void> {
  if (!active) {
    active = runDrain().finally(() => {
      active = null;
    });
    return active;
  }
  if (!followUp) {
    followUp = active
      .catch(() => undefined)
      .then(() => {
        followUp = null;
        return drainOutbox();
      });
  }
  return followUp;
}

async function runDrain(): Promise<void> {
  const items = await listPending(25);
  for (const item of items) {
    try {
      await sendItem(item);
      await markDone(item.id);
    } catch (err) {
      const status = (err as AxiosError).response?.status;
      const permanent =
        typeof status === 'number' && status >= 400 && status < 500 && status !== 408 && status !== 429;
      if (permanent) {
        await markFailed(item.id, describeError(err));
        continue;
      }
      await bumpRetry(item.id, describeError(err), item.retryCount);
      break;
    }
  }
}

async function sendItem(item: OutboxItem): Promise<void> {
  const headers: Record<string, string> = { 'Idempotency-Key': item.id };
  const payload = JSON.parse(item.payload) as { tenantId?: number; body?: unknown };
  await api.request({
    url: item.endpoint,
    method: item.method,
    data: payload.body,
    headers,
    tenantId: payload.tenantId,
    // La firma y las fotos viajan en este cuerpo; con un enlace móvil lento el tiempo normal (20 s) cortaría la subida y se confundiría con «sin señal».
    timeout: 120_000,
  });
}

function describeError(err: unknown): string {
  const ax = err as AxiosError<{ title?: string; error_description?: string; message?: string }>;
  if (ax.response) {
    const body = ax.response.data;
    return `HTTP ${ax.response.status}: ${body?.title ?? body?.error_description ?? body?.message ?? ax.message}`;
  }
  return ax.message ?? 'Network error';
}
