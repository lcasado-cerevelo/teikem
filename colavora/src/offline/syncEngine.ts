import { AxiosError } from 'axios';
import { api } from '@config/axios';
import { bumpRetry, listPending, markDone, markFailed, type OutboxItem } from './outbox';

interface MultipartPayload {
  // An array value is appended once per item under the same key — how ASP.NET
  // binds a List<string> from a form (e.g. Barcodes in dispatch_batch).
  fields?: Record<string, string | number | boolean | null | undefined | Array<string | number>>;
  files?: Array<{ field: string; uri: string; name: string; type: string }>;
}

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

  if (item.isMultipart) {
    const desc = JSON.parse(item.payload) as MultipartPayload;
    const form = new FormData();
    for (const [k, v] of Object.entries(desc.fields ?? {})) {
      if (v == null) continue;
      if (Array.isArray(v)) {
        for (const item of v) form.append(k, String(item));
      } else {
        form.append(k, String(v));
      }
    }
    for (const f of desc.files ?? []) {
      form.append(f.field, { uri: f.uri, name: f.name, type: f.type } as unknown as Blob);
    }
    await api.request({
      url: item.endpoint,
      method: item.method,
      data: form,
      headers: { ...headers, 'Content-Type': 'multipart/form-data' },
      // Proof photo + signature ride in this body; on a slow mobile uplink the
      // default 20s timeout cuts the upload short and misreads it as offline.
      timeout: 120_000,
    });
    return;
  }

  await api.request({
    url: item.endpoint,
    method: item.method,
    data: JSON.parse(item.payload),
    headers,
  });
}

function describeError(err: unknown): string {
  const ax = err as AxiosError<{ error_description?: string; message?: string }>;
  if (ax.response) {
    const body = ax.response.data;
    return `HTTP ${ax.response.status}: ${body?.error_description ?? body?.message ?? ax.message}`;
  }
  return ax.message ?? 'Network error';
}
