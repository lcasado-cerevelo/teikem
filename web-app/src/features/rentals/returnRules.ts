// Lote F18 (Rentas F-R2) — lógica pura de la devolución de renta, la lista de devoluciones y la cola de proceso de los equipos
// devueltos (contrato del servidor: docs/manual/11-rentas.md §5 y §6). Sin React: equipos pendientes de devolver, borrador del
// formulario de devolución y sus validaciones espejo de RentalReturnService (devuelven un código que la pantalla traduce con
// `t('rentalReturns.errors.<code>', params)`: en español es el mensaje exacto del servidor), cuerpo del POST, filtros y URL de
// las listas, días en proceso (día de la compañía) y estatus a los que se ofrece avanzar un proceso.
import type { components, paths } from '../../kernel/api/schema'
import { StageKinds, type StatusOption } from '../../kernel/catalogs/types'
import type { RentalDto, RentalIssue, RentalLineDto } from './rentalRules'

type Schemas = components['schemas']
export type RentalReturnDto = Schemas['RentalReturnDto']
export type RentalReturnListItemDto = Schemas['RentalReturnListItemDto']
export type RentalReturnLineDto = Schemas['RentalReturnLineDto']
export type RentalReturnCreateRequest = Schemas['RentalReturnCreateRequest']
export type RentalProcessDto = Schemas['RentalProcessDto']
export type RentalReturnListQuery = NonNullable<paths['/api/v1/rental-returns']['get']['parameters']['query']>
export type RentalProcessListQuery = NonNullable<paths['/api/v1/rental-processes']['get']['parameters']['query']>

/** Catálogos (LookupCode) de la devolución y dominio de estatus / tipo de entidad del proceso. */
export const RETURN_REASON_DOMAIN = 'RentalReturnReason'
export const RETURN_CONDITION_DOMAIN = 'RentalReturnCondition'
export const PROCESS_STATUS_DOMAIN = 'RentalProcessStatus'
export const PROCESS_ENTITY_TYPE = 'RENTAL_PROCESS'
/** Motivo que exige notas y condición por defecto (códigos del servidor). */
export const REASON_OTHER = 'OTHER'
export const CONDITION_GOOD = 'GOOD'
/** Terminales del proceso con acción propia (Completar → READY, Dar de baja → SCRAPPED). */
export const PROCESS_READY = 'READY'
export const PROCESS_SCRAPPED = 'SCRAPPED'
/** Zona que nunca es destino de una devolución ni del traslado al terminar (ZoneType RENTAL: posición EN-RENTA). */
export const RENTAL_ZONE_TYPES = ['RENTAL'] as const
/** Topes del servidor (RentalRules / RentalReturnService). */
export const RETURN_LIMITS = { notes: 1000, lineNotes: 500, comment: 500 } as const

// =====================================================================================================================
// Registrar devolución: equipos pendientes, borrador, validación y cuerpo
// =====================================================================================================================

/** Equipos que se pueden devolver: activos, despachados y sin devolver (los únicos que el servidor acepta). */
export function pendingReturnLines(rental: RentalDto | null | undefined): RentalLineDto[] {
  return (rental?.lines ?? []).filter((l) => l.isActive !== false && Boolean(l.dispatchedAtUtc) && !l.returnedAtUtc)
}

/** ¿Se puede registrar una devolución? Renta En renta con al menos un equipo despachado y sin devolver. */
export function canRegisterReturn(rental: RentalDto | null | undefined): boolean {
  return (rental?.rental?.statusCode ?? '').toUpperCase() === 'ON_RENT' && pendingReturnLines(rental).length > 0
}

/** Un equipo en el formulario de devolución. */
export interface ReturnLineDraft {
  lineId: number
  serialNumber: string
  sku: string
  productName: string
  /** Posición de donde salió (destino por defecto si no se elige otra). */
  fromBinCode: string
  /** ¿Va en esta devolución? (devolución parcial permitida). */
  include: boolean
  condition: string
  /** Posición de destino propia del equipo (null = la del encabezado o, sin ella, la de origen). */
  toBinId: number | null
  /** ¿Pasa por proceso? (sí por defecto: lo decide quien recibe). */
  requiresProcess: boolean
  notes: string
}

/** Equipos pendientes como borrador: todos incluidos, condición Buena, sin destino propio y con proceso. */
export function initialReturnLines(rental: RentalDto | null | undefined): ReturnLineDraft[] {
  return pendingReturnLines(rental).map((l) => ({
    lineId: l.id ?? 0,
    serialNumber: l.serialNumber ?? '',
    sku: l.sku ?? '',
    productName: l.productName ?? '',
    fromBinCode: l.fromBinCode ?? '',
    include: true,
    condition: CONDITION_GOOD,
    toBinId: null,
    requiresProcess: true,
    notes: '',
  }))
}

/** Encabezado del formulario de devolución. */
export interface ReturnHeaderDraft {
  /** 'YYYY-MM-DD' ('' = hoy, lo decide el servidor). */
  returnedOn: string
  reason: string
  notes: string
  estimatedPickupCost: number | null
  transportCurrency: string
  /** Destino común (null = cada equipo vuelve a su posición de origen, salvo el que tenga uno propio). */
  toBinId: number | null
}

export type ReturnIssueField = 'returnedOn' | 'reason' | 'notes' | 'estimatedPickupCost' | 'lines'

/** Problemas por campo (más uno por equipo en `lineNotes`, índice del borrador). */
export interface ReturnIssues {
  fields: Partial<Record<ReturnIssueField, RentalIssue>>
  lineNotes: Record<number, RentalIssue>
}

/**
 * Validación espejo de RentalReturnService: motivo obligatorio; con "Otro", notas obligatorias; notas ≤ 1000 y del equipo ≤ 500;
 * fecha no futura (día de la compañía) ni anterior al inicio de la renta; costo de recogido no negativo; al menos un equipo.
 */
export function returnIssues(
  header: ReturnHeaderDraft,
  lines: readonly ReturnLineDraft[],
  ctx: { today: string; startDate: string | null | undefined },
): ReturnIssues {
  const fields: ReturnIssues['fields'] = returnHeaderIssues(header, ctx)
  const lineNotes: Record<number, RentalIssue> = {}
  if (!lines.some((l) => l.include)) fields.lines = { code: 'noLines' }
  lines.forEach((l, i) => {
    if (l.include && (l.notes ?? '').trim().length > RETURN_LIMITS.lineNotes) lineNotes[i] = { code: 'lineNotesTooLong' }
  })
  return { fields, lineNotes }
}

/** Solo el encabezado (lo que valida el formulario): motivo, notas, fecha y costo de recogido. */
export function returnHeaderIssues(
  header: Omit<ReturnHeaderDraft, 'toBinId'>,
  ctx: { today: string; startDate: string | null | undefined },
): Partial<Record<Exclude<ReturnIssueField, 'lines'>, RentalIssue>> {
  const fields: Partial<Record<ReturnIssueField, RentalIssue>> = {}
  const reason = header.reason.trim().toUpperCase()
  if (!reason) fields.reason = { code: 'reasonRequired' }
  const notes = header.notes.trim()
  if (notes.length > RETURN_LIMITS.notes) fields.notes = { code: 'notesTooLong' }
  else if (reason === REASON_OTHER && !notes) fields.notes = { code: 'otherNeedsNotes' }
  const day = header.returnedOn
  if (day) {
    if (ctx.today && day > ctx.today) fields.returnedOn = { code: 'returnedOnFuture' }
    else if (ctx.startDate && day < ctx.startDate) fields.returnedOn = { code: 'returnedOnBeforeStart', params: { date: ctx.startDate } }
  }
  if (header.estimatedPickupCost !== null && (Number.isNaN(header.estimatedPickupCost) || header.estimatedPickupCost < 0))
    fields.estimatedPickupCost = { code: Number.isNaN(header.estimatedPickupCost) ? 'numberInvalid' : 'negativePickupCost' }
  return fields
}

export function hasReturnIssues(issues: ReturnIssues): boolean {
  return Object.keys(issues.fields).length > 0 || Object.keys(issues.lineNotes).length > 0
}

/** ¿Anticipada? Antes de la fecha de recogido vigente (dato calculado, como `isEarly` del servidor). */
export function isEarlyReturn(returnedOn: string | null | undefined, pickupDate: string | null | undefined, today?: string): boolean {
  const day = returnedOn || today || ''
  return Boolean(day && pickupDate && day < pickupDate)
}

/**
 * Cuerpo del POST: solo los equipos incluidos, por número de serie (lo que espera el servidor); textos vacíos → null; el costo
 * con su moneda solo si hay costo (la moneda vacía = la de la compañía); el destino común en `toBinId` y el propio por equipo.
 */
export function returnBody(header: ReturnHeaderDraft, lines: readonly ReturnLineDraft[], rowVersion: string | null | undefined): RentalReturnCreateRequest {
  return {
    reason: header.reason,
    returnedOn: header.returnedOn || null,
    notes: header.notes.trim() || null,
    toBinId: header.toBinId,
    estimatedPickupCost: header.estimatedPickupCost,
    transportCurrency: header.estimatedPickupCost !== null ? header.transportCurrency || null : null,
    rowVersion: rowVersion ?? null,
    lines: lines
      .filter((l) => l.include)
      .map((l) => ({
        serialNumber: l.serialNumber,
        condition: l.condition || CONDITION_GOOD,
        toBinId: l.toBinId,
        requiresProcess: l.requiresProcess,
        notes: l.notes.trim() || null,
      })),
  }
}

// =====================================================================================================================
// Lista de devoluciones: filtros, URL y consulta
// =====================================================================================================================

/** '' = todas; 'true' = solo anticipadas; 'false' = solo al término. */
export type EarlyFilter = '' | 'true' | 'false'

export interface ReturnFilterState {
  reasons: string[]
  clientPublicId: string | null
  /** Renta de origen (enlace "Ver devoluciones" de la ficha de la renta). */
  rentalPublicId: string | null
  /** Fecha de devolución ('YYYY-MM-DD' o ''). */
  range: { from: string; to: string }
  early: EarlyFilter
  search: string
}

export const EMPTY_RETURN_FILTERS: ReturnFilterState = { reasons: [], clientPublicId: null, rentalPublicId: null, range: { from: '', to: '' }, early: '', search: '' }

const YMD = /^\d{4}-\d{2}-\d{2}$/
const CODE = /^[A-Z_]+$/

function codesOf(params: URLSearchParams, name: string): string[] {
  const out = params
    .getAll(name)
    .flatMap((v) => v.split(','))
    .map((v) => v.trim().toUpperCase())
    .filter((v) => CODE.test(v))
  return [...new Set(out)]
}

/** Filtros iniciales desde la URL (una vez al montar): `reason` (repetible), `clientPublicId`, `rentalPublicId`, `from`, `to`, `early`, `search`. */
export function returnFiltersFromUrl(params: URLSearchParams): ReturnFilterState {
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const early = (params.get('early') ?? '').toLowerCase()
  return {
    reasons: codesOf(params, 'reason'),
    clientPublicId: params.get('clientPublicId') || null,
    rentalPublicId: params.get('rentalPublicId') || null,
    range: { from: YMD.test(from) ? from : '', to: YMD.test(to) ? to : '' },
    early: early === 'true' || early === 'false' ? early : '',
    search: params.get('search') ?? '',
  }
}

/** Consulta de `GET /api/v1/rental-returns` (sin página: la pone quien llama). */
export function returnListQuery(f: ReturnFilterState, search: string = f.search): RentalReturnListQuery {
  const q: RentalReturnListQuery = {}
  if (f.reasons.length > 0) q.reason = f.reasons
  if (f.clientPublicId) q.clientPublicId = f.clientPublicId
  if (f.rentalPublicId) q.rentalPublicId = f.rentalPublicId
  if (f.range.from) q.from = f.range.from
  if (f.range.to) q.to = f.range.to
  if (f.early) q.early = f.early === 'true'
  const text = search.trim()
  if (text) q.search = text
  return q
}

// =====================================================================================================================
// Cola de proceso: filtros, URL, consulta, días en proceso y estatus para avanzar
// =====================================================================================================================

/** Abiertos (por defecto), terminados (Lista o Dada de baja) o todos. */
export type OpenFilter = 'open' | 'finished' | 'all'

export interface ProcessFilterState {
  status: string[]
  open: OpenFilter
  warehousePublicId: string | null
  search: string
}

export const DEFAULT_PROCESS_FILTERS: ProcessFilterState = { status: [], open: 'open', warehousePublicId: null, search: '' }

/** Filtros iniciales desde la URL: `status` (repetible), `open=true|false|all`, `warehousePublicId`, `search` (p. ej. la serie). */
export function processFiltersFromUrl(params: URLSearchParams): ProcessFilterState {
  const open = (params.get('open') ?? '').toLowerCase()
  return {
    status: codesOf(params, 'status'),
    open: open === 'false' ? 'finished' : open === 'all' ? 'all' : 'open',
    warehousePublicId: params.get('warehousePublicId') || null,
    search: params.get('search') ?? '',
  }
}

/** Consulta de `GET /api/v1/rental-processes` (sin página). */
export function processListQuery(f: ProcessFilterState, search: string = f.search): RentalProcessListQuery {
  const q: RentalProcessListQuery = {}
  if (f.status.length > 0) q.status = f.status
  if (f.open !== 'all') q.open = f.open === 'open'
  if (f.warehousePublicId) q.warehousePublicId = f.warehousePublicId
  const text = search.trim()
  if (text) q.search = text
  return q
}

/** Días de calendario entre dos 'YYYY-MM-DD' (b − a). */
function dayDiff(a: string, b: string): number {
  return Math.round((Date.parse(`${b}T00:00:00Z`) - Date.parse(`${a}T00:00:00Z`)) / 86_400_000)
}

/**
 * Días en proceso (espejo de RentalAnalyticsRules.DaysInProcess): del día de inicio al de fin, o a hoy si sigue abierto, en
 * días de la compañía; nunca negativo. `localDay` convierte un instante del API al día local (`localDayOf`).
 */
export function daysInProcess(
  p: Pick<RentalProcessDto, 'startedAtUtc' | 'completedAtUtc'>,
  today: string,
  localDay: (iso: string | null | undefined) => string | null,
): number | null {
  const start = localDay(p.startedAtUtc)
  const end = p.completedAtUtc ? localDay(p.completedAtUtc) : today
  if (!start || !end || !YMD.test(start) || !YMD.test(end)) return null
  return Math.max(0, dayDiff(start, end))
}

/**
 * Estatus que se ofrecen en "Avanzar": los HABILITADOS que no son terminales (Lista y Dada de baja tienen su acción propia) ni el
 * actual, en el orden de la compañía. El motor del servidor decide si el salto es legal (422 con su mensaje si no).
 */
export function advanceOptions(statuses: readonly StatusOption[], currentCode: string | null | undefined): StatusOption[] {
  const current = (currentCode ?? '').toUpperCase()
  return [...statuses]
    .filter((s) => s.isEnabled && s.stageKind !== StageKinds.Terminal && s.code.toUpperCase() !== current)
    .sort((a, b) => a.sortOrder - b.sortOrder)
}

/** Paso sugerido: el siguiente del pipeline habilitado después del actual (si el actual es un paso); si no, ninguno. */
export function suggestedAdvance(statuses: readonly StatusOption[], currentCode: string | null | undefined): string {
  const sorted = [...statuses].filter((s) => s.isEnabled).sort((a, b) => a.sortOrder - b.sortOrder)
  const current = sorted.find((s) => s.code.toUpperCase() === (currentCode ?? '').toUpperCase())
  if (!current || current.stageKind !== StageKinds.Pipeline) return ''
  return sorted.find((s) => s.sortOrder > current.sortOrder && s.stageKind === StageKinds.Pipeline)?.code ?? ''
}

/** Confirmación fuerte de la baja: hay que escribir la serie del equipo (sin espacios a los lados ni distinguir mayúsculas). */
export function scrapConfirmed(typed: string, serial: string | null | undefined): boolean {
  const s = (serial ?? '').trim().toUpperCase()
  return s !== '' && typed.trim().toUpperCase() === s
}
