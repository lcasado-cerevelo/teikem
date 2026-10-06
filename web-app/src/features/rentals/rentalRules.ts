// Lote F17 (Rentas F-R1) — lógica pura de las pantallas de rentas (contrato del servidor: docs/manual/11-rentas.md).
// Sin React: filtros de la lista y su URL, "vencida"/"por vencer" (datos calculados, nunca estatus), validaciones espejo de
// RentalRules.cs (devuelven un código que la pantalla traduce con `t('rentals.errors.<code>', params)`: en español es el
// mensaje exacto del servidor), equipos elegidos (series) y los cuerpos de alta, edición, tarifa y extensión.
import type { components, paths } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type RentalDto = Schemas['RentalDto']
export type RentalListItemDto = Schemas['RentalListItemDto']
export type RentalLineDto = Schemas['RentalLineDto']
export type RentalLineRateDto = Schemas['RentalLineRateDto']
export type RentalExtensionDto = Schemas['RentalExtensionDto']
export type RentalCreateRequest = Schemas['RentalCreateRequest']
export type RentalPatchRequest = Schemas['RentalPatchRequest']
export type RentalLinesAddRequest = Schemas['RentalLinesAddRequest']
export type RentalExtendRequest = Schemas['RentalExtendRequest']
export type RentalLineRateInput = Schemas['RentalLineRateInput']
export type RentalListQuery = NonNullable<paths['/api/v1/rentals']['get']['parameters']['query']>

/** Dominio de estatus y tipo de entidad (historial de estatus: `/status/history/RENTAL/{id}`). */
export const RENTAL_STATUS_DOMAIN = 'RentalStatus'
export const RENTAL_ENTITY_TYPE = 'RENTAL'
/** Abiertas = Programada o En renta (las únicas que pueden estar vencidas o por vencer). */
export const RENTAL_OPEN_STATUSES = ['SCHEDULED', 'ON_RENT'] as const
/** Frecuencias de cobro del servidor (`RentalBillingFrequency`); `ONE_TIME` se rotula "Fija". */
export const RATE_FREQUENCIES = ['DAILY', 'WEEKLY', 'MONTHLY', 'ONE_TIME'] as const
/** Ventana del aviso "por vencer" (RentalAnalyticsRules.DueSoonDays). */
export const RENTAL_DUE_SOON_DAYS = 7
/** Topes del servidor (RentalRules). */
export const RENTAL_LIMITS = { contractNumber: 80, notes: 1000, reason: 300, serial: 80, maxLines: 200, comment: 500 } as const

/** Código de un mensaje de validación + sus parámetros (se traduce con `rentals.errors.<code>`). */
export interface RentalIssue {
  code: string
  params?: Record<string, string | number>
}

export function isOpenRental(statusCode: string | null | undefined): boolean {
  return (RENTAL_OPEN_STATUSES as readonly string[]).includes((statusCode ?? '').toUpperCase())
}

// =====================================================================================================================
// Lista: filtros, URL y consulta
// =====================================================================================================================

export interface RentalFilterState {
  /** Códigos de estatus (vacío = todos). */
  status: string[]
  clientPublicId: string | null
  /** "Vencen en N días" como texto del campo ('' = sin filtro). */
  dueWithinDays: string
  /** Solo vencidas. */
  overdue: boolean
  /** Buscador libre (número de renta o de contrato, cliente, localidad o serie). */
  search: string
}

export const EMPTY_RENTAL_FILTERS: RentalFilterState = { status: [], clientPublicId: null, dueWithinDays: '', overdue: false, search: '' }

/** Días de "vencen en N días": entero de 0 a 3650; otro texto → null (sin filtro). */
export function parseDueDays(text: string | null | undefined): number | null {
  const s = (text ?? '').trim()
  if (!/^\d{1,4}$/.test(s)) return null
  const n = Number(s)
  return n <= 3650 ? n : null
}

/**
 * Filtros iniciales desde la URL (se leen UNA vez al montar): `status` (repetible o con comas), `clientPublicId`,
 * `dueWithinDays`, `overdue=true` y `search`. Así llega "Ver todos" de "Necesita tu atención"
 * (`?dueWithinDays=7&overdue=true`). Lo desconocido se ignora.
 */
export function rentalFiltersFromUrl(params: URLSearchParams): RentalFilterState {
  const status = params
    .getAll('status')
    .flatMap((v) => v.split(','))
    .map((v) => v.trim().toUpperCase())
    .filter((v) => /^[A-Z_]+$/.test(v))
  const due = parseDueDays(params.get('dueWithinDays'))
  return {
    status: [...new Set(status)],
    clientPublicId: params.get('clientPublicId') || null,
    dueWithinDays: due === null ? '' : String(due),
    overdue: (params.get('overdue') ?? '').toLowerCase() === 'true',
    search: params.get('search') ?? '',
  }
}

/** Consulta de `GET /api/v1/rentals` (la misma para la tabla y Exportar, sin página: la pone quien llama). */
export function rentalListQuery(f: RentalFilterState, search: string = f.search): RentalListQuery {
  const due = parseDueDays(f.dueWithinDays)
  const q: RentalListQuery = {}
  if (f.status.length > 0) q.status = f.status
  if (f.clientPublicId) q.clientPublicId = f.clientPublicId
  if (due !== null) q.dueWithinDays = due
  if (f.overdue) q.overdue = true
  const text = search.trim()
  if (text) q.search = text
  return q
}

/** Lote F18 — tarjetas de resumen de la lista: En renta hoy, por vencer (7 días) y vencidas. */
export type RentalSummaryKey = 'onRent' | 'dueSoon' | 'overdue'

/** Filtros que aplica cada tarjeta (los demás quedan vacíos). */
export function summaryFilters(key: RentalSummaryKey, soonDays: number = RENTAL_DUE_SOON_DAYS): RentalFilterState {
  switch (key) {
    case 'onRent':
      return { ...EMPTY_RENTAL_FILTERS, status: ['ON_RENT'] }
    case 'dueSoon':
      return { ...EMPTY_RENTAL_FILTERS, dueWithinDays: String(soonDays) }
    case 'overdue':
      return { ...EMPTY_RENTAL_FILTERS, overdue: true }
  }
}

/** Tarjeta cuyo filtro es EXACTAMENTE el activo (para `aria-pressed`); null si los filtros son otros. */
export function summaryCardOf(f: RentalFilterState, soonDays: number = RENTAL_DUE_SOON_DAYS): RentalSummaryKey | null {
  const keys: RentalSummaryKey[] = ['onRent', 'dueSoon', 'overdue']
  const same = (a: RentalFilterState, b: RentalFilterState) =>
    a.status.join(',') === b.status.join(',') &&
    a.clientPublicId === b.clientPublicId &&
    (parseDueDays(a.dueWithinDays) ?? -1) === (parseDueDays(b.dueWithinDays) ?? -1) &&
    a.overdue === b.overdue &&
    a.search.trim() === b.search.trim()
  return keys.find((k) => same(f, summaryFilters(k, soonDays))) ?? null
}

export function hasRentalFilters(f: RentalFilterState): boolean {
  return f.status.length > 0 || Boolean(f.clientPublicId) || parseDueDays(f.dueWithinDays) !== null || f.overdue || f.search.trim() !== ''
}

// =====================================================================================================================
// "Vencida" y "por vencer" (calculados por el servidor: `daysToPickup`, `isOverdue`)
// =====================================================================================================================

/** overdue = vencida; today = se recoge hoy; soon = en ≤ 7 días; later = abierta con más tiempo; null = no abierta. */
export type DueState = 'overdue' | 'today' | 'soon' | 'later'

export function dueState(
  r: { statusCode?: string | null; daysToPickup?: number | null; isOverdue?: boolean | null },
  soonDays: number = RENTAL_DUE_SOON_DAYS,
): DueState | null {
  if (!isOpenRental(r.statusCode)) return null
  const days = r.daysToPickup ?? 0
  if (r.isOverdue || days < 0) return 'overdue'
  if (days === 0) return 'today'
  return days <= soonDays ? 'soon' : 'later'
}

/** Clave i18n y parámetros del texto de vencimiento ("Vencida hace 3 días", "Se recoge hoy", "Vence en 1 día"…). */
export function dueTextKey(state: DueState, daysToPickup: number | null | undefined): { key: string; params: { days: number } } {
  const days = Math.abs(daysToPickup ?? 0)
  switch (state) {
    case 'overdue':
      return { key: days === 1 ? 'rentals.due.overdueOne' : 'rentals.due.overdue', params: { days } }
    case 'today':
      return { key: 'rentals.due.today', params: { days } }
    case 'soon':
      return { key: days === 1 ? 'rentals.due.soonOne' : 'rentals.due.soon', params: { days } }
    default:
      return { key: 'rentals.due.later', params: { days } }
  }
}

/** Texto del vencimiento ya traducido (tabla, tarjeta, exportación y ficha). */
export function dueLabel(t: (key: string, params?: Record<string, string | number>) => string, state: DueState, daysToPickup: number | null | undefined): string {
  const { key, params } = dueTextKey(state, daysToPickup)
  return t(key, params)
}

// =====================================================================================================================
// Validaciones espejo del servidor (mismos mensajes; la pantalla los traduce)
// =====================================================================================================================

const YMD = /^\d{4}-\d{2}-\d{2}$/

/** 'YYYY-MM-DD' válido. */
export function isIsoDay(v: string | null | undefined): v is string {
  return typeof v === 'string' && YMD.test(v) && !Number.isNaN(Date.parse(`${v}T00:00:00Z`))
}

/** Suma días a un 'YYYY-MM-DD' (calendario, sin zona). */
export function addDays(day: string, days: number): string {
  const d = new Date(`${day}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}

/** 400 de RentalRules.ValidateDates: recogido antes del inicio. */
export function datesIssue(startDate: string | null | undefined, pickupDate: string | null | undefined): RentalIssue | null {
  if (!isIsoDay(startDate) || !isIsoDay(pickupDate)) return null
  return pickupDate < startDate ? { code: 'pickupBeforeStart' } : null
}

/** Validación de la extensión (RentalService.ExtendAsync): nueva fecha obligatoria y posterior a la vigente; motivo obligatorio (≤ 300). */
export function extensionIssues(input: { currentPickupDate: string | null | undefined; newPickupDate: string | null | undefined; reason: string | null | undefined }): {
  newPickupDate?: RentalIssue
  reason?: RentalIssue
} {
  const out: { newPickupDate?: RentalIssue; reason?: RentalIssue } = {}
  if (!isIsoDay(input.newPickupDate)) out.newPickupDate = { code: 'newPickupRequired' }
  else if (isIsoDay(input.currentPickupDate) && input.newPickupDate <= input.currentPickupDate)
    out.newPickupDate = { code: 'newPickupNotLater', params: { date: input.currentPickupDate } }
  const reason = (input.reason ?? '').trim()
  if (!reason) out.reason = { code: 'reasonRequired' }
  else if (reason.length > RENTAL_LIMITS.reason) out.reason = { code: 'reasonTooLong' }
  return out
}

/** Tarifa capturada en pantalla: frecuencia ('' = sin tarifa), monto (null = vacío) y moneda ('' = la de la compañía). */
export interface RateDraft {
  frequency: string
  amount: number | null
  currency: string
}

export const EMPTY_RATE: RateDraft = { frequency: '', amount: null, currency: '' }

/** ¿La tarifa está vacía (no se manda)? */
export function isEmptyRate(r: RateDraft): boolean {
  return !r.frequency && r.amount === null
}

/** Validación de una tarifa (RentalService.NormalizeRateAsync): vacía = sin tarifa; si no, frecuencia y monto ≥ 0. */
export function rateIssue(r: RateDraft): { field: 'frequency' | 'amount'; issue: RentalIssue } | null {
  if (isEmptyRate(r)) return null
  if (!r.frequency) return { field: 'frequency', issue: { code: 'frequencyRequired' } }
  if (r.amount === null || Number.isNaN(r.amount)) return { field: 'amount', issue: { code: 'rateAmountRequired' } }
  if (r.amount < 0) return { field: 'amount', issue: { code: 'negativeRate' } }
  return null
}

/** Tarifa para el API (null si está vacía). */
export function rateInput(r: RateDraft): RentalLineRateInput | null {
  if (isEmptyRate(r)) return null
  return { frequency: r.frequency, amount: r.amount, currency: r.currency || null }
}

/** ¿Una tarifa capturada es distinta de la vigente? (frecuencia, monto o moneda; la moneda vacía = la vigente). */
export function rateChanged(draft: RateDraft, current: RentalLineRateDto | null | undefined): boolean {
  if (isEmptyRate(draft)) return false
  if (!current) return true
  return (
    draft.frequency.toUpperCase() !== (current.frequencyCode ?? '').toUpperCase() ||
    draft.amount !== (current.amount ?? null) ||
    (draft.currency !== '' && draft.currency.toUpperCase() !== (current.currencyCode ?? '').toUpperCase())
  )
}

/** Tarifa vigente de un equipo como borrador (para editarla). */
export function rateDraftOf(rate: RentalLineRateDto | null | undefined): RateDraft {
  if (!rate?.frequencyCode) return { ...EMPTY_RATE }
  return { frequency: rate.frequencyCode, amount: rate.amount ?? null, currency: rate.currencyCode ?? '' }
}

// =====================================================================================================================
// Equipos (series) elegidos
// =====================================================================================================================

/** Un equipo elegido en pantalla (una serie). */
export interface PickedEquipment {
  productPublicId: string
  sku: string
  productName: string
  serialNumber: string
  binCode: string
  rate: RentalLineRateInput | null
}

/** Clave de comparación de una serie (sin espacios a los lados ni distinguir mayúsculas, como el servidor). */
export function serialKey(serial: string | null | undefined): string {
  return (serial ?? '').trim().toUpperCase()
}

/** Agrega equipos sin repetir series (las repetidas se devuelven aparte para avisar). */
export function addEquipment(current: readonly PickedEquipment[], incoming: readonly PickedEquipment[]): { list: PickedEquipment[]; duplicates: string[] } {
  const seen = new Set(current.map((e) => serialKey(e.serialNumber)))
  const list = [...current]
  const duplicates: string[] = []
  for (const e of incoming) {
    const k = serialKey(e.serialNumber)
    if (!k) continue
    if (seen.has(k)) {
      duplicates.push(e.serialNumber)
      continue
    }
    seen.add(k)
    list.push(e)
  }
  return { list, duplicates }
}

/**
 * Renglones del alta / de "agregar equipos": un renglón por producto y tarifa (las series del mismo producto con la misma
 * tarifa van juntas), en el orden en que se eligieron.
 */
export function equipmentLines(list: readonly PickedEquipment[]): RentalLinesAddRequest[] {
  const groups = new Map<string, RentalLinesAddRequest>()
  for (const e of list) {
    const key = `${e.productPublicId}|${JSON.stringify(e.rate ?? null)}`
    const g = groups.get(key)
    if (g) g.serialNumbers!.push(e.serialNumber)
    else groups.set(key, { productPublicId: e.productPublicId, serialNumbers: [e.serialNumber], ...(e.rate ? { rate: e.rate } : {}) })
  }
  return [...groups.values()]
}

/** Primer problema de una serie escaneada o tecleada: ya elegida, ya en la renta o no disponible en el almacén. */
export function scannedSerialIssue(
  serial: string,
  ctx: { picked: ReadonlySet<string>; available: ReadonlyMap<string, unknown>; warehouseCode: string },
): RentalIssue | null {
  const k = serialKey(serial)
  if (!k) return null
  if (ctx.picked.has(k)) return { code: 'serialAlreadyPicked', params: { serial: serial.trim() } }
  if (!ctx.available.has(k)) return { code: 'serialNotAvailable', params: { serial: serial.trim(), where: ctx.warehouseCode } }
  return null
}

/** Equipos activos de una renta (los que cuentan para no repetir series y para extender tarifas). */
export function activeLines(rental: RentalDto | null | undefined): RentalLineDto[] {
  return (rental?.lines ?? []).filter((l) => l.isActive !== false)
}

/** Estado de un equipo para la ficha: inactivo (quitado o renta cancelada), devuelto, despachado o por despachar. */
export type LineState = 'inactive' | 'returned' | 'dispatched' | 'pending'

export function lineState(line: RentalLineDto): LineState {
  if (line.isActive === false) return 'inactive'
  if (line.returnedAtUtc) return 'returned'
  if (line.dispatchedAtUtc) return 'dispatched'
  return 'pending'
}

// =====================================================================================================================
// Encabezado: alta y edición
// =====================================================================================================================

/** Valores del formulario del encabezado (alta y edición). */
export interface RentalHeaderValues {
  clientPublicId: string | null
  locationPublicId: string
  clientContactId: string
  warehousePublicId: string | null
  startDate: string
  pickupDate: string
  contractNumber: string
  contractSignedOn: string
  estimatedDeliveryCost: number | null
  transportCurrency: string
  notes: string
}

/** Cuerpo del alta (`POST /rentals`): textos vacíos → null; los equipos ya agrupados. */
export function createRentalBody(v: RentalHeaderValues, equipment: readonly PickedEquipment[]): RentalCreateRequest {
  const lines = equipmentLines(equipment)
  return {
    clientPublicId: v.clientPublicId,
    locationPublicId: v.locationPublicId || null,
    clientContactId: v.clientContactId ? Number(v.clientContactId) : null,
    warehousePublicId: v.warehousePublicId || null,
    startDate: v.startDate || null,
    pickupDate: v.pickupDate || null,
    contractNumber: v.contractNumber.trim() || null,
    contractSignedOn: v.contractSignedOn || null,
    estimatedDeliveryCost: v.estimatedDeliveryCost,
    transportCurrency: v.estimatedDeliveryCost !== null ? v.transportCurrency || null : null,
    notes: v.notes.trim() || null,
    ...(lines.length > 0 ? { lines } : {}),
  }
}

/** Valores del formulario a partir de la ficha (edición). */
export function headerValuesOf(dto: RentalDto): RentalHeaderValues {
  const r = dto.rental ?? {}
  return {
    clientPublicId: r.clientPublicId ?? null,
    locationPublicId: r.locationPublicId ?? '',
    clientContactId: dto.clientContactId != null ? String(dto.clientContactId) : '',
    warehousePublicId: r.warehousePublicId ?? null,
    startDate: r.startDate ?? '',
    pickupDate: r.pickupDate ?? '',
    contractNumber: r.contractNumber ?? '',
    contractSignedOn: dto.contractSignedOn ?? '',
    estimatedDeliveryCost: dto.estimatedDeliveryCost ?? null,
    transportCurrency: dto.transportCurrencyCode ?? '',
    notes: dto.notes ?? '',
  }
}

/**
 * Cuerpo del PATCH con SOLO lo que cambió (el API lee `null` como "sin cambio"): `''` borra el contrato o las notas; quitar el
 * contacto, la fecha de firma o el costo manda su `clear…`. El cliente no se manda nunca (no se cambia). null = nada cambió.
 */
export function rentalPatchBody(dto: RentalDto, v: RentalHeaderValues): RentalPatchRequest | null {
  const before = headerValuesOf(dto)
  const body: RentalPatchRequest = {}
  if (v.locationPublicId && v.locationPublicId !== before.locationPublicId) body.locationPublicId = v.locationPublicId
  if (v.clientContactId !== before.clientContactId) {
    if (v.clientContactId) body.clientContactId = Number(v.clientContactId)
    else body.clearClientContact = true
  }
  if (v.warehousePublicId && v.warehousePublicId !== before.warehousePublicId) body.warehousePublicId = v.warehousePublicId
  if (v.startDate && v.startDate !== before.startDate) body.startDate = v.startDate
  if (v.pickupDate && v.pickupDate !== before.pickupDate) body.pickupDate = v.pickupDate
  if (v.contractNumber.trim() !== before.contractNumber.trim()) body.contractNumber = v.contractNumber.trim()
  if (v.contractSignedOn !== before.contractSignedOn) {
    if (v.contractSignedOn) body.contractSignedOn = v.contractSignedOn
    else body.clearContractSignedOn = true
  }
  if (v.estimatedDeliveryCost !== before.estimatedDeliveryCost) {
    if (v.estimatedDeliveryCost === null) body.clearEstimatedDeliveryCost = true
    else body.estimatedDeliveryCost = v.estimatedDeliveryCost
  }
  if (v.transportCurrency && v.transportCurrency !== before.transportCurrency && v.estimatedDeliveryCost !== null) body.transportCurrency = v.transportCurrency
  if (v.notes.trim() !== before.notes.trim()) body.notes = v.notes.trim()
  if (Object.keys(body).length === 0) return null
  return { ...body, rowVersion: dto.rowVersion ?? null }
}

/** Cuerpo de la extensión: tarifa nueva para los equipos elegidos (solo los que cambian, como pide el servidor). */
export function extendBody(
  dto: RentalDto,
  v: { newPickupDate: string; reason: string; rate: RateDraft; lineIds: readonly number[] },
): RentalExtendRequest {
  const rates = isEmptyRate(v.rate)
    ? []
    : activeLines(dto)
        .filter((l) => l.id != null && v.lineIds.includes(l.id) && rateChanged(v.rate, l.rate))
        .map((l) => ({ lineId: l.id!, frequency: v.rate.frequency, amount: v.rate.amount, currency: v.rate.currency || null }))
  return { newPickupDate: v.newPickupDate, reason: v.reason.trim(), ...(rates.length > 0 ? { rates } : {}), rowVersion: dto.rowVersion ?? null }
}
