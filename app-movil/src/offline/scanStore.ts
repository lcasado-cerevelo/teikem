import { onlineManager } from '@tanstack/react-query';
import { storage } from './db';
import { metaKey, readMeta, type ScanMeta } from './scanProblems';
import { enqueue, listPending } from './outbox';
import { drainOutbox } from './syncEngine';
import { fetchSnapshotPage, postScanAction } from '@services/scanApi';
import { ambiguous, applyScan, normalizeCode, notFound, type LocalOrder, type LocalScanResult, type ScanAction, type SnapStatus } from '@utils/scanRules';
export { addProblem, dismissProblem, readMeta, readProblems, type ScanMeta, type ScanProblem } from './scanProblems';

// Lote 216 — la estación de escaneo sin señal: la descarga (hasta 5000 envíos no completados, los más recientes) y el escaneo local-primero.
// Cada lectura se contesta al instante con lo bajado y queda en la cola (la misma de las entregas); sale al servidor cuando hay señal (POST /scan/action, idempotente por pieza y acción).
// Lo que el servidor conteste distinto de «hecho» (p. ej. no hay ruta que reciba el envío) queda en la lista «Con problema».

/** Escaneos de esta compañía que todavía no llegaron al servidor. */
export async function pendingScans(tenantId: number): Promise<{ id: string; code: string; action: string; tripPublicId: string | null }[]> {
  const items = await listPending(5000);
  const out: { id: string; code: string; action: string; tripPublicId: string | null }[] = [];
  for (const i of items) {
    if (i.kind !== 'scan') continue;
    try {
      const p = JSON.parse(i.payload) as { tenantId?: number; body?: { code?: string; action?: string; tripPublicId?: string } };
      if (p.tenantId === tenantId && p.body?.code && p.body.action) out.push({ id: i.id, code: p.body.code, action: p.body.action, tripPublicId: p.body.tripPublicId ?? null });
    } catch { /* payload dañado: se ignora */ }
  }
  return out;
}

/**
 * Baja la descarga de la compañía por páginas a una generación nueva y, solo si llegó completa, reemplaza a la anterior. Luego vuelve a aplicar encima los escaneos que siguen en la cola
 * (el servidor todavía no los tiene). Lanza si no hay señal o el servidor falla; en ese caso lo anterior queda intacto.
 */
export async function downloadSnapshot(tenantId: number, onProgress?: (done: number, total: number) => void): Promise<ScanMeta> {
  const first = await fetchSnapshotPage(tenantId, 0);
  const gen = await storage.scanNewGeneration(tenantId);
  try {
    let got = first.orders.length;
    await storage.scanInsertOrders(tenantId, gen, first.orders);
    onProgress?.(got, first.total);
    while (got < first.total) {
      const page = await fetchSnapshotPage(tenantId, got);
      if (page.orders.length === 0) break;   // el servidor tiene menos de lo que dijo: se confirma lo que llegó
      await storage.scanInsertOrders(tenantId, gen, page.orders);
      got += page.orders.length;
      onProgress?.(got, first.total);
    }
    await storage.scanCommit(tenantId, gen);
  } catch (e) {
    await storage.scanAbort(tenantId, gen);
    throw e;
  }
  const meta: ScanMeta = { generatedAtUtc: first.generatedAtUtc, downloadedAtUtc: new Date().toISOString(), total: await storage.scanOrderCount(tenantId), capped: first.capped, statuses: first.statuses, routes: first.routes };
  await storage.kvSet(metaKey(tenantId), JSON.stringify(meta));
  // lo escaneado sin señal que aún no llegó al servidor se vuelve a marcar sobre lo recién bajado
  for (const s of await pendingScans(tenantId)) await applyToLocal(tenantId, s.code, s.action as ScanAction, meta.statuses, s.tripPublicId, null);
  return meta;
}

/** Aplica una acción a la pieza en lo bajado (y la anota). Devuelve el resultado local y el envío afectado. */
async function applyToLocal(tenantId: number, code: string, action: ScanAction, statuses: SnapStatus[], tripPublicId: string | null, tripCode: string | null): Promise<{ result: LocalScanResult; order: LocalOrder | null; many: number }> {
  const matches = await storage.scanFindPiece(tenantId, code);
  if (matches.length === 0) return { result: notFound(), order: null, many: 0 };
  if (matches.length > 1) return { result: ambiguous(matches.length), order: null, many: matches.length };
  const order = matches[0].order;
  const { result, nextStatus, recorded } = applyScan(order, code, action, statuses, { chosenTripCode: tripCode });
  if (recorded) await storage.scanRecord(tenantId, order.publicId, code, action, nextStatus);
  void tripPublicId;
  return { result, order, many: 1 };
}

export interface ScanOutput {
  result: LocalScanResult;
  orderPublicId: string | null;
  /** El código no cabe en lo bajado y hay señal: se le preguntó al servidor. */
  server: { outcome: string; message: string } | null;
}

/**
 * Un escaneo. Local-primero: se contesta con lo bajado y, si cuenta, se encola para el servidor (sale solo con señal). Si la pieza no está en lo bajado y HAY señal, se le pregunta al servidor
 * (la descarga pudo quedar atrasada); sin señal, «No se encontró la pieza». Devolver (RETURN) y los números repetidos en varios envíos solo van directo al servidor, con señal.
 */
export async function scanPiece(tenantId: number, rawCode: string, action: ScanAction, opts: { tripPublicId?: string | null; tripCode?: string | null } = {}): Promise<ScanOutput | 'EMPTY'> {
  const code = normalizeCode(rawCode);
  if (!code) return 'EMPTY';
  const meta = await readMeta(tenantId);
  const statuses = meta?.statuses ?? [];

  if (action === 'RETURN') return viaServer(tenantId, code, action, opts.tripPublicId ?? null);

  const local = await applyToLocal(tenantId, code, action, statuses, opts.tripPublicId ?? null, opts.tripCode ?? null);
  if (local.result.outcome === 'NOT_FOUND' || local.result.outcome === 'AMBIGUOUS') {
    // fuera de lo bajado (más de 5000, o más nueva que la descarga) o repetida: solo el servidor puede decirlo, y solo con señal
    if (onlineManager.isOnline()) return viaServer(tenantId, code, action, opts.tripPublicId ?? null, local.result);
    return { result: local.result, orderPublicId: null, server: null };
  }
  if (local.result.send) {
    await enqueue({ kind: 'scan', endpoint: 'scan/action', method: 'POST', payload: { tenantId, body: { action, code, tripPublicId: opts.tripPublicId || undefined }, meta: { orderNumber: local.result.orderNumber } } });
    if (onlineManager.isOnline()) void drainOutbox();
  }
  return { result: local.result, orderPublicId: local.order?.publicId ?? null, server: null };
}

async function viaServer(tenantId: number, code: string, action: ScanAction, tripPublicId: string | null, fallback?: LocalScanResult): Promise<ScanOutput> {
  if (!onlineManager.isOnline()) {
    return { result: fallback ?? { ...notFound(), message: { key: 'SCAN.MSG_NEEDS_SIGNAL', params: {} } }, orderPublicId: null, server: null };
  }
  try {
    const r = await postScanAction(tenantId, action, code, tripPublicId);
    const found = r.voice === 'found';
    const result: LocalScanResult = {
      outcome: found ? 'COUNTED' : r.voice === 'dup' ? 'DUPLICATE' : 'NOT_FOUND', voice: found ? 'found' : r.voice === 'dup' ? 'dup' : 'notfound',
      message: { key: 'SCAN.MSG_SERVER', params: { text: r.message } }, orderNumber: r.orderNumber, fromStatus: null, toStatus: null, scanned: r.scanned ?? 0, total: r.total ?? 0, routeNote: null, send: false,
    };
    return { result, orderPublicId: null, server: { outcome: r.outcome, message: r.message } };
  } catch {
    return { result: fallback ?? { ...notFound(), message: { key: 'SCAN.MSG_NEEDS_SIGNAL', params: {} } }, orderPublicId: null, server: null };
  }
}
