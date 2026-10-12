// Lote F-E3 — Monitoreo de ruta: reglas puras (manual 05 §9). Sin React ni red: se prueban solas.
// La verdad del avance, los totales y el último ping es el servidor (`GET /trips/monitor`); aquí solo se decide cómo se lee: edad del ping y su
// texto «hace N s», la insignia EN VIVO, el progreso y la próxima llegada, el rumbo en punto cardinal y el orden de lo que se pinta.
import type { components } from '../../kernel/api/schema'
import { parseApiDate } from '../../kernel/api/dates'
import type { TParams } from '../../kernel/i18n'

type Schemas = components['schemas']
export type Monitor = Schemas['MonitorDto']
export type MonitorTotals = Schemas['MonitorTotalsDto']
export type MonitorTrip = Schemas['MonitorTripDto']
export type DriverPing = Schemas['DriverPingDto']
type TFn = (key: string, params?: TParams) => string

/** Un ping con menos de este tiempo (segundos) cuenta como «reciente»: la insignia dice EN VIVO. */
export const LIVE_WINDOW_SECONDS = 120
/** Cada cuánto se vuelve a pedir el monitor mientras se sigue en vivo (ms). */
export const REFRESH_MS = 30_000
/** Clave de localStorage del interruptor «Siguiendo en vivo». */
export const FOLLOW_STORAGE_KEY = 'teikem.monitor.follow'

// ---------------------------------------------------------------- edad del ping

/** ¿El ping trae una posición y una hora de captura legibles? */
export function hasPing(ping: DriverPing | null | undefined): ping is DriverPing {
  if (!ping?.capturedAtUtc) return false
  return !Number.isNaN(parseApiDate(ping.capturedAtUtc).getTime())
}

/** Segundos desde que el chofer capturó el ping (nunca negativo: un reloj atrasado no da «hace -3 s»). Sin ping → null. */
export function pingAgeSeconds(ping: DriverPing | null | undefined, nowMs: number): number | null {
  if (!hasPing(ping)) return null
  return Math.max(0, Math.floor((nowMs - parseApiDate(ping.capturedAtUtc!).getTime()) / 1000))
}

/** Segundos → cantidad y unidad más natural: s (< 1 min), min (< 1 h), h (< 1 día), d. */
export function agoParts(seconds: number): { n: number; unit: 's' | 'min' | 'h' | 'd' } {
  const s = Math.max(0, Math.floor(seconds))
  if (s < 60) return { n: s, unit: 's' }
  if (s < 3600) return { n: Math.floor(s / 60), unit: 'min' }
  if (s < 86_400) return { n: Math.floor(s / 3600), unit: 'h' }
  return { n: Math.floor(s / 86_400), unit: 'd' }
}

/** «hace 12 s» / «hace 3 min» / «hace 2 h» / «hace 1 d» (es) o «12 s ago» (en); sin dato → null. */
export function agoText(t: TFn, seconds: number | null): string | null {
  if (seconds === null) return null
  const { n, unit } = agoParts(seconds)
  return t(`monitor.ago.${unit}`, { n })
}

/** ¿El ping es reciente (menos de `LIVE_WINDOW_SECONDS`)? */
export function isRecentPing(ping: DriverPing | null | undefined, nowMs: number): boolean {
  const age = pingAgeSeconds(ping, nowMs)
  return age !== null && age < LIVE_WINDOW_SECONDS
}

/** EN VIVO solo si al menos una ruta mostrada tiene un ping reciente; si no, la insignia es neutra y honesta. */
export function liveSummary(trips: readonly MonitorTrip[], nowMs: number): { live: boolean; recent: number; withPing: number } {
  let recent = 0
  let withPing = 0
  for (const trip of trips) {
    if (hasPing(trip.lastPing)) withPing++
    if (isRecentPing(trip.lastPing, nowMs)) recent++
  }
  return { live: recent > 0, recent, withPing }
}

// ---------------------------------------------------------------- avance y próxima llegada

/** Porcentaje 0–100 de lo atendido (entregadas + fallidas) sobre el total. */
export function percentOf(done: number, total: number): number {
  return total <= 0 ? 0 : Math.max(0, Math.min(100, Math.round((100 * done) / total)))
}

/** Avance de una ruta: paradas atendidas (completadas + fallidas) sobre el total de sus paradas. */
export function tripProgress(trip: Pick<MonitorTrip, 'completedStops' | 'failedStops' | 'totalStops'>): number {
  return percentOf((trip.completedStops ?? 0) + (trip.failedStops ?? 0), trip.totalStops ?? 0)
}

/**
 * Progreso del día: paradas atendidas sobre paradas totales, con los TOTALES del servidor (calculados antes de la búsqueda, así que no
 * cambian al buscar). Ponderado por paradas: una ruta de 20 paradas pesa más que una de 2.
 */
export function overallProgress(totals: MonitorTotals | null | undefined): number {
  return percentOf((totals?.completedStops ?? 0) + (totals?.failedStops ?? 0), totals?.totalStops ?? 0)
}

/** La llegada planeada más próxima de las rutas dadas (la `nextEtaUtc` más temprana) y de qué ruta es; sin ninguna → null. */
export function nextArrival(trips: readonly MonitorTrip[]): { etaUtc: string; trip: MonitorTrip } | null {
  let best: { etaUtc: string; trip: MonitorTrip; ms: number } | null = null
  for (const trip of trips) {
    if (!trip.nextEtaUtc) continue
    const ms = parseApiDate(trip.nextEtaUtc).getTime()
    if (Number.isNaN(ms)) continue
    if (!best || ms < best.ms) best = { etaUtc: trip.nextEtaUtc, trip, ms }
  }
  return best ? { etaUtc: best.etaUtc, trip: best.trip } : null
}

// ---------------------------------------------------------------- telemetría

export type Compass = 'N' | 'NE' | 'E' | 'SE' | 'S' | 'SW' | 'W' | 'NW'
const COMPASS: readonly Compass[] = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW']

/** Rumbo en grados (0 = norte, sentido del reloj; admite negativos y más de 360) → punto cardinal. Sin dato o no finito → null. */
export function compassOf(deg: number | null | undefined): Compass | null {
  if (typeof deg !== 'number' || !Number.isFinite(deg)) return null
  const d = ((deg % 360) + 360) % 360
  return COMPASS[Math.round(d / 45) % 8]
}

/** Velocidad redondeada a km/h entero; sin dato → null. Un valor negativo o no finito no es una velocidad. */
export function speedOf(kmh: number | null | undefined): number | null {
  if (typeof kmh !== 'number' || !Number.isFinite(kmh) || kmh < 0) return null
  return Math.round(kmh)
}

// ---------------------------------------------------------------- lista y selección

/** La ruta elegida: la de `?trip=` si está en la lista (o se está cargando), si no la primera. */
export function pickSelected(trips: readonly MonitorTrip[], param: string | null, loading: boolean): string | null {
  if (param && (loading || trips.some((x) => x.publicId === param))) return param
  return trips[0]?.publicId ?? null
}

/** Consulta de `GET /trips/monitor`: solo manda lo que tiene valor; «Incluir completadas» va siempre explícito (el servidor lo incluye por omisión). */
export function buildMonitorQuery(f: { date: string; zoneId: string; search: string; includeCompleted: boolean }) {
  const zone = Number(f.zoneId)
  const search = f.search.trim()
  return {
    date: f.date,
    ...(f.zoneId !== '' && Number.isInteger(zone) ? { dispatchZoneId: zone } : {}),
    ...(search ? { search } : {}),
    includeCompleted: f.includeCompleted,
  }
}

// ---------------------------------------------------------------- «Siguiendo en vivo» recordado

/** Lee el interruptor guardado: sin dato o ilegible → true (por omisión se sigue en vivo). */
export function readFollow(storage: Pick<Storage, 'getItem'> | null): boolean {
  try {
    return storage?.getItem(FOLLOW_STORAGE_KEY) !== 'off'
  } catch {
    return true
  }
}

export function writeFollow(storage: Pick<Storage, 'setItem'> | null, on: boolean): void {
  try {
    storage?.setItem(FOLLOW_STORAGE_KEY, on ? 'on' : 'off')
  } catch {
    // almacenamiento no disponible (modo privado, cuota): vale solo en esta visita
  }
}
