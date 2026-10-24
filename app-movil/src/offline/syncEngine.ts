import { AxiosError } from 'axios';
import { api } from '@config/axios';
import { bumpRetry, listPending, markDone, markFailed, type OutboxItem } from './outbox';
import { addProblem } from './scanProblems';
import { isServerProblem } from '@utils/scanRules';

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

const BATCH = 25;

async function runDrain(): Promise<void> {
  // Lote 216: con cientos de escaneos en cola se sigue de 25 en 25 hasta vaciarla (o hasta el primer fallo de señal).
  for (;;) {
    const items = await listPending(BATCH);
    if (items.length === 0) return;
    const stop = await drainBatch(items);
    if (stop || items.length < BATCH) return;
  }
}

/** Manda un lote; devuelve true si hay que parar (fallo transitorio: se conserva el orden). */
async function drainBatch(items: OutboxItem[]): Promise<boolean> {
  for (const item of items) {
    try {
      const data = await sendItem(item);
      await markDone(item.id);
      if (item.kind === 'scan') await noteScanResult(item, data);
    } catch (err) {
      const status = (err as AxiosError).response?.status;
      const permanent =
        typeof status === 'number' && status >= 400 && status < 500 && status !== 408 && status !== 429;
      if (permanent) {
        await markFailed(item.id, describeError(err));
        if (item.kind === 'scan') await noteScanFailure(item, describeError(err));
        continue;
      }
      await bumpRetry(item.id, describeError(err), item.retryCount);
      return true;
    }
  }
  return false;
}

/** Lote 216: lo que el servidor contestó a un escaneo sin señal; si no quedó hecho, va a la lista «Con problema» para que el usuario lo vea. */
async function noteScanResult(item: OutboxItem, data: unknown): Promise<void> {
  const r = data as { outcome?: string; message?: string; orderNumber?: string } | undefined;
  if (!isServerProblem(r?.outcome)) return;
  const p = JSON.parse(item.payload) as { tenantId?: number; body?: { code?: string; action?: string } };
  if (p.tenantId == null) return;
  await addProblem(p.tenantId, { id: item.id, at: Date.now(), code: p.body?.code ?? '', action: p.body?.action ?? '', outcome: r?.outcome ?? '', message: r?.message ?? '', orderNumber: r?.orderNumber ?? null });
}

async function noteScanFailure(item: OutboxItem, message: string): Promise<void> {
  const p = JSON.parse(item.payload) as { tenantId?: number; body?: { code?: string; action?: string } };
  if (p.tenantId == null) return;
  await addProblem(p.tenantId, { id: item.id, at: Date.now(), code: p.body?.code ?? '', action: p.body?.action ?? '', outcome: 'ERROR', message, orderNumber: null });
}

async function sendItem(item: OutboxItem): Promise<unknown> {
  const headers: Record<string, string> = { 'Idempotency-Key': item.id };
  const payload = JSON.parse(item.payload) as { tenantId?: number; body?: unknown };
  const res = await api.request({
    url: item.endpoint,
    method: item.method,
    data: payload.body,
    headers,
    tenantId: payload.tenantId,
    // La firma y las fotos viajan en este cuerpo; con un enlace móvil lento el tiempo normal (20 s) cortaría la subida y se confundiría con «sin señal».
    timeout: 120_000,
  });
  return res.data;
}

function describeError(err: unknown): string {
  const ax = err as AxiosError<{ title?: string; error_description?: string; message?: string }>;
  if (ax.response) {
    const body = ax.response.data;
    return `HTTP ${ax.response.status}: ${body?.title ?? body?.error_description ?? body?.message ?? ax.message}`;
  }
  return ax.message ?? 'Network error';
}
