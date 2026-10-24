import { onlineManager } from '@tanstack/react-query';
import { storage } from './db';
import { metaKey, readMeta, type ScanMeta } from './scanProblems';
import { enqueue, listPending } from './outbox';
import { drainOutbox } from './syncEngine';
import { fetchLookup, fetchSnapshotPage, postScanAction, type LookupItem, type LookupResult } from '@services/scanApi';
import { ALL_SCOPE, ambiguous, applyScan, destinationTrip, fold, inScope, normalizeCode, notFound, type LocalOrder, type LocalScanResult, type ScanAction, type ScanScope, type SnapStatus } from '@utils/scanRules';
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
  const cities = new Map<string, string>();   // sin tildes ni mayúsculas → como se escribe
  const addCities = (orders: LocalOrder[]) => { for (const o of orders) if (o.city && !cities.has(fold(o.city))) cities.set(fold(o.city), o.city); };
  try {
    let got = first.orders.length;
    addCities(first.orders);
    await storage.scanInsertOrders(tenantId, gen, first.orders);
    onProgress?.(got, first.total);
    while (got < first.total) {
      const page = await fetchSnapshotPage(tenantId, got);
      if (page.orders.length === 0) break;   // el servidor tiene menos de lo que dijo: se confirma lo que llegó
      addCities(page.orders);
      await storage.scanInsertOrders(tenantId, gen, page.orders);
      got += page.orders.length;
      onProgress?.(got, first.total);
    }
    await storage.scanCommit(tenantId, gen);
  } catch (e) {
    await storage.scanAbort(tenantId, gen);
    throw e;
  }
  const meta: ScanMeta = { generatedAtUtc: first.generatedAtUtc, downloadedAtUtc: new Date().toISOString(), total: await storage.scanOrderCount(tenantId), capped: first.capped, statuses: first.statuses, routes: first.routes, drivers: first.drivers, cities: [...cities.values()].sort((a, b) => a.localeCompare(b)) };
  await storage.kvSet(metaKey(tenantId), JSON.stringify(meta));
  // lo escaneado sin señal que aún no llegó al servidor se vuelve a marcar sobre lo recién bajado
  for (const s of await pendingScans(tenantId)) await applyToLocal(tenantId, s.code, s.action as ScanAction, meta.statuses, ALL_SCOPE, null, true);
  return meta;
}

/**
 * Aplica una acción a la pieza en lo bajado (y la anota). Con un alcance (ruta, ciudad o chofer), solo cuentan los envíos de lo escogido: si el número está en el sistema pero es de otro, la respuesta es «No».
 * `replay`: se está volviendo a marcar un escaneo que ya se aceptó (después de una descarga): no se advierte ni se filtra, solo se anota.
 */
async function applyToLocal(tenantId: number, code: string, action: ScanAction, statuses: SnapStatus[], scope: ScanScope, tripCode: string | null, replay = false, confirmedBeyond = false): Promise<{ result: LocalScanResult; order: LocalOrder | null }> {
  const all = (await storage.scanFindPiece(tenantId, code)).map((m) => m.order);
  if (all.length === 0) return { result: notFound(), order: null };
  const effective = replay ? ALL_SCOPE : scope;
  const matches = effective.mode === 'ALL' ? all : all.filter((o) => inScope(o, effective, action));
  if (matches.length === 0) {
    // existe, pero no es de lo escogido: «No» (se explica con el primero)
    return { result: applyScan(all[0], code, action, statuses, { scope: effective, chosenTripCode: tripCode }).result, order: all[0] };
  }
  if (matches.length > 1) return { result: ambiguous(matches.length), order: null };
  const order = matches[0];
  const { result, nextStatus, recorded } = applyScan(order, code, action, statuses, { scope: effective, chosenTripCode: tripCode, confirmedBeyond: confirmedBeyond || replay });
  if (recorded) await storage.scanRecord(tenantId, order.publicId, code, action, nextStatus);
  return { result, order };
}

export interface ScanOutput {
  result: LocalScanResult;
  orderPublicId: string | null;
  /** El código no cabe en lo bajado y hay señal: se le preguntó al servidor. */
  server: { outcome: string; message: string } | null;
}

export interface ScanOptions {
  /** Cómo se escanea (por omisión, todo). Con Enrutar y una sola ruta escogida, los envíos libres van a esa ruta. */
  scope?: ScanScope;
  /** El usuario vio la advertencia de «estatus más adelante» y pidió seguir. */
  confirmedBeyond?: boolean;
}

/**
 * Un escaneo. Local-primero: se contesta con lo bajado y, si cuenta, se encola para el servidor (sale solo con señal). Si la pieza no está en lo bajado, se escanea TODO y HAY señal, se le pregunta al servidor
 * (la descarga pudo quedar atrasada); sin señal, «No se encontró la pieza». Con un alcance (ruta/ciudad/chofer) no se le pregunta al servidor: el filtro vive en el teléfono y lo que no está en lo bajado es «No».
 * Devolver (RETURN) y los números repetidos solo van directo al servidor, con señal.
 */
export async function scanPiece(tenantId: number, rawCode: string, action: ScanAction, opts: ScanOptions = {}): Promise<ScanOutput | 'EMPTY'> {
  const code = normalizeCode(rawCode);
  if (!code) return 'EMPTY';
  const meta = await readMeta(tenantId);
  const statuses = meta?.statuses ?? [];
  const scope = opts.scope ?? ALL_SCOPE;
  const destination = destinationTrip(scope, action);
  const tripCode = destination ? (meta?.routes.find((r) => r.publicId === destination)?.code ?? null) : null;

  if (action === 'RETURN') return viaServer(tenantId, code, action, null, undefined, opts.confirmedBeyond);

  const local = await applyToLocal(tenantId, code, action, statuses, scope, tripCode, false, opts.confirmedBeyond);
  if (local.result.outcome === 'NOT_FOUND' || local.result.outcome === 'AMBIGUOUS') {
    if (scope.mode === 'ALL' && onlineManager.isOnline()) return viaServer(tenantId, code, action, null, local.result, opts.confirmedBeyond);
    return { result: local.result, orderPublicId: null, server: null };
  }
  if (local.result.send) {
    await enqueue({ kind: 'scan', endpoint: 'scan/action', method: 'POST', payload: { tenantId, body: { action, code, tripPublicId: destination || undefined, confirmBeyond: opts.confirmedBeyond || undefined }, meta: { orderNumber: local.result.orderNumber } } });
    if (onlineManager.isOnline()) void drainOutbox();
  }
  return { result: local.result, orderPublicId: local.order?.publicId ?? null, server: null };
}

async function viaServer(tenantId: number, code: string, action: ScanAction, tripPublicId: string | null, fallback?: LocalScanResult, confirmBeyond?: boolean): Promise<ScanOutput> {
  if (!onlineManager.isOnline()) {
    return { result: fallback ?? { ...notFound(), message: { key: 'SCAN.MSG_NEEDS_SIGNAL', params: {} } }, orderPublicId: null, server: null };
  }
  try {
    const r = await postScanAction(tenantId, action, code, tripPublicId, confirmBeyond);
    const found = r.voice === 'found';
    const result: LocalScanResult = {
      outcome: r.outcome === 'BEYOND' ? 'BEYOND' : found ? 'COUNTED' : r.voice === 'dup' ? 'DUPLICATE' : 'NOT_FOUND', voice: r.outcome === 'BEYOND' ? 'warn' : found ? 'found' : r.voice === 'dup' ? 'dup' : 'notfound',
      message: { key: 'SCAN.MSG_SERVER', params: { text: r.message } }, orderNumber: r.orderNumber, fromStatus: null, toStatus: null, scanned: r.scanned ?? 0, total: r.total ?? 0, routeNote: null, send: false,
    };
    return { result, orderPublicId: null, server: { outcome: r.outcome, message: r.message } };
  } catch {
    return { result: fallback ?? { ...notFound(), message: { key: 'SCAN.MSG_NEEDS_SIGNAL', params: {} } }, orderPublicId: null, server: null };
  }
}


/**
 * La lupa. Con señal pregunta a la base (todo lo que haya: cancelado, final, baja, reclamación…); sin señal muestra solo lo que hay en lo bajado (envíos no completados), y lo dice.
 */
export async function lookupPiece(tenantId: number, rawCode: string): Promise<LookupResult | 'EMPTY'> {
  const code = normalizeCode(rawCode);
  if (!code) return 'EMPTY';
  if (onlineManager.isOnline()) {
    try { return await fetchLookup(tenantId, code); } catch { /* sin respuesta: se cae a lo bajado */ }
  }
  const meta = await readMeta(tenantId);
  const label = (c: string) => meta?.statuses.find((x) => x.code === c)?.label ?? c;
  const found = (await storage.scanFindPiece(tenantId, code)).map((m) => m.order);
  const items: LookupItem[] = found.map((o) => {
    const p = o.pieces.find((x) => x.number === code);
    return {
      pieceNumber: p?.number ?? null, pieceActive: true, missing: p?.missing ?? null, claimNumber: null, claimStatus: null,
      scans: (p?.actions ?? []).map((a) => ({ action: a, scannedAtUtc: null, byName: null })),
      orderNumber: o.orderNumber, waybillNumber: o.waybill, status: label(o.statusCode), orderActive: true, clientName: null, consigneeName: null, city: o.city ?? null,
      tripCode: o.tripCode, tripStatus: null, driverName: null, pieces: o.pieces.length, flags: o.tripCode ? ['IN_ROUTE'] : [],
    };
  });
  return { code, matchedBy: items.length ? 'PIECE' : null, items, offline: true };
}
