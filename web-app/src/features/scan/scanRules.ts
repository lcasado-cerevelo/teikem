// Lote F-E1 — Estación de escaneo (salida): reglas puras (manual 05 §7 y §3.1). Sin React ni red: se prueban solas.
// La verdad de los resultados es el servidor (`outcome`, `reasonCode`, `message`, `voice`); aquí solo se decide cómo se pintan.
import type { components } from '../../kernel/api/schema'
import type { ChipTone } from '../../kernel/ui/Chip'
import { matchesQ } from '../../kernel/ui/matchesQ'

export type ScanResult = components['schemas']['OutboundScanResultDto']
export type UnassignedOrder = components['schemas']['UnassignedOrderDto']

/** Largo máximo del código (`OutboundScanRules.MaxCodeLength`). */
export const MAX_CODE_LENGTH = 40
/** Máximo de órdenes que se pide para «Pendientes por escanear» (tope del API: 500). */
export const PENDING_TAKE = 500
/** Escaneos que se recuerdan en la sesión (la lista vive solo en el navegador). */
export const MAX_SESSION_ENTRIES = 200

export const Outcomes = {
  FoundAssigned: 'FOUND_ASSIGNED',
  FoundUnassigned: 'FOUND_UNASSIGNED',
  AlreadyAssigned: 'ALREADY_ASSIGNED',
  NotFound: 'NOT_FOUND',
  NotEligible: 'NOT_ELIGIBLE',
} as const

export const Reasons = {
  NoMatch: 'NO_MATCH',
  MultipleMatches: 'MULTIPLE_MATCHES',
  InTrip: 'IN_TRIP',
  NotEligible: 'NOT_ELIGIBLE',
  NoZone: 'NO_ZONE',
  AmbiguousZone: 'AMBIGUOUS_ZONE',
  NoOpenRoute: 'NO_OPEN_ROUTE',
} as const

/** Código listo para enviar (el servidor también recorta espacios). */
export function normalizeCode(raw: string): string {
  return raw.trim()
}

/** Clave de i18n del problema del código, o null si se puede enviar. Espejo de `OutboundScanRules.NormalizeCode`. */
export function codeProblemKey(raw: string): 'scan.errors.empty' | 'scan.errors.tooLong' | null {
  const code = normalizeCode(raw)
  if (code === '') return 'scan.errors.empty'
  if (code.length > MAX_CODE_LENGTH) return 'scan.errors.tooLong'
  return null
}

export type ScanTone = 'ok' | 'unassigned' | 'dup' | 'ineligible' | 'notfound'

export interface OutcomeInfo {
  tone: ScanTone
  chip: ChipTone
  /** Clave del nombre corto del resultado. */
  labelKey: string
}

const OUTCOME_INFO: Record<string, OutcomeInfo> = {
  [Outcomes.FoundAssigned]: { tone: 'ok', chip: 'deliv', labelKey: 'scan.outcome.FOUND_ASSIGNED' },
  [Outcomes.FoundUnassigned]: { tone: 'unassigned', chip: 'warn', labelKey: 'scan.outcome.FOUND_UNASSIGNED' },
  [Outcomes.AlreadyAssigned]: { tone: 'dup', chip: 'disp', labelKey: 'scan.outcome.ALREADY_ASSIGNED' },
  [Outcomes.NotEligible]: { tone: 'ineligible', chip: 'wh', labelKey: 'scan.outcome.NOT_ELIGIBLE' },
  [Outcomes.NotFound]: { tone: 'notfound', chip: 'fail', labelKey: 'scan.outcome.NOT_FOUND' },
}

/** Chip, tono y nombre del `outcome`; un valor desconocido se pinta como «no encontrada» (rojo) con su propio texto. */
export function outcomeInfo(outcome: string | null | undefined): OutcomeInfo {
  return OUTCOME_INFO[outcome ?? ''] ?? { tone: 'notfound', chip: 'fail', labelKey: 'scan.outcome.UNKNOWN' }
}

/** Clave del texto «qué hacer» del `reasonCode` (null si no hace falta: asignada o motivo desconocido). */
export function hintKey(result: Pick<ScanResult, 'outcome' | 'reasonCode'>): string | null {
  if (result.outcome === Outcomes.FoundAssigned) return null
  const reason = result.reasonCode ?? ''
  return Object.values<string>(Reasons).includes(reason) ? `scan.hint.${reason}` : null
}

export type VoiceKind = 'found' | 'dup' | 'notfound'

/** La palabra a pronunciar: la que manda el servidor (`voice`); si falta o es rara, la que sale del resultado. */
export function voiceKind(result: Pick<ScanResult, 'outcome' | 'voice'>): VoiceKind {
  if (result.voice === 'found' || result.voice === 'dup' || result.voice === 'notfound') return result.voice
  if (result.outcome === Outcomes.FoundAssigned || result.outcome === Outcomes.FoundUnassigned) return 'found'
  if (result.outcome === Outcomes.AlreadyAssigned) return 'dup'
  return 'notfound'
}

// ---------------------------------------------------------------- sesión

/** Lo que se sabe de la orden de un escaneo (de «Pendientes» o de la ficha); todo opcional. */
export interface KnownOrder {
  clientName?: string | null
  consigneeName?: string | null
  city?: string | null
  postalCode?: string | null
  zoneCode?: string | null
  statusLabel?: string | null
}

export interface ScanEntry {
  id: number
  /** Lo que se escaneó o escribió. */
  code: string
  /** ISO UTC del momento del escaneo. */
  at: string
  result: ScanResult
  known: KnownOrder
}

/** Agrega al principio y recorta a `MAX_SESSION_ENTRIES`. */
export function pushEntry(list: readonly ScanEntry[], entry: ScanEntry): ScanEntry[] {
  return [entry, ...list].slice(0, MAX_SESSION_ENTRIES)
}

/** Lo que muestra una fila de «Pendientes» para una orden sin asignar. */
export function knownFromPending(o: UnassignedOrder): KnownOrder {
  return {
    clientName: o.clientName,
    consigneeName: o.consigneeName,
    city: o.city,
    postalCode: o.postalCode,
    zoneCode: o.zoneCode,
    statusLabel: o.status,
  }
}

/** Referencia que se ve de un escaneo: el número de la orden; si no se encontró, lo escrito. */
export function entryRef(e: ScanEntry): string {
  return e.result.orderNumber || e.code
}

/** Medidor: órdenes DISTINTAS asignadas a una ruta en la sesión (un duplicado o un «ya estaba» no suma). */
export function assignedOrderIds(entries: readonly ScanEntry[]): Set<string> {
  const ids = new Set<string>()
  for (const e of entries) {
    if (e.result.outcome === Outcomes.FoundAssigned && e.result.orderPublicId) ids.add(e.result.orderPublicId)
  }
  return ids
}

export interface ScanFilters {
  /** '' = todas, 'none' = sin zona, si no el id de la zona. */
  zone: string
  zoneCode: string | null
  postalCode: string
  city: string
  clientName: string | null
}

export const NO_FILTERS: ScanFilters = { zone: '', zoneCode: null, postalCode: '', city: '', clientName: null }

export function hasFilters(f: ScanFilters): boolean {
  return f.zone !== '' || f.postalCode.trim() !== '' || f.city.trim() !== '' || f.clientName !== null
}

const fold = (s: string | null | undefined) => (s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim()

/**
 * ¿Un escaneo de la sesión cae dentro de los filtros? Sin filtros, todos. Con un filtro, de las órdenes solo las que se sabe que cumplen;
 * un código que no es de ninguna orden (no encontrado) no tiene nada que filtrar y se ve siempre: un error de lectura no debe desaparecer.
 */
export function entryMatchesFilters(e: ScanEntry, f: ScanFilters): boolean {
  if (!hasFilters(f)) return true
  if (!e.result.orderPublicId) return true
  const k = e.known
  if (f.zone === 'none') {
    if (k.zoneCode) return false
  } else if (f.zone !== '') {
    if (!f.zoneCode || fold(k.zoneCode) !== fold(f.zoneCode)) return false
  }
  if (f.postalCode.trim() !== '' && !fold(k.postalCode).startsWith(fold(f.postalCode))) return false
  if (f.city.trim() !== '' && fold(k.city) !== fold(f.city)) return false
  if (f.clientName !== null && fold(k.clientName) !== fold(f.clientName)) return false
  return true
}

/** Buscador libre del panel de escaneados (referencia, empaque, cliente, consignatario, pueblo, ruta, resultado, mensaje). */
export function entryMatchesQuery(e: ScanEntry, q: string, outcomeLabel: string): boolean {
  return matchesQ(
    q,
    e.code,
    e.result.orderNumber,
    e.result.packBatchNumber,
    e.known.clientName,
    e.known.consigneeName,
    e.known.city,
    e.known.postalCode,
    e.result.zoneCode,
    e.result.tripCode,
    outcomeLabel,
    e.result.message,
  )
}

/** Buscador libre de «Pendientes por escanear». */
export function pendingMatchesQuery(o: UnassignedOrder, q: string): boolean {
  return matchesQ(q, o.orderNumber, o.packBatchNumber, o.clientInvoiceNumber, o.clientName, o.consigneeName, o.city, o.postalCode, o.zoneCode)
}

/** Medidor «N de M»: M = asignadas en la sesión + pendientes que hay ahora con el filtro. Sin pendientes (sin permiso) no hay M. */
export function meterOf(assigned: number, pendingTotal: number | null): { done: number; total: number | null; percent: number } {
  if (pendingTotal === null) return { done: assigned, total: null, percent: 0 }
  const total = assigned + pendingTotal
  return { done: assigned, total, percent: total === 0 ? 0 : Math.round((100 * assigned) / total) }
}

/** Consulta de pendientes para el API a partir de los filtros de pantalla. */
export function buildPendingQuery(f: ScanFilters, clientPublicId: string | null) {
  const postalCode = f.postalCode.trim()
  const city = f.city.trim()
  const zoneId = f.zone !== '' && f.zone !== 'none' ? Number(f.zone) : null
  return {
    ...(zoneId !== null && Number.isFinite(zoneId) ? { DispatchZoneId: zoneId } : {}),
    ...(f.zone === 'none' ? { NoZone: true } : {}),
    ...(postalCode ? { PostalCode: postalCode } : {}),
    ...(city ? { City: city } : {}),
    ...(clientPublicId ? { ClientPublicId: clientPublicId } : {}),
    Skip: 0,
    Take: PENDING_TAKE,
  }
}
