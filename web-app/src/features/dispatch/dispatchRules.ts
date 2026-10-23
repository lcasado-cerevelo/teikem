// Lote F-E2a — Sala de despacho: reglas puras (manual 05 §1–§8 y §10). Sin React ni red: se prueban solas.
// La verdad de los estatus, los avisos y los errores es el servidor; aquí solo se decide qué acciones se pintan (permiso Y estatus), se arman
// los requests mínimos y se validan los formularios con los MISMOS mensajes del servidor.
import { z } from 'zod'
import { utcFromZonedInput } from '../../kernel/api/tenantZone'
import { TRANSITION_COMMENT_MAX } from '../../kernel/catalogs'
import type { components } from '../../kernel/api/schema'
import type { TParams } from '../../kernel/i18n'
import { matchesQ } from '../../kernel/ui/matchesQ'
import { accuracyOf, isGeoPoint, stateOf } from '../../kernel/ui/routeMapRules'
import type { RouteMapStop } from '../../kernel/ui/RouteMap'

type Schemas = components['schemas']
export type TripListItem = Schemas['TripListItemDto']
export type TripDetail = Schemas['TripDetailDto']
export type TripIssue = Schemas['TripIssueDto']
export type RouteStop = Schemas['RouteStopDto']
export type DispatchableTrip = Schemas['DispatchableTripDto']
export type TripCreateRequest = Schemas['TripCreateRequest']
export type TripPatchRequest = Schemas['TripPatchRequest']
export type UnassignedOrder = Schemas['UnassignedOrderDto']
export type UnassignedStop = Schemas['UnassignedStopDto']
export type OptimizationRun = Schemas['OptimizationRunDto']
export type DispatchResultItem = Schemas['TripDispatchResultItemDto']
export type PlanDayResult = Schemas['PlanDayResultDto']
export type PlanDayZoneResult = Schemas['PlanDayZoneResultDto']
export type MonitorTrip = Schemas['MonitorTripDto']
type TFn = (key: string, params?: TParams) => string

/** Estatus del dominio TripStatus. */
export const TripStatuses = {
  Draft: 'DRAFT',
  Planned: 'PLANNED',
  Dispatched: 'DISPATCHED',
  InProgress: 'IN_PROGRESS',
  Completed: 'COMPLETED',
  Cancelled: 'CANCELLED',
} as const

/** Topes del servidor (`TripOrderRequestRules`, `TripPlanningRules`). */
export const MAX_ORDERS_PER_REQUEST = 200
export const MAX_STOPS_PER_TRIP = 300
export const MAX_ZONES = 50
export const MAX_BATCH_TRIPS = 50
export const UNASSIGNED_TAKE = 500
export const COMMENT_MAX = TRANSITION_COMMENT_MAX

// ---------------------------------------------------------------- acciones por permiso Y estatus

export interface DispatchPerms {
  plan: boolean
  optimize: boolean
  dispatch: boolean
  close: boolean
}

export interface TripActions {
  /** Agregar, quitar y reordenar órdenes (`trips.plan` + ruta abierta). */
  edit: boolean
  /** Cambiar chofer o vehículo (`trips.plan` + la capacidad EDIT_TRIP del estatus). */
  editHeader: boolean
  optimize: boolean
  /** «Eliminar ruta»: solo borrador o planificada. */
  remove: boolean
  /** «Iniciar ruta»: despachada. */
  start: boolean
  /** «Cerrar ruta»: en curso. */
  close: boolean
}

export const NO_ACTIONS: TripActions = { edit: false, editHeader: false, optimize: false, remove: false, start: false, close: false }

/**
 * Lo que se puede hacer con la ruta activa: el permiso de la acción Y lo que el servidor dice de la ruta (`isEditable`: borrador o
 * planificada; `canEditHeader`: capacidad EDIT_TRIP). Iniciar y cerrar no tienen bandera en el DTO: dependen del estatus.
 */
export function tripActionsOf(
  trip: Pick<TripDetail, 'statusCode' | 'isEditable' | 'canEditHeader' | 'isActive'> | null | undefined,
  perms: DispatchPerms,
): TripActions {
  if (!trip) return NO_ACTIONS
  const open = trip.isEditable === true
  return {
    edit: perms.plan && open,
    editHeader: perms.plan && trip.canEditHeader === true,
    optimize: perms.optimize && open,
    remove: perms.plan && open,
    start: perms.dispatch && trip.statusCode === TripStatuses.Dispatched,
    close: perms.close && trip.statusCode === TripStatuses.InProgress,
  }
}

// ---------------------------------------------------------------- secuencia manual (▲ ▼)

/** Ids de las paradas de la versión vigente en el orden mostrado (lo que exige `PUT /route/sequence`). */
export function stopIds(stops: readonly RouteStop[] | null | undefined): number[] {
  return [...(stops ?? [])].sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)).map((s) => s.id ?? 0)
}

/** Mueve la parada de la posición `index` una posición (−1 sube, +1 baja). Fuera de rango → null (el botón está deshabilitado). */
export function moveStop(ids: readonly number[], index: number, delta: -1 | 1): number[] | null {
  const to = index + delta
  if (index < 0 || index >= ids.length || to < 0 || to >= ids.length) return null
  const next = [...ids]
  ;[next[index], next[to]] = [next[to], next[index]]
  return next
}

export function orderedStops(stops: readonly RouteStop[] | null | undefined): RouteStop[] {
  return [...(stops ?? [])].sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0))
}

// ---------------------------------------------------------------- arrastrar para reordenar (F-E2b)

/** Mueve el elemento de la posición `from` a la posición `to` (las demás se corren). Posiciones fuera de rango → null. */
export function reorderIds(ids: readonly number[], from: number, to: number): number[] | null {
  if (from === to || from < 0 || to < 0 || from >= ids.length || to >= ids.length) return null
  const next = [...ids]
  const [moved] = next.splice(from, 1)
  next.splice(to, 0, moved)
  return next
}

/** Fila sobre la que está el puntero: la primera cuyo borde de abajo (`bottoms`, en px de pantalla) queda por debajo de `y`; pasado el final, la última. */
export function rowIndexAt(bottoms: readonly number[], y: number): number {
  if (bottoms.length === 0) return -1
  const i = bottoms.findIndex((b) => y <= b)
  return i === -1 ? bottoms.length - 1 : i
}

// ---------------------------------------------------------------- plano y pin manual (F-E2b)

/** ¿La parada tiene coordenadas dibujables? (el detalle trae `point` solo si el servidor la geocodificó o se puso a mano). */
export function stopHasPoint(s: Pick<RouteStop, 'point'>): boolean {
  return isGeoPoint(s.point)
}

/** Paradas (ya en el orden mostrado) como las pide el plano: la secuencia mostrada, el nombre accesible «Parada 3 · Consignatario · pueblo», precisión y estado. */
export function mapStops(stops: readonly RouteStop[], t: TFn): RouteMapStop[] {
  return stops.map((s, i) => ({
    id: s.id ?? 0,
    seq: i + 1,
    label: [t('dispatch.map.stop', { number: i + 1 }), s.consigneeName || s.orderNumber, s.city].filter(Boolean).join(' · '),
    short: s.orderNumber ?? undefined,
    point: s.point ?? null,
    accuracy: accuracyOf(s.geocodeAccuracyCode),
    state: stateOf(s.statusCode),
  }))
}

/** La primera parada con pin aproximado o sin pin (la misma definición del aviso APPROXIMATE_PINS del servidor: `isApproximate`). */
export function firstApproximateStop(stops: readonly RouteStop[]): RouteStop | undefined {
  return stops.find((s) => s.isApproximate === true)
}

/** «18,4655» o «18.4655» → 18.4655; lo que no es un número decimal escrito con cifras → null. */
export function parseCoordinate(text: string): number | null {
  const v = text.trim().replace(',', '.')
  if (!/^[+-]?(\d+(\.\d*)?|\.\d+)$/.test(v)) return null
  const n = Number(v)
  return Number.isFinite(n) ? n : null
}

export interface PinValues {
  lat: string
  lng: string
}

export const pinValuesOf = (s: Pick<RouteStop, 'point'> | null | undefined): PinValues =>
  s && isGeoPoint(s.point) ? { lat: String(s.point.lat), lng: String(s.point.lng) } : { lat: '', lng: '' }

/** Latitud y longitud con los mismos textos y rangos del servidor (`RouteEditRules.ValidateLocation`); acepta coma o punto decimal. */
export function pinSchema(t: TFn) {
  return z.object({ lat: z.string(), lng: z.string() }).superRefine((v, ctx) => {
    for (const [field, max, range] of [['lat', 90, 'dispatch.pin.latRange'] as const, ['lng', 180, 'dispatch.pin.lngRange'] as const]) {
      const raw = v[field].trim()
      if (raw === '') {
        ctx.addIssue({ code: 'custom', path: [field], message: t('dispatch.pin.required') })
        continue
      }
      const n = parseCoordinate(raw)
      if (n === null) ctx.addIssue({ code: 'custom', path: [field], message: t(field === 'lat' ? 'dispatch.pin.latNumber' : 'dispatch.pin.lngNumber') })
      else if (Math.abs(n) > max) ctx.addIssue({ code: 'custom', path: [field], message: t(range) })
    }
  })
}

export function buildPinRequest(v: PinValues, rowVersion: string | null | undefined) {
  return { lat: parseCoordinate(v.lat) ?? 0, lng: parseCoordinate(v.lng) ?? 0, rowVersion: rowVersion ?? null }
}

/** Las coordenadas tomadas del plano a 5 decimales (≈ 1 m): lo que se escribe en el diálogo. */
export function coordText(n: number): string {
  return String(Math.round(n * 1e5) / 1e5)
}

// ---------------------------------------------------------------- avisos y bloqueantes

/** Primero los que bloquean; dentro de cada grupo se respeta el orden del servidor. */
export function sortedIssues(issues: readonly TripIssue[] | null | undefined): TripIssue[] {
  const list = [...(issues ?? [])]
  return [...list.filter((i) => i.blocking), ...list.filter((i) => !i.blocking)]
}

export function blockingIssues(issues: readonly TripIssue[] | null | undefined): TripIssue[] {
  return (issues ?? []).filter((i) => i.blocking)
}

export function warningIssues(issues: readonly TripIssue[] | null | undefined): TripIssue[] {
  return (issues ?? []).filter((i) => !i.blocking)
}

/** Una ruta del selector se puede marcar si el servidor dice que se puede despachar (sin bloqueantes). */
export function isSelectable(item: DispatchableTrip): boolean {
  return item.canDispatch === true && blockingIssues(item.issues).length === 0
}

export function selectableIds(items: readonly DispatchableTrip[]): string[] {
  return items.filter(isSelectable).map((i) => i.trip?.publicId ?? '').filter(Boolean)
}

/** ¿Alguna de las rutas elegidas trae avisos que no bloquean? (se despachan confirmando). */
export function selectedWithWarnings(items: readonly DispatchableTrip[], selected: readonly string[]): DispatchableTrip[] {
  return items.filter((i) => selected.includes(i.trip?.publicId ?? '') && warningIssues(i.issues).length > 0)
}

export interface BatchSummary {
  requested: number
  dispatched: number
  failed: number
}

export function batchSummary(items: readonly DispatchResultItem[]): BatchSummary {
  const dispatched = items.filter((i) => i.dispatched).length
  return { requested: items.length, dispatched, failed: items.length - dispatched }
}

// ---------------------------------------------------------------- máximo de paradas

/** De dónde sale el máximo: el del chofer (override) o el de la compañía. Sin datos del chofer (sin `fleet.view`) no se afirma nada. */
export function maxStopsSource(driverOverride: number | null | undefined, known: boolean): 'driver' | 'tenant' | null {
  if (!known) return null
  return driverOverride != null ? 'driver' : 'tenant'
}

/**
 * ¿Tiene sentido avisar que la ruta pasa el máximo de paradas? Solo mientras se arma (Borrador o Planificada): una ruta ya despachada, en curso o cerrada
 * (p. ej. un manifiesto migrado) no se puede cambiar, así que el aviso solo estorba.
 */
export function showOverStopLimit(overStopLimit: boolean | null | undefined, statusCode: string | null | undefined): boolean {
  return overStopLimit === true && ['DRAFT', 'PLANNED'].includes((statusCode ?? '').toUpperCase())
}

// ---------------------------------------------------------------- progreso

const ATTENDED_STOPS = ['COMPLETED', 'FAILED']

/** Paradas ya atendidas (entregadas o falladas) de la ficha. */
export function attendedStops(stops: readonly RouteStop[] | null | undefined): number {
  return (stops ?? []).filter((s) => ATTENDED_STOPS.includes((s.statusCode ?? '').toUpperCase())).length
}

export function percent(done: number, total: number): number {
  return total <= 0 ? 0 : Math.max(0, Math.min(100, Math.round((100 * done) / total)))
}

// ---------------------------------------------------------------- optimización

export interface OptimizeDelta {
  /** Cambio de la distancia contra la corrida anterior, en % (negativo = mejor). */
  distancePct: number | null
  /** Cambio de la duración en minutos (negativo = mejor). */
  durationMin: number | null
}

/**
 * Compara la corrida más reciente con la anterior que dio resultado (`OK`). Las corridas llegan de la más nueva a la más vieja. Sin corrida
 * anterior no hay comparación (null): la primera optimización no inventa un «-12 %». Cada medida se compara solo si las dos corridas la traen:
 * con direcciones sin coordenadas el servidor no calcula distancia (solo 15 min fijos por tramo), así que puede haber duración sin distancia.
 */
export function optimizeDelta(runs: readonly OptimizationRun[] | null | undefined): OptimizeDelta | null {
  const ok = (runs ?? []).filter((r) => (r.statusCode ?? '').toUpperCase() === 'OK')
  if (ok.length < 2) return null
  const [current, previous] = ok
  const distancePct =
    current.totalDistanceKm != null && previous.totalDistanceKm != null && previous.totalDistanceKm > 0
      ? Math.round((100 * (current.totalDistanceKm - previous.totalDistanceKm)) / previous.totalDistanceKm)
      : null
  const durationMin = current.totalDurationMin != null && previous.totalDurationMin != null ? current.totalDurationMin - previous.totalDurationMin : null
  return { distancePct, durationMin }
}

// ---------------------------------------------------------------- errores por orden (422 al agregar)

export interface OrderProblem {
  number: string
  reason: string
}

/** `errors: { "{número}": ["{motivo}"] }` del 422 «Hay órdenes que no se pueden asignar a la ruta.» → una fila por orden y motivo. */
export function orderProblems(errors: Record<string, string[]> | null | undefined): OrderProblem[] {
  return Object.entries(errors ?? {}).flatMap(([number, reasons]) => (reasons ?? []).map((reason) => ({ number, reason })))
}

// ---------------------------------------------------------------- fechas

const DAY = /^(\d{4})-(\d{2})-(\d{2})$/

/** Suma días a un día de calendario 'YYYY-MM-DD' (sin zona: aritmética en UTC). */
export function addDays(day: string, n: number): string {
  const m = DAY.exec(day)
  if (!m) return day
  const d = new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]) + n))
  return d.toISOString().slice(0, 10)
}

/** Rango válido de la fecha de una ruta: de ayer a hoy + 60 días (`TripPlanningRules.ValidatePlanDate`). */
export function planDateRange(today: string): { min: string; max: string } {
  return { min: addDays(today, -1), max: addDays(today, 60) }
}

export function planDateIssue(planDate: string, today: string, t: TFn): string | null {
  if (!planDate) return t('dispatch.errors.planDateRequired')
  const { min, max } = planDateRange(today)
  if (!DAY.test(planDate) || planDate < min || planDate > max) return t('dispatch.errors.planDateRange')
  return null
}

// ---------------------------------------------------------------- formularios

export interface TripCreateValues {
  planDate: string
  dispatchZoneId: string
  driverPublicId: string
  vehiclePublicId: string
  /** Hora de salida 'HH:mm' en la hora de la compañía; vacía = la del servidor (12:00 UTC). El campo se llama como el del servidor para que su error salga debajo. */
  plannedStartUtc: string
}

export const emptyTripCreateValues = (planDate: string): TripCreateValues => ({
  planDate,
  dispatchZoneId: '',
  driverPublicId: '',
  vehiclePublicId: '',
  plannedStartUtc: '',
})

export function tripCreateSchema(t: TFn, today: string) {
  return z
    .object({ planDate: z.string(), dispatchZoneId: z.string(), driverPublicId: z.string(), vehiclePublicId: z.string(), plannedStartUtc: z.string() })
    .superRefine((v, ctx) => {
      const issue = planDateIssue(v.planDate, today, t)
      if (issue) ctx.addIssue({ code: 'custom', path: ['planDate'], message: issue })
      if (v.plannedStartUtc && !/^\d{2}:\d{2}$/.test(v.plannedStartUtc)) ctx.addIssue({ code: 'custom', path: ['plannedStartUtc'], message: t('dispatch.errors.startInvalid') })
    })
}

/** Alta: lo vacío no se manda (sin zona/chofer/vehículo/hora el servidor decide: chofer estándar de la zona, 12:00 UTC). */
export function buildTripCreate(v: TripCreateValues): TripCreateRequest {
  const zone = Number(v.dispatchZoneId)
  const start = v.plannedStartUtc ? utcFromZonedInput(`${v.planDate}T${v.plannedStartUtc}`) : null
  return {
    planDate: v.planDate,
    ...(v.dispatchZoneId !== '' && Number.isFinite(zone) ? { dispatchZoneId: zone } : {}),
    ...(v.driverPublicId ? { driverPublicId: v.driverPublicId } : {}),
    ...(v.vehiclePublicId ? { vehiclePublicId: v.vehiclePublicId } : {}),
    ...(start ? { plannedStartUtc: start } : {}),
  }
}

/** Cambio de chofer o vehículo en la ruta activa: el valor nuevo, o `clear*` si se eligió «Sin …»; siempre con `rowVersion`. */
export function buildHeaderPatch(change: { driver?: string; vehicle?: string }, rowVersion: string | null | undefined): TripPatchRequest {
  return {
    ...(change.driver === undefined ? {} : change.driver === '' ? { clearDriver: true } : { driverPublicId: change.driver }),
    ...(change.vehicle === undefined ? {} : change.vehicle === '' ? { clearVehicle: true } : { vehiclePublicId: change.vehicle }),
    rowVersion: rowVersion ?? null,
  }
}

export interface PlanDayValues {
  planDate: string
  /** Ids de zona elegidos (vacío = todas las zonas activas). */
  zoneIds: string[]
  createEmptyTrips: boolean
}

export function planDaySchema(t: TFn, today: string) {
  return z
    .object({ planDate: z.string(), zoneIds: z.array(z.string()), createEmptyTrips: z.boolean() })
    .superRefine((v, ctx) => {
      const issue = planDateIssue(v.planDate, today, t)
      if (issue) ctx.addIssue({ code: 'custom', path: ['planDate'], message: issue })
      if (v.zoneIds.length > MAX_ZONES) ctx.addIssue({ code: 'custom', path: ['zoneIds'], message: t('dispatch.errors.planDayMaxZones') })
    })
}

export function buildPlanDayRequest(v: PlanDayValues) {
  return {
    planDate: v.planDate,
    dispatchZoneIds: v.zoneIds.length === 0 ? null : v.zoneIds.map(Number),
    createEmptyTrips: v.createEmptyTrips,
  }
}

export interface ReassignValues {
  planDate: string
  zoneIds: string[]
  driverPublicId: string
}

export function reassignSchema(t: TFn, today: string) {
  return z
    .object({ planDate: z.string(), zoneIds: z.array(z.string()), driverPublicId: z.string() })
    .superRefine((v, ctx) => {
      const issue = planDateIssue(v.planDate, today, t)
      if (issue) ctx.addIssue({ code: 'custom', path: ['planDate'], message: issue })
      if (v.zoneIds.length === 0) ctx.addIssue({ code: 'custom', path: ['zoneIds'], message: t('dispatch.errors.zonesRequired') })
      else if (v.zoneIds.length > MAX_ZONES) ctx.addIssue({ code: 'custom', path: ['zoneIds'], message: t('dispatch.errors.reassignMaxZones') })
      if (!v.driverPublicId) ctx.addIssue({ code: 'custom', path: ['driverPublicId'], message: t('dispatch.errors.driverRequired') })
    })
}

export function buildReassignRequest(v: ReassignValues) {
  return { planDate: v.planDate, dispatchZoneIds: v.zoneIds.map(Number), driverPublicId: v.driverPublicId }
}

// ---------------------------------------------------------------- filtros de «Órdenes sin asignar»

export interface UnassignedFilters {
  /** '' = todas, 'none' = sin zona, si no el id de la zona. */
  zone: string
  postalCode: string
  city: string
}

export const NO_UNASSIGNED_FILTERS: UnassignedFilters = { zone: '', postalCode: '', city: '' }

export function hasUnassignedFilters(f: UnassignedFilters, clientPublicId: string | null): boolean {
  return f.zone !== '' || f.postalCode.trim() !== '' || f.city.trim() !== '' || clientPublicId !== null
}

/** Consulta de `GET /trips/unassigned-orders`: los filtros van al servidor (sirven aunque haya más de 500). */
export function buildUnassignedQuery(f: UnassignedFilters, clientPublicId: string | null) {
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
    Take: UNASSIGNED_TAKE,
  }
}

/** Buscador libre sobre lo cargado (después de los filtros). */
export function unassignedMatches(o: UnassignedOrder, q: string): boolean {
  return matchesQ(q, o.orderNumber, o.packBatchNumber, o.clientInvoiceNumber, o.clientName, o.consigneeName, o.city, o.postalCode, o.zoneCode)
}

// ---------------------------------------------------------------- textos de resultado

/** Dos números como «42.3 km»: el formato lo pone la pantalla; aquí solo se redondea a un decimal. */
export const round1 = (n: number): number => Math.round(n * 10) / 10

/** Duración en minutos como «1 h 20 min» / «45 min». */
export function durationText(min: number | null | undefined): string | null {
  if (min == null) return null
  const m = Math.max(0, Math.round(min))
  const h = Math.floor(m / 60)
  const r = m % 60
  return h === 0 ? `${r} min` : r === 0 ? `${h} h` : `${h} h ${r} min`
}

/** «1 parada» / «N paradas». */
export function stopsLabel(t: TFn, n: number): string {
  return t(n === 1 ? 'dispatch.stopsOne' : 'dispatch.stopsMany', { count: n })
}

/**
 * Texto de un error del servidor para mostrar en pantalla: el primer error de campo en un 400 de validación (el título es genérico) y el
 * título tal cual en lo demás (409, 422, 404…: ya es el mensaje del manual).
 */
export function problemMessage(problem: { title: string; code?: string; errors: Record<string, string[]> }): string {
  if (problem.code === 'validation') {
    const first = Object.values(problem.errors).flat().find(Boolean)
    if (first) return first
  }
  return problem.title
}
