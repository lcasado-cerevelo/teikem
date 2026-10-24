// Lote 216 — la estación de escaneo en el teléfono, sin señal. Réplica LOCAL y pura de la decisión del servidor (ScanActionService, Lotes 107/110): sirve para contestar al instante con lo bajado;
// el servidor sigue siendo la autoridad y su respuesta real llega al sincronizar. Sin React ni red: se prueba con scripts/test-scan.mjs.

export const SCAN_ACTIONS = ['PICKUP', 'ARRIVE', 'ROUTE', 'DEPART'] as const;
export type ScanAction = (typeof SCAN_ACTIONS)[number] | 'RETURN';

/** Estatus de la orden al que lleva cada acción (igual que el servidor). */
export function targetStatus(action: ScanAction): string {
  switch (action) {
    case 'PICKUP': return 'PICKUP';
    case 'ARRIVE': return 'UNASSIGNED';
    case 'ROUTE': return 'ASSIGNED';
    case 'DEPART': return 'IN_TRANSIT';
    default: return 'PICKUP';
  }
}

export interface SnapStatus { code: string; label: string; sortOrder: number; stageKind: string; isInitial: boolean; isEnabled: boolean }
export interface LocalPiece { number: string; actions: string[]; missing: string | null }
export interface LocalOrder {
  publicId: string;
  orderNumber: string;
  waybill: string | null;
  statusCode: string;
  tripCode: string | null;
  tripPublicId: string | null;
  pickUp: boolean;
  pieces: LocalPiece[];
  /** Pueblo de la parada (para escanear «por ciudad»). */
  city?: string | null;
  /** Chofer de su ruta vigente (para escanear «por chofer»). */
  driverPublicId?: string | null;
}

/** Cómo se escanea (decidido por Luis): todo, o solo lo de unas rutas, unas ciudades o unos choferes. Los `ids` son ids de ruta, nombres de ciudad o ids de chofer. */
export type ScanScope = { mode: 'ALL' } | { mode: 'ROUTE' | 'CITY' | 'DRIVER'; ids: string[] };
export const ALL_SCOPE: ScanScope = { mode: 'ALL' };

/** Texto sin mayúsculas ni tildes, para comparar ciudades y buscar en las listas. */
export function fold(text: string | null | undefined): string {
  return (text ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
}

/** ¿El envío es de lo escogido? «Todo» siempre. Por ruta: su ruta vigente está entre las escogidas (con Enrutar y UNA sola ruta escogida, un envío sin ruta también vale: va a esa ruta). */
export function inScope(order: LocalOrder, scope: ScanScope, action: ScanAction): boolean {
  if (scope.mode === 'ALL') return true;
  if (scope.mode === 'ROUTE') {
    if (order.tripPublicId) return scope.ids.includes(order.tripPublicId);
    return action === 'ROUTE' && scope.ids.length === 1;
  }
  if (scope.mode === 'CITY') return !!order.city && scope.ids.some((c) => fold(c) === fold(order.city));
  return !!order.driverPublicId && scope.ids.includes(order.driverPublicId);
}

/** La ruta donde ponen los envíos libres: solo con Enrutar y una única ruta escogida. */
export function destinationTrip(scope: ScanScope, action: ScanAction): string | null {
  return action === 'ROUTE' && scope.mode === 'ROUTE' && scope.ids.length === 1 ? scope.ids[0] : null;
}

export type ScanOutcome = 'APPLIED' | 'COUNTED' | 'DUPLICATE' | 'FINAL' | 'NOT_FOUND' | 'PICKUP_ONLY' | 'AMBIGUOUS' | 'NOT_IN_SCOPE' | 'BEYOND';
/** found = «Sí», dup = «Ok», notfound = «No», warn = el sistema PARA y advierte (el envío ya va más adelante que lo que se escanea). */
export type ScanVoice = 'found' | 'dup' | 'notfound' | 'warn';

/** Mensaje como clave de traducción + parámetros (la pantalla lo traduce a español o inglés). */
export interface ScanMessage { key: string; params: Record<string, string | number> }

export interface LocalScanResult {
  outcome: ScanOutcome;
  voice: ScanVoice;
  message: ScanMessage;
  orderNumber: string | null;
  fromStatus: string | null;
  toStatus: string | null;
  scanned: number;
  total: number;
  /** Con Enrutar: el servidor es quien pone el envío en la ruta; aquí solo se sabe cuál escogió quien escanea (o «la de su zona»). */
  routeNote: ScanMessage | null;
  /** Se manda a la cola para el servidor (los resultados que no cambian ni cuentan nada no se mandan). */
  send: boolean;
}

const msg = (key: string, params: Record<string, string | number> = {}): ScanMessage => ({ key, params });
const voiceOf = (o: ScanOutcome): ScanVoice => (o === 'APPLIED' || o === 'COUNTED' ? 'found' : o === 'DUPLICATE' ? 'dup' : o === 'BEYOND' ? 'warn' : 'notfound');

/** El código escaneado limpio (espacios y saltos de línea que mete el lector), o null si queda vacío. */
export function normalizeCode(raw: string | null | undefined): string | null {
  const c = (raw ?? '').replace(/[\r\n\t]+/g, '').trim();
  return c.length === 0 || c.length > 40 ? null : c;
}

export function notFound(): LocalScanResult {
  return { outcome: 'NOT_FOUND', voice: 'notfound', message: msg('SCAN.MSG_NOT_FOUND'), orderNumber: null, fromStatus: null, toStatus: null, scanned: 0, total: 0, routeNote: null, send: false };
}

export function ambiguous(count: number): LocalScanResult {
  return { outcome: 'AMBIGUOUS', voice: 'notfound', message: msg('SCAN.MSG_AMBIGUOUS', { n: count }), orderNumber: null, fromStatus: null, toStatus: null, scanned: 0, total: 0, routeNote: null, send: false };
}

export interface ApplyOptions {
  /** Ruta escogida para Enrutar (código), si la hay. */
  chosenTripCode?: string | null;
  /** Cómo se escanea (todo / ruta / ciudad / chofer). Por omisión, todo. */
  scope?: ScanScope;
  /** El usuario ya vio la advertencia de «estatus más adelante» y pidió seguir: la pieza solo se cuenta. */
  confirmedBeyond?: boolean;
}

/**
 * Aplica una acción a una pieza del envío con lo bajado. Devuelve el resultado, el estatus nuevo del envío (o null si no cambia) y si la pieza quedó escaneada con la acción.
 * Reglas (las del servidor): un estatus final no se toca; repetir pieza+acción es duplicado; un envío «pick up» no sale a ruta ni a chofer; la acción lleva el envío a su estatus
 * solo si ese estatus está más adelante (desde un lateral, como Intento fallido, se cuenta desde «Recogida»); si no, la pieza solo se cuenta.
 */
export function applyScan(order: LocalOrder, pieceNumber: string, action: ScanAction, statuses: SnapStatus[], options: ApplyOptions = {}): { result: LocalScanResult; nextStatus: string | null; recorded: boolean } {
  const label = (code: string) => statuses.find((s) => s.code === code)?.label ?? code;
  const piece = order.pieces.find((p) => p.number === pieceNumber);
  const base = { orderNumber: order.orderNumber, fromStatus: order.statusCode, toStatus: null as string | null, routeNote: null as ScanMessage | null };
  const total = order.pieces.length;
  const scannedWith = (a: string, extra = 0) => order.pieces.filter((p) => p.actions.includes(a)).length + extra;
  if (!piece) return { result: { ...notFound() }, nextStatus: null, recorded: false };

  const scope = options.scope ?? ALL_SCOPE;
  const current = statuses.find((s) => s.code === order.statusCode);
  const terminal = current?.stageKind === 'TERMINAL';
  const countsAtWarehouse = order.pickUp && action === 'ARRIVE' && order.statusCode === 'IN_WAREHOUSE';

  // 1. Lo escaneado no es de lo escogido (otra ruta, otra ciudad u otro chofer): «No», aunque exista en el sistema.
  if (!inScope(order, scope, action)) {
    return { result: { ...base, outcome: 'NOT_IN_SCOPE', voice: 'notfound', message: msg(`SCAN.MSG_NOT_IN_${scope.mode}`, { piece: pieceNumber, order: order.orderNumber }), scanned: scannedWith(action), total, send: false }, nextStatus: null, recorded: false };
  }
  if (order.pickUp && (action === 'ROUTE' || action === 'DEPART')) {
    return { result: { ...base, outcome: 'PICKUP_ONLY', voice: 'notfound', message: msg('SCAN.MSG_PICKUP_ONLY', { piece: pieceNumber, order: order.orderNumber }), scanned: scannedWith(action), total, send: false }, nextStatus: null, recorded: false };
  }
  // 2. Estatus final (entregada, cancelada…): «No».
  if (terminal && !countsAtWarehouse) {
    return { result: { ...base, outcome: 'FINAL', voice: 'notfound', message: msg('SCAN.MSG_FINAL', { piece: pieceNumber, order: order.orderNumber, status: label(order.statusCode) }), scanned: scannedWith(action), total, send: false }, nextStatus: null, recorded: false };
  }
  // 3. ESTA pieza ya se escaneó con esta acción: «Ok».
  if (piece.actions.includes(action)) {
    return { result: { ...base, outcome: 'DUPLICATE', voice: 'dup', message: msg('SCAN.MSG_DUPLICATE', { piece: pieceNumber }), scanned: scannedWith(action), total, send: false }, nextStatus: null, recorded: false };
  }

  const target = order.pickUp && action === 'ARRIVE' ? 'IN_WAREHOUSE' : targetStatus(action);
  const targetStage = statuses.find((s) => s.code === target);
  const fromSort = current?.stageKind === 'LATERAL' ? (statuses.find((s) => s.code === 'PICKUP')?.sortOrder ?? -1) : (current?.sortOrder ?? -1);
  const sameStatus = countsAtWarehouse || order.statusCode === target;
  const beyond = !sameStatus && !!targetStage && targetStage.sortOrder <= fromSort;

  // 4. El envío ya va MÁS ADELANTE que lo que se escanea: el sistema PARA y advierte; solo sigue si la persona lo confirma (la pieza se cuenta, el estatus no retrocede).
  if (beyond && !options.confirmedBeyond) {
    return { result: { ...base, outcome: 'BEYOND', voice: 'warn', message: msg('SCAN.MSG_BEYOND', { piece: pieceNumber, order: order.orderNumber, current: label(order.statusCode), target: label(target) }), scanned: scannedWith(action), total, send: false }, nextStatus: null, recorded: false };
  }

  const routeNote = action === 'ROUTE'
    ? (order.tripCode ? msg('SCAN.ROUTE_ALREADY', { trip: order.tripCode }) : options.chosenTripCode ? msg('SCAN.ROUTE_CHOSEN', { trip: options.chosenTripCode }) : msg('SCAN.ROUTE_ZONE'))
    : null;
  const scanned = scannedWith(action, 1);
  const moves = !sameStatus && !beyond && !!targetStage;
  if (moves) {
    return { result: { ...base, outcome: 'APPLIED', voice: 'found', message: msg('SCAN.MSG_APPLIED', { piece: pieceNumber, order: order.orderNumber, status: label(target) }), toStatus: target, scanned, total, routeNote, send: true }, nextStatus: target, recorded: true };
  }
  // 5. La pieza es nueva para esta acción pero el envío ya estaba en ese estatus (o el usuario confirmó seguir): la pieza se cuenta y ella dice «Sí». El «Ok» es solo de la pieza repetida (paso 3):
  //    el escaneo es a la PIEZA, y es ella la que dice Sí o No (decidido por Luis).
  return { result: { ...base, outcome: 'COUNTED', voice: 'found', message: msg(sameStatus ? 'SCAN.MSG_SAME_STATUS' : 'SCAN.MSG_COUNTED', { piece: pieceNumber, status: label(target), order: order.orderNumber }), scanned, total, routeNote, send: true }, nextStatus: null, recorded: true };
}

/** Resultados del servidor que NO son un problema al sincronizar (el escaneo quedó hecho o ya estaba hecho). */
export const OK_SERVER_OUTCOMES = ['APPLIED', 'COUNTED', 'DUPLICATE', 'ALREADY_BEYOND', 'REAPPEARED', 'RETURNED'];
export function isServerProblem(outcome: string | null | undefined): boolean {
  return !!outcome && !OK_SERVER_OUTCOMES.includes(outcome);
}
export { voiceOf };
