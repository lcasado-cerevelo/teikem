// Lote F-D2 — Flota (Catálogo → Flota y mantenimiento). Lógica pura: esquemas zod espejo del servidor (VehicleService, VehicleRules,
// VehicleDocumentService, DispatchZoneService; manual 04 §1, §2.7), armado de los requests (alta y PATCH solo de lo cambiado),
// estado visible del vehículo, filtros de «Documentos por vencer», chip de días y texto claro de los motivos de disponibilidad.
// Sin React ni red.
import { z } from 'zod'
import type { TParams } from '../../kernel/i18n'
import type { AvailabilityIssue, ExpiringQuery, VehicleCreateRequest, VehicleDetail, VehicleDocument, VehicleDocumentRequest, VehicleListItem, VehiclePatchRequest } from './api'

/** Función de textos (`useT()` o `t`): los esquemas piden sus mensajes por clave para que la prueba los vea EXACTOS. */
export type TFn = (key: string, params?: TParams) => string

// ---- topes (= columnas de dbo.Vehicle / VehicleDocument / DispatchZone; los mismos del servidor) ----
export const VEHICLE_CODE_MAX = 30
export const PLATE_MAX = 20
export const MAKE_MAX = 60
export const MODEL_MAX = 60
export const VIN_MAX = 40
export const DOC_NUMBER_MAX = 80
export const ZONE_CODE_MAX = 20
export const ZONE_NAME_MAX = 120
export const MIN_MODEL_YEAR = 1900

/** Códigos del catálogo VehicleStatus. */
export const VehicleStatuses = { Active: 'ACTIVE', Maintenance: 'MAINTENANCE', Inactive: 'INACTIVE' } as const

/** Valor del filtro de la lista: sin filtro. */
export const FILTER_ANY = ''

/** Ventana por omisión de «Documentos por vencer» (días) y su tope (el servidor responde 400 fuera de 0..365). */
export const DEFAULT_WITHIN_DAYS = 30
export const MAX_WITHIN_DAYS = 365

// ---------------------------------------------------------------- pestañas

export const FLEET_TABS = ['vehicles', 'maintenance', 'workOrders', 'fuel', 'expiring', 'zones', 'availability'] as const
export type FleetTab = (typeof FLEET_TABS)[number]

/** La pestaña de `?tab=`: sin parámetro o con un valor desconocido, la primera. */
export function tabOf(raw: string | null): FleetTab {
  return FLEET_TABS.find((k) => k === raw) ?? 'vehicles'
}

// ---------------------------------------------------------------- estado visible

export type VehicleState = 'retired' | 'inactive' | 'maintenance' | 'available'

/**
 * Los estados distintos del vehículo, de más a menos fuerte: baja definitiva (estatus terminal INACTIVE), inactivo (casilla «Activo»
 * apagada), en mantenimiento (estatus MAINTENANCE); si no, disponible.
 */
export function vehicleState(v: Pick<VehicleListItem, 'statusCode' | 'isActive'>): VehicleState {
  const code = (v.statusCode ?? '').toUpperCase()
  if (code === VehicleStatuses.Inactive) return 'retired'
  if (!v.isActive) return 'inactive'
  if (code === VehicleStatuses.Maintenance) return 'maintenance'
  return 'available'
}

/** «tipo · propiedad · combustible» de la segunda línea de la fila (lo que falte no se pinta). */
export function vehicleMeta(v: Pick<VehicleListItem, 'vehicleType' | 'vehicleTypeCode' | 'ownership' | 'fuelType'>): string {
  return [v.vehicleType || v.vehicleTypeCode, v.ownership, v.fuelType].filter(Boolean).join(' · ')
}

// ---------------------------------------------------------------- números

/** Separador de miles con coma (como `N0` invariante del servidor) para el límite del mensaje de precisión. */
function thousands(n: number): string {
  return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ',')
}

/**
 * Espejo de `FleetRules.DecimalError(valor, precisión, escala)`: más decimales que la escala o un valor igual o mayor que 10^(enteros).
 * Devuelve los datos del mensaje del servidor («El valor admite como máximo {decimals} decimales y debe ser menor que {limit}.») o null.
 */
export function decimalIssue(value: number | null, precision: number, scale: number): { decimals: number; limit: string } | null {
  if (value === null || !Number.isFinite(value)) return null
  const limit = 10 ** (precision - scale)
  const tooManyDecimals = Number(value.toFixed(scale)) !== value
  const tooLarge = Math.abs(value) >= limit
  if (!tooManyDecimals && !tooLarge) return null
  return { decimals: scale, limit: thousands(limit) }
}

// ---------------------------------------------------------------- formulario del vehículo (alta y edición)

export interface VehicleValues {
  /** Solo en el alta (después es inmutable). */
  code: string
  plateNumber: string
  /** Código del catálogo; '' = sin elegir. */
  vehicleType: string
  ownership: string
  fuelType: string
  make: string
  model: string
  modelYear: number | null
  vin: string
  maxWeightKg: number | null
  maxVolumeM3: number | null
  maxStops: number | null
  currentOdometerKm: number | null
}

export function emptyVehicleValues(): VehicleValues {
  return {
    code: '',
    plateNumber: '',
    vehicleType: '',
    ownership: '',
    fuelType: '',
    make: '',
    model: '',
    modelYear: null,
    vin: '',
    maxWeightKg: null,
    maxVolumeM3: null,
    maxStops: null,
    currentOdometerKm: 0,
  }
}

export function vehicleValuesOf(v: VehicleDetail): VehicleValues {
  return {
    code: v.code ?? '',
    plateNumber: v.plateNumber ?? '',
    vehicleType: v.vehicleTypeCode ?? '',
    ownership: v.ownershipCode ?? '',
    fuelType: v.fuelTypeCode ?? '',
    make: v.make ?? '',
    model: v.model ?? '',
    modelYear: v.modelYear ?? null,
    vin: v.vin ?? '',
    maxWeightKg: v.maxWeightKg ?? null,
    maxVolumeM3: v.maxVolumeM3 ?? null,
    maxStops: v.maxStops ?? null,
    currentOdometerKm: v.currentOdometerKm ?? null,
  }
}

/** VIN como lo guarda el servidor: sin espacios y en mayúsculas. */
export function normalizeVin(raw: string): string {
  return raw.replace(/\s+/g, '').toUpperCase()
}

export interface VehicleSchemaOptions {
  /** true = alta (el código es obligatorio); false = edición (el código no se valida). */
  creating: boolean
  /** Valores guardados (solo edición): el servidor lee `null` como «no cambiar», así que un número que ya existe no se puede vaciar. */
  original?: VehicleValues
  /** Año en curso de la compañía (el máximo del modelo es ese año + 1). */
  currentYear: number
}

export function vehicleSchema(t: TFn, opts: VehicleSchemaOptions) {
  return z
    .object({
      code: z.string(),
      plateNumber: z.string(),
      vehicleType: z.string(),
      ownership: z.string(),
      fuelType: z.string(),
      make: z.string(),
      model: z.string(),
      modelYear: z.number().nullable(),
      vin: z.string(),
      maxWeightKg: z.number().nullable(),
      maxVolumeM3: z.number().nullable(),
      maxStops: z.number().nullable(),
      currentOdometerKm: z.number().nullable(),
    })
    .superRefine((v, ctx) => {
      const add = (path: keyof VehicleValues, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })
      if (opts.creating) {
        const code = v.code.trim()
        if (!code) add('code', t('fleet.errors.codeRequired'))
        else if (code.length > VEHICLE_CODE_MAX) add('code', t('fleet.errors.codeMax', { max: VEHICLE_CODE_MAX }))
      }
      const tooLong = (field: 'plateNumber' | 'make' | 'model', max: number) => {
        if (v[field].trim().length > max) add(field, t('fleet.errors.maxChars', { max }))
      }
      tooLong('plateNumber', PLATE_MAX)
      tooLong('make', MAKE_MAX)
      tooLong('model', MODEL_MAX)
      if (normalizeVin(v.vin).length > VIN_MAX) add('vin', t('fleet.errors.vinMax'))

      const precision = (field: 'maxWeightKg' | 'maxVolumeM3' | 'currentOdometerKm', scale: number) => {
        const issue = decimalIssue(v[field], 12, scale)
        if (issue) add(field, t('fleet.errors.decimals', issue))
      }
      if (v.maxWeightKg !== null && v.maxWeightKg < 0) add('maxWeightKg', t('fleet.errors.capacityNegative'))
      else precision('maxWeightKg', 3)
      if (v.maxVolumeM3 !== null && v.maxVolumeM3 < 0) add('maxVolumeM3', t('fleet.errors.capacityNegative'))
      else precision('maxVolumeM3', 4)
      if (v.maxStops !== null && !Number.isInteger(v.maxStops)) add('maxStops', t('fleet.errors.maxStopsInteger'))
      else if (v.maxStops !== null && v.maxStops < 1) add('maxStops', t('fleet.errors.maxStopsMin'))
      if (v.currentOdometerKm !== null && v.currentOdometerKm < 0) add('currentOdometerKm', t('fleet.errors.odometerNegative'))
      else precision('currentOdometerKm', 1)
      if (v.modelYear !== null) {
        const max = opts.currentYear + 1
        if (!Number.isInteger(v.modelYear) || v.modelYear < MIN_MODEL_YEAR || v.modelYear > max) add('modelYear', t('fleet.errors.modelYear', { min: String(MIN_MODEL_YEAR), max: String(max) }))
      }

      // el PATCH lee `null` como «no cambiar»: un número que ya tiene valor no se puede dejar vacío
      const o = opts.original
      if (o) {
        for (const field of ['modelYear', 'maxWeightKg', 'maxVolumeM3', 'maxStops', 'currentOdometerKm'] as const) {
          if (o[field] !== null && v[field] === null) add(field, t('fleet.errors.cannotClear'))
        }
      }
    })
}

/** Alta: código en mayúsculas y recortado (el servidor también lo hace); texto vacío → null; VIN sin espacios. */
export function buildVehicleCreate(v: VehicleValues): VehicleCreateRequest {
  const text = (s: string) => s.trim() || null
  return {
    code: v.code.trim().toUpperCase(),
    plateNumber: text(v.plateNumber),
    vehicleType: v.vehicleType || null,
    ownership: v.ownership || null,
    fuelType: v.fuelType || null,
    make: text(v.make),
    model: text(v.model),
    modelYear: v.modelYear,
    vin: normalizeVin(v.vin) || null,
    maxWeightKg: v.maxWeightKg,
    maxVolumeM3: v.maxVolumeM3,
    maxStops: v.maxStops,
    currentOdometerKm: v.currentOdometerKm,
  }
}

export interface VehiclePatchPlan {
  request: VehiclePatchRequest
  /** false = no hay nada que mandar. */
  changed: boolean
}

/**
 * PATCH solo de lo cambiado + `rowVersion`. El servidor lee `null` como «no cambiar»: un texto se manda tal cual (`''` lo borra, nunca
 * `|| null`), un catálogo se borra con `''`, y los números solo se cambian (un número no se puede dejar vacío: lo impide el esquema).
 */
export function buildVehiclePatch(v: VehicleValues, vehicle: VehicleDetail): VehiclePatchPlan {
  const original = vehicleValuesOf(vehicle)
  const request: VehiclePatchRequest = { rowVersion: vehicle.rowVersion ?? null }
  let changed = false
  const setText = (field: 'plateNumber' | 'make' | 'model') => {
    const next = v[field].trim()
    if (next !== original[field]) {
      request[field] = next
      changed = true
    }
  }
  setText('plateNumber')
  setText('make')
  setText('model')
  for (const field of ['vehicleType', 'ownership', 'fuelType'] as const) {
    if (v[field] !== original[field]) {
      request[field] = v[field]
      changed = true
    }
  }
  if (normalizeVin(v.vin) !== original.vin) {
    request.vin = normalizeVin(v.vin)
    changed = true
  }
  for (const field of ['modelYear', 'maxWeightKg', 'maxVolumeM3', 'maxStops', 'currentOdometerKm'] as const) {
    if (v[field] !== original[field] && v[field] !== null) {
      request[field] = v[field]
      changed = true
    }
  }
  return { request, changed }
}

// ---------------------------------------------------------------- documentos del vehículo

export interface DocumentValues {
  /** Código del catálogo VehicleDocType. */
  docType: string
  docNumber: string
  /** 'YYYY-MM-DD' o ''. */
  issuedDate: string
  expiryDate: string
}

export const emptyDocumentValues = (): DocumentValues => ({ docType: '', docNumber: '', issuedDate: '', expiryDate: '' })

export function documentValuesOf(d: VehicleDocument): DocumentValues {
  return { docType: d.docTypeCode ?? '', docNumber: d.docNumber ?? '', issuedDate: d.issuedDate ?? '', expiryDate: d.expiryDate ?? '' }
}

export function documentSchema(t: TFn) {
  return z
    .object({
      docType: z.string().min(1, t('fleet.errors.docTypeRequired')),
      docNumber: z.string().trim().max(DOC_NUMBER_MAX, t('fleet.errors.maxChars', { max: DOC_NUMBER_MAX })),
      issuedDate: z.string(),
      expiryDate: z.string(),
    })
    .superRefine((v, ctx) => {
      // el servidor reporta este error en `expiryDate`
      if (v.issuedDate && v.expiryDate && v.expiryDate < v.issuedDate) ctx.addIssue({ code: 'custom', path: ['expiryDate'], message: t('fleet.errors.datesOrder') })
    })
}

/**
 * Documento: alta con todo lo capturado; edición solo con lo cambiado. Una fecha que se vacía va con `clearIssuedDate` /
 * `clearExpiryDate` (el servidor lee `null` como «no cambiar»); el número vacío se manda como `''` (lo borra).
 */
export function buildDocumentRequest(v: DocumentValues, original?: DocumentValues): VehicleDocumentRequest {
  const req: VehicleDocumentRequest = {}
  if (!original || v.docType !== original.docType) req.docType = v.docType
  if (!original || v.docNumber.trim() !== original.docNumber) req.docNumber = v.docNumber.trim()
  if (v.issuedDate) {
    if (!original || v.issuedDate !== original.issuedDate) req.issuedDate = v.issuedDate
  } else if (original?.issuedDate) req.clearIssuedDate = true
  if (v.expiryDate) {
    if (!original || v.expiryDate !== original.expiryDate) req.expiryDate = v.expiryDate
  } else if (original?.expiryDate) req.clearExpiryDate = true
  return req
}

/** ¿Hay algo que mandar al editar un documento? (todas las claves son opcionales). */
export function hasDocumentChanges(req: VehicleDocumentRequest): boolean {
  return Object.keys(req).length > 0
}

// ---------------------------------------------------------------- zonas de despacho

export interface ZoneValues {
  /** Solo en el alta (después es inmutable). */
  code: string
  name: string
  /** Lote 129: id del nivel de zona como texto; '' = sin nivel. */
  zoneGroupId: string
}

export const emptyZoneValues = (): ZoneValues => ({ code: '', name: '', zoneGroupId: '' })

export function zoneSchema(t: TFn, opts: { creating: boolean }) {
  return z
    .object({
      code: z.string(),
      name: z.string().trim().max(ZONE_NAME_MAX, t('fleet.errors.zoneNameMax', { max: ZONE_NAME_MAX })),
      zoneGroupId: z.string().default(''),
    })
    .superRefine((v, ctx) => {
      if (!opts.creating) return
      const code = v.code.trim()
      if (!code) ctx.addIssue({ code: 'custom', path: ['code'], message: t('fleet.errors.zoneCodeRequired') })
      else if (code.length > ZONE_CODE_MAX) ctx.addIssue({ code: 'custom', path: ['code'], message: t('fleet.errors.codeMax', { max: ZONE_CODE_MAX }) })
    })
}

/** Alta: código en mayúsculas y recortado; el nombre es opcional. */
export function buildZoneCreate(v: ZoneValues): { code: string; name: string; zoneGroupId: number | null } {
  return { code: v.code.trim().toUpperCase(), name: v.name.trim(), zoneGroupId: v.zoneGroupId ? Number(v.zoneGroupId) : null }
}

// ---------------------------------------------------------------- documentos por vencer

export interface ExpiringFilters {
  withinDays: number
  includeExpired: boolean
  /** Códigos: REGISTRATION, INSURANCE, INSPECTION, PERMIT, LICENSE, CERTIFICATION. Vacío = todos. */
  docTypes: readonly string[]
  /** VEHICLE y/o DRIVER. Vacío = ambos. */
  entities: readonly string[]
}

export const DOC_KIND_LICENSE = 'LICENSE'
export const DOC_KIND_CERTIFICATION = 'CERTIFICATION'

/** Los filtros del panel como consulta del API (los arreglos vacíos no se mandan). */
export function buildExpiringQuery(f: ExpiringFilters): ExpiringQuery {
  return {
    withinDays: f.withinDays,
    includeExpired: f.includeExpired,
    ...(f.docTypes.length ? { docType: [...f.docTypes] } : {}),
    ...(f.entities.length ? { entity: [...f.entities] } : {}),
  }
}

/** Lo escrito en «Dentro de N días»: entero entre 0 y 365; vacío o fuera de rango → null (no se pide). */
export function parseWithinDays(text: string): number | null {
  const s = text.trim()
  if (!/^\d+$/.test(s)) return null
  const n = Number(s)
  return n >= 0 && n <= MAX_WITHIN_DAYS ? n : null
}

export interface DaysChipInfo {
  tone: 'fail' | 'warn' | 'route' | 'cap' | 'deliv'
  /** Clave de texto (`fleet.docs.*`) y parámetros. */
  key: string
  params?: { days: number }
}

/** Chip de la columna «Días» (maqueta): vencido en rojo, hasta 14 días en `route`, hasta 30 en `cap`, más en verde. */
export function daysChipInfo(days: number): DaysChipInfo {
  if (days < 0) return { tone: 'fail', key: 'fleet.docs.expired', params: { days: Math.abs(days) } }
  if (days === 0) return { tone: 'route', key: 'fleet.docs.today' }
  const key = days === 1 ? 'fleet.docs.daysOne' : 'fleet.docs.days'
  if (days <= 14) return { tone: 'route', key, params: { days } }
  if (days <= 30) return { tone: 'cap', key, params: { days } }
  return { tone: 'deliv', key, params: { days } }
}

// ---------------------------------------------------------------- disponibilidad

/** Códigos que bloquean (el recurso queda «No disponible») y los que solo avisan; manual 04 §4. */
export const BLOCKING_ISSUES = ['DRIVER_INACTIVE', 'DRIVER_STATUS', 'NO_VALID_LICENSE', 'VEHICLE_INACTIVE', 'VEHICLE_STATUS', 'VEHICLE_DOC_EXPIRED', 'WORK_ORDER_IN_PROGRESS'] as const
export const WARNING_ISSUES = ['CERT_EXPIRED', 'DOC_EXPIRING', 'VEHICLE_NO_DOCUMENTS'] as const

const ISO_DATE = '(\\d{4}-\\d{2}-\\d{2})'

/**
 * Texto claro de un motivo, por su código (las claves `fleet.avail.issue.*`). Los datos que trae el mensaje del servidor (estatus,
 * tipo de documento, fecha, número de la orden) se leen del mensaje; si no calzan, se muestra el mensaje del servidor tal cual.
 * `formatDate` pinta las fechas con el formato de la compañía.
 */
export function issueText(issue: AvailabilityIssue, t: TFn, formatDate: (iso: string) => string): string {
  const message = (issue.message ?? '').trim()
  const fallback = message || (issue.code ?? '')
  const match = (re: RegExp) => re.exec(message)
  switch (issue.code) {
    case 'DRIVER_INACTIVE':
    case 'VEHICLE_INACTIVE':
    case 'VEHICLE_NO_DOCUMENTS':
      return t(`fleet.avail.issue.${issue.code}`)
    case 'DRIVER_STATUS':
    case 'VEHICLE_STATUS': {
      const m = match(/'(.*)'/)
      return m ? t(`fleet.avail.issue.${issue.code}`, { status: m[1] }) : fallback
    }
    case 'NO_VALID_LICENSE': {
      const m = match(new RegExp(`^Licencia vencida el ${ISO_DATE}`))
      return m ? t('fleet.avail.issue.NO_VALID_LICENSE_EXPIRED', { date: formatDate(m[1]) }) : t('fleet.avail.issue.NO_VALID_LICENSE')
    }
    case 'VEHICLE_DOC_EXPIRED': {
      const m = match(new RegExp(`^(.*) vencido el ${ISO_DATE}`))
      return m ? t('fleet.avail.issue.VEHICLE_DOC_EXPIRED', { doc: m[1], date: formatDate(m[2]) }) : fallback
    }
    case 'WORK_ORDER_IN_PROGRESS': {
      const m = match(/^Orden de trabajo (.*) en proceso/)
      return m ? t('fleet.avail.issue.WORK_ORDER_IN_PROGRESS', { number: m[1] }) : fallback
    }
    case 'CERT_EXPIRED': {
      const m = match(new RegExp(`^Certificación (.*) vencida el ${ISO_DATE}`))
      return m ? t('fleet.avail.issue.CERT_EXPIRED', { doc: m[1], date: formatDate(m[2]) }) : fallback
    }
    case 'DOC_EXPIRING': {
      const lic = match(new RegExp(`^Licencia (.*) vence el ${ISO_DATE}`))
      if (lic) return t('fleet.avail.issue.DOC_EXPIRING_LICENSE', { doc: lic[1], date: formatDate(lic[2]) })
      const cert = match(new RegExp(`^Certificación (.*) vence el ${ISO_DATE}`))
      if (cert) return t('fleet.avail.issue.DOC_EXPIRING_CERT', { doc: cert[1], date: formatDate(cert[2]) })
      const doc = match(new RegExp(`^(.*) vence el ${ISO_DATE}`))
      return doc ? t('fleet.avail.issue.DOC_EXPIRING', { doc: doc[1], date: formatDate(doc[2]) }) : fallback
    }
    default:
      return fallback
  }
}

/** Motivos de un recurso: primero los que bloquean y luego los avisos (orden estable). */
export function sortedIssues(issues: readonly AvailabilityIssue[] | null | undefined): AvailabilityIssue[] {
  const list = [...(issues ?? [])]
  return [...list.filter((i) => i.blocking), ...list.filter((i) => !i.blocking)]
}
