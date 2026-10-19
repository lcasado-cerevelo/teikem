// Lote F-D1 — Tarifas, política de pago y viajes pagados del chofer. Lógica pura: esquemas zod espejo del servidor
// (DriverRateService, DriverTripService, manual 04 §8-§9), armado de los requests, filtros de las tablas, el ejemplo de la vista
// previa (el cálculo lo hace el SERVIDOR) y reglas de qué fila admite editar o cerrar. Sin React ni red.
import { z } from 'zod'
import type { AttemptRate, DeliveryRate, DriverRates, DriverTrip, DriverTripCreateRequest, PayoutPreviewRequest, TripRate } from './api'
import type { TFn } from './driverRules'

/** Tope de decimales y de magnitud de una tarifa: DECIMAL(18,4) → «menor que 100,000,000,000,000». */
export const RATE_DECIMALS = 4
export const RATE_LIMIT = 1e14
export const RATE_LIMIT_TEXT = '100,000,000,000,000'
export const TRIP_NOTES_MAX = 500
/** Tope de niveles de intento por compañía (CK_DriverPayPolicy_Levels). */
export const MAX_ATTEMPT_LEVELS = 20

/** Códigos de DriverPayoutFormula. */
export const PayoutFormulas = {
  Stack: 'DELIVERY_PLUS_ATTEMPTS',
  IncludesFirst: 'DELIVERY_INCLUDES_FIRST',
  FailedReplaces: 'FAILED_REPLACES_DELIVERY',
} as const

/** Estatus de un viaje pagado (DriverTripStatus). */
export const TripStatuses = { Open: 'OPEN', Settled: 'SETTLED', Cancelled: 'CANCELLED' } as const
export const TRIP_STATUS_DOMAIN = 'DriverTripStatus'
export const TRIP_ENTITY_TYPE = 'DRIVER_TRIP'

// ---------------------------------------------------------------- tarifa

/** Mensaje de la tarifa inválida (negativa, con demasiados decimales o fuera de rango) o null. Espejo de `ValidateRate`. */
export function rateIssue(rate: number | null | undefined, t: TFn): string | null {
  if (rate == null || Number.isNaN(rate)) return t('drivers.rates.errors.rateRequired')
  if (rate < 0) return t('drivers.rates.errors.rateMin')
  if (Number(rate.toFixed(RATE_DECIMALS)) !== rate || Math.abs(rate) >= RATE_LIMIT) return t('drivers.rates.errors.rateDecimals', { decimals: RATE_DECIMALS, limit: RATE_LIMIT_TEXT })
  return null
}

export interface RateValues {
  serviceType: string
  packageType: string
  /** Id del tipo de servicio especial (tarifa por viaje) como texto; '' = sin elegir. */
  tripTypeId: string
  rate: number | null
  /** 'YYYY-MM-DD'. Solo se pide al editar; en el alta el servidor usa hoy. */
  effectiveFrom: string
  /** Lote 129: código del nivel de zona de la tarifa; '' = general. */
  zoneGroup: string
}

export const emptyRateValues = (effectiveFrom: string): RateValues => ({ serviceType: '', packageType: '', tripTypeId: '', rate: null, effectiveFrom, zoneGroup: '' })

export interface RateSchemaOptions {
  /** 'delivery' exige servicio y paquete; 'trip' exige el tipo de viaje; false = solo la tarifa (editar o intento). */
  needs: 'delivery' | 'trip' | false
  /** Se pide la fecha «Vigente desde» (editar / fijar el intento). */
  needsDate: boolean
  /** «Hoy» de la compañía: ninguna fecha puede ser anterior (el historial de tarifas no se reescribe). */
  today: string
}

export function rateSchema(t: TFn, opts: RateSchemaOptions) {
  return z
    .object({
      serviceType: z.string(),
      packageType: z.string(),
      tripTypeId: z.string(),
      rate: z.number(t('drivers.rates.errors.rateRequired')).nullable(),
      effectiveFrom: z.string(),
      zoneGroup: z.string(),
    })
    .superRefine((v, ctx) => {
      if (opts.needs === 'delivery') {
        if (!v.serviceType) ctx.addIssue({ code: 'custom', path: ['serviceType'], message: t('drivers.rates.errors.keysRequired') })
        if (!v.packageType) ctx.addIssue({ code: 'custom', path: ['packageType'], message: t('drivers.rates.errors.keysRequired') })
      }
      if (opts.needs === 'trip' && !v.tripTypeId) ctx.addIssue({ code: 'custom', path: ['tripTypeId'], message: t('drivers.rates.errors.tripTypeRequired') })
      const problem = rateIssue(v.rate, t)
      if (problem) ctx.addIssue({ code: 'custom', path: ['rate'], message: problem })
      if (opts.needsDate) {
        if (!v.effectiveFrom) ctx.addIssue({ code: 'custom', path: ['effectiveFrom'], message: t('drivers.rates.errors.dateRequired') })
        else if (v.effectiveFrom < opts.today) ctx.addIssue({ code: 'custom', path: ['effectiveFrom'], message: t('drivers.rates.errors.dateBeforeToday') })
      }
    })
}

export const buildDeliveryRateCreate = (v: RateValues) => ({ serviceType: v.serviceType, packageType: v.packageType, rate: v.rate ?? 0, zoneGroup: v.zoneGroup || null })
export const buildTripRateCreate = (v: RateValues) => ({ specialServiceTypeId: Number(v.tripTypeId), rate: v.rate ?? 0 })
/** Editar = nueva versión: la tarifa y desde cuándo rige (el servidor cierra la fila abierta ahí). */
export const buildRateUpdate = (v: RateValues) => ({ rate: v.rate ?? 0, effectiveFrom: v.effectiveFrom })

// ---------------------------------------------------------------- filas editables y filtros

/** Una fila admite editar o cerrar solo mientras está abierta (sin `effectiveTo`); una cerrada, aunque rija hasta una fecha futura, no. */
export const isOpenRow = (row: { effectiveTo?: string | null }): boolean => !row.effectiveTo

/** Tarifas por entrega según los filtros Servicio y Paquete (códigos; vacío = todos). */
export function filterDeliveryRows(rows: readonly DeliveryRate[], services: readonly string[], packages: readonly string[]): DeliveryRate[] {
  return rows.filter((r) => (services.length === 0 || services.includes(r.serviceTypeCode ?? '')) && (packages.length === 0 || packages.includes(r.packageTypeCode ?? '')))
}

/** Tarifas por viaje según el filtro «Tipo de viaje» (ids como texto; vacío = todos). */
export function filterTripRows(rows: readonly TripRate[], typeIds: readonly string[]): TripRate[] {
  return rows.filter((r) => typeIds.length === 0 || typeIds.includes(String(r.specialServiceTypeId)))
}

/** Opciones únicas {value,label} de un campo de las filas (para los filtros): ordenadas por etiqueta. */
export function uniqueOptions<T>(rows: readonly T[], pick: (row: T) => { value: string; label: string } | null): { value: string; label: string }[] {
  const seen = new Map<string, string>()
  for (const r of rows) {
    const o = pick(r)
    if (o && o.value && !seen.has(o.value)) seen.set(o.value, o.label)
  }
  return [...seen.entries()].map(([value, label]) => ({ value, label })).sort((a, b) => a.label.localeCompare(b.label, 'es'))
}

/** «sin tarifa» (el nivel no tiene fila vigente) es distinto de $0 capturado a propósito. */
export type AttemptCellKind = 'none' | 'open' | 'closed'
export function attemptCellKind(a: Pick<AttemptRate, 'id' | 'effectiveTo'>): AttemptCellKind {
  if (a.id == null) return 'none'
  return a.effectiveTo ? 'closed' : 'open'
}

// ---------------------------------------------------------------- vista previa del pago

/** Intentos del ejemplo de la maqueta: 2 intentos, el 1.º fallido y el 2.º entregado. */
export const PREVIEW_ATTEMPTS = [
  { number: 1, delivered: false },
  { number: 2, delivered: true },
]

/**
 * Cuerpo de la vista previa para un chofer: su tarifa de entrega vigente (la primera, como la maqueta: «solo para ilustrar») y las
 * tarifas vigentes de cada nivel de intento (un nivel sin tarifa no se manda: el servidor lo muestra «sin tarifa configurada»).
 * Sin chofer, o sin tarifas, el servidor calcula con ceros y notas.
 */
export function previewRequestFor(rates: DriverRates | undefined | null): PayoutPreviewRequest {
  const delivery = rates?.deliveryRates?.find((r) => r.isCurrent)
  const attemptRates = (rates?.attemptRates ?? []).filter((a) => a.id != null && a.isCurrent && a.rate != null).map((a) => ({ number: a.attemptNumber, rate: a.rate ?? 0 }))
  return { attempts: PREVIEW_ATTEMPTS.map((a) => ({ ...a })), deliveryRate: delivery?.rate ?? null, attemptRates }
}

// ---------------------------------------------------------------- viajes pagados

export interface TripValues {
  specialServiceTypeId: string
  tripDate: string
  notes: string
}

export const emptyTripValues = (today: string): TripValues => ({ specialServiceTypeId: '', tripDate: today, notes: '' })

export function tripSchema(t: TFn, today: string) {
  return z.object({
    specialServiceTypeId: z.string().min(1, t('drivers.trips.errors.typeRequired')),
    tripDate: z
      .string()
      .min(1, t('drivers.trips.errors.dateRequired'))
      .refine((d) => d <= today, t('drivers.trips.errors.dateFuture')),
    notes: z.string().max(TRIP_NOTES_MAX, t('drivers.trips.errors.notesMax', { max: TRIP_NOTES_MAX })),
  })
}

export function buildTripRequest(v: TripValues): DriverTripCreateRequest {
  return { specialServiceTypeId: Number(v.specialServiceTypeId), tripDate: v.tripDate, notes: v.notes.trim() || null }
}

/** Consulta de la lista de viajes: fechas (inclusivas) y estatus; sin estatus, solo los vigentes. */
export function buildTripQuery(f: { from: string; to: string; status: string }): { from?: string; to?: string; status?: string[] } {
  const q: { from?: string; to?: string; status?: string[] } = {}
  if (f.from) q.from = f.from
  if (f.to) q.to = f.to
  if (f.status) q.status = [f.status]
  return q
}

/** ¿Se ofrece «Cancelar» en el viaje? Solo abierto y vigente; el de una entrega especial lo intenta y muestra el 409 del servidor. */
export function canCancelTrip(trip: Pick<DriverTrip, 'statusCode' | 'isActive'>): boolean {
  return (trip.statusCode ?? '').toUpperCase() === TripStatuses.Open && trip.isActive
}
