// Lógica pura de los contratos del cliente (F-A2): validaciones espejo del servidor (ContractRules, RateService,
// SpecialServiceService), borrador del SLA, plan de guardado del modelo de facturación y armado de los requests.
// Sin React ni red, para probarla sola (contractRules.test.ts).
import { z } from 'zod'
import { ApiError } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import type { LookupOption } from '../../kernel/catalogs'

type Schemas = components['schemas']
export type ContractDetail = Schemas['ContractDetailDto']
export type ContractSummary = Schemas['ClientContractSummaryDto']
export type ContractPatchRequest = Schemas['ContractPatchRequest']
export type ContractCreateRequest = Schemas['ContractCreateRequest']
export type BillingModelRequest = Schemas['BillingModelRequest']
export type DispatchFeeRequest = Schemas['DispatchFeeRequest']
export type CodFeeRequest = Schemas['CodFeeRequest']
export type ServiceLevelUpsert = Schemas['ServiceLevelUpsert']
export type ServiceLevel = Schemas['ServiceLevelDto']
export type RateRow = Schemas['RateRowDto']
export type ExtraPieceRow = Schemas['ExtraPieceRowDto']
export type Tier = Schemas['TierDto']
export type SpecialService = Schemas['SpecialServiceDto']

/** Dominio de estatus y EntityType del contrato (CatalogDomains.ContractStatus / EntityTypes.Contract). */
export const CONTRACT_STATUS_DOMAIN = 'ContractStatus'
export const CONTRACT_ENTITY_TYPE = 'CONTRACT'

// ---- Límites del servidor ----
export const CONTRACT_TITLE_MAX = 200
export const CONTRACT_NUMBER_MAX = 40
export const SPECIAL_NAME_MAX = 120
/** Valor del selector de tipos de servicio especial que abre el campo del nombre nuevo. */
export const NEW_SPECIAL_TYPE = '__new__'
/** Tipos del cargo por COD (lookup PricingType: FIXED = monto por orden, PERCENT = por ciento del monto COD). */
export const COD_TYPES = ['FIXED', 'PERCENT'] as const
export type CodType = (typeof COD_TYPES)[number]
/** Primer número de pieza que puede ser «extra» (la pieza 1 va en la tarifa por servicio). */
export const MIN_EXTRA_PIECE = 2

/**
 * «Hoy» por omisión de «Vigente desde»: el servidor valida con su fecha UTC («no anterior a hoy»); si la compañía va
 * detrás de UTC (p. ej. Puerto Rico de noche) su «hoy» ya sería ayer para el servidor. Se usa el día más reciente.
 */
export function defaultEffectiveDate(tenantToday: string, now: Date = new Date()): string {
  const utc = serverToday(now)
  return utc > tenantToday ? utc : tenantToday
}

/** «Hoy» del servidor (fecha UTC): la vigencia de una nueva versión o de un cierre no puede ser anterior. */
export function serverToday(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10)
}

/** Nombre corto de un contrato para el selector y los títulos: «Número · Título». */
export function contractLabel(c: Pick<ContractSummary, 'contractNumber' | 'title'>): string {
  return [c.contractNumber, c.title].filter(Boolean).join(' · ')
}

/** Contrato que se abre por omisión: el vigente de la ficha o, sin él, el primero de la lista. */
export function defaultContractId(currentPublicId: string | null | undefined, contracts: readonly ContractSummary[]): string | null {
  if (currentPublicId && contracts.some((c) => c.publicId === currentPublicId)) return currentPublicId
  return contracts.find((c) => c.isCurrent)?.publicId ?? contracts[0]?.publicId ?? null
}

// ---- Datos generales del contrato ----
export interface ContractValues {
  contractNumber: string
  title: string
  startDate: string
  endDate: string
  autoRenew: boolean
  currency: string
  billingTrigger: string
  notes: string
}

export interface ContractMessages {
  titleRequired: string
  titleMax: string
  numberMax: string
  startRequired: string
  endBeforeStart: string
}

export function contractSchema(m: ContractMessages) {
  return z
    .object({
      contractNumber: z.string().trim().max(CONTRACT_NUMBER_MAX, m.numberMax),
      title: z.string().trim().min(1, m.titleRequired).max(CONTRACT_TITLE_MAX, m.titleMax),
      startDate: z.string().min(1, m.startRequired),
      endDate: z.string(),
      autoRenew: z.boolean(),
      currency: z.string(),
      billingTrigger: z.string(),
      notes: z.string(),
    })
    .superRefine((v, ctx) => {
      if (v.startDate && v.endDate && v.endDate < v.startDate) ctx.addIssue({ code: 'custom', path: ['endDate'], message: m.endBeforeStart })
    })
}

export function contractValuesOf(c: ContractDetail): ContractValues {
  return {
    contractNumber: c.contractNumber ?? '',
    title: c.title ?? '',
    startDate: c.startDate ?? '',
    endDate: c.endDate ?? '',
    autoRenew: c.autoRenew ?? false,
    currency: c.currency ?? '',
    billingTrigger: c.billingTrigger ?? '',
    notes: c.notes ?? '',
  }
}

export function emptyContractValues(today: string): ContractValues {
  return { contractNumber: '', title: '', startDate: today, endDate: '', autoRenew: false, currency: '', billingTrigger: '', notes: '' }
}

/**
 * PATCH de los datos generales: solo lo que cambió (el número no se edita). La fecha fin vacía con una anterior = `clearEndDate`;
 * las notas vacías (cadena vacía) las borran. Lleva el `rowVersion` de la ficha.
 */
export function buildContractPatch(v: ContractValues, original: ContractValues, rowVersion: string | null | undefined): ContractPatchRequest {
  const req: ContractPatchRequest = { rowVersion }
  if (v.title.trim() !== original.title) req.title = v.title.trim()
  if (v.startDate !== original.startDate) req.startDate = v.startDate
  if (v.endDate !== original.endDate) {
    if (v.endDate === '') req.clearEndDate = true
    else req.endDate = v.endDate
  }
  if (v.autoRenew !== original.autoRenew) req.autoRenew = v.autoRenew
  if (v.currency !== original.currency && v.currency !== '') req.currency = v.currency
  if (v.billingTrigger !== original.billingTrigger && v.billingTrigger !== '') req.billingTrigger = v.billingTrigger
  if (v.notes.trim() !== original.notes.trim()) req.notes = v.notes.trim()
  return req
}

/** POST /contracts (contracts.create): número vacío = lo genera el servidor; el SLA no se pide aquí (va en su pestaña). */
export function buildContractCreate(clientPublicId: string, v: ContractValues): ContractCreateRequest {
  return {
    clientPublicId,
    contractNumber: v.contractNumber.trim() || null,
    title: v.title.trim(),
    startDate: v.startDate,
    endDate: v.endDate || null,
    autoRenew: v.autoRenew,
    currency: v.currency || null,
    billingTrigger: v.billingTrigger || null,
    notes: v.notes.trim() || null,
  }
}

// ---- Modelo de facturación, cargo por despacho y cargo por COD ----
export const BILLING_FLAGS = ['billPerService', 'billExtraPiece', 'billDispatchFee', 'billCodFee', 'billSpecialServices'] as const
export type BillingFlag = (typeof BILLING_FLAGS)[number]

export interface BillingValues {
  billPerService: boolean
  billExtraPiece: boolean
  billDispatchFee: boolean
  billCodFee: boolean
  billSpecialServices: boolean
  dispatchAmount?: number | null
  codType: CodType
  codValue?: number | null
}

export function billingValuesOf(c: ContractDetail): BillingValues {
  const type = (c.codFee?.type ?? '').toUpperCase()
  return {
    billPerService: c.billingModel?.billPerService ?? false,
    billExtraPiece: c.billingModel?.billExtraPiece ?? false,
    billDispatchFee: c.billingModel?.billDispatchFee ?? false,
    billCodFee: c.billingModel?.billCodFee ?? false,
    billSpecialServices: c.billingModel?.billSpecialServices ?? false,
    dispatchAmount: c.dispatchFee ?? null,
    codType: type === 'PERCENT' ? 'PERCENT' : 'FIXED',
    codValue: c.codFee ? c.codFee.value ?? null : null,
  }
}

export interface BillingMessages {
  number: string
  dispatchMin: string
  dispatchRequired: string
  codFixedMin: string
  codPercentRange: string
  codRequired: string
}

/**
 * Montos no negativos; PERCENT entre 0 y 100. Con el check encendido el monto es obligatorio, salvo que el contrato ya
 * estuviera así sin monto (no se bloquea guardar otra cosa).
 */
export function billingSchema(m: BillingMessages, original: BillingValues) {
  return z
    .object({
      billPerService: z.boolean(),
      billExtraPiece: z.boolean(),
      billDispatchFee: z.boolean(),
      billCodFee: z.boolean(),
      billSpecialServices: z.boolean(),
      dispatchAmount: z.number(m.number).nullable(),
      codType: z.enum(COD_TYPES),
      codValue: z.number(m.number).nullable(),
    })
    .superRefine((v, ctx) => {
      if (v.billDispatchFee) {
        if (v.dispatchAmount == null) {
          if (!(original.billDispatchFee && original.dispatchAmount == null)) ctx.addIssue({ code: 'custom', path: ['dispatchAmount'], message: m.dispatchRequired })
        } else if (v.dispatchAmount < 0) ctx.addIssue({ code: 'custom', path: ['dispatchAmount'], message: m.dispatchMin })
      }
      if (v.billCodFee) {
        if (v.codValue == null) {
          if (!(original.billCodFee && original.codValue == null)) ctx.addIssue({ code: 'custom', path: ['codValue'], message: m.codRequired })
        } else if (v.codValue < 0) ctx.addIssue({ code: 'custom', path: ['codValue'], message: v.codType === 'PERCENT' ? m.codPercentRange : m.codFixedMin })
        else if (v.codType === 'PERCENT' && v.codValue > 100) ctx.addIssue({ code: 'custom', path: ['codValue'], message: m.codPercentRange })
      }
    })
}

export interface BillingPlan {
  flags?: BillingModelRequest
  dispatch?: DispatchFeeRequest
  cod?: CodFeeRequest
}

/**
 * Qué llamadas hacen falta para guardar: los checks que cambiaron (PATCH billing-model), el cargo por despacho y el de COD
 * solo si su check está marcado y el monto cambió. Desmarcar un check no borra el monto que ya tenía el contrato.
 */
export function planBillingSave(v: BillingValues, original: BillingValues): BillingPlan {
  const plan: BillingPlan = {}
  const flags: BillingModelRequest = {}
  for (const f of BILLING_FLAGS) if (v[f] !== original[f]) flags[f] = v[f]
  if (Object.keys(flags).length > 0) plan.flags = flags
  if (v.billDispatchFee && v.dispatchAmount != null && v.dispatchAmount !== original.dispatchAmount) plan.dispatch = { amount: v.dispatchAmount }
  if (v.billCodFee && v.codValue != null && (v.codValue !== original.codValue || v.codType !== original.codType)) plan.cod = { type: v.codType, value: v.codValue }
  return plan
}

// ---- Niveles de servicio (SLA) ----
export interface SlaRow {
  serviceType: string
  label: string
  // el esquema zod infiere estos campos como opcionales: se aceptan ausentes o null
  maxTransitHours?: number | null
  pickupWindowMin?: number | null
  onTimeTargetPct?: number | null
  penaltyAmount?: number | null
}
export interface SlaValues {
  levels: SlaRow[]
}
export const SLA_FIELDS = ['maxTransitHours', 'pickupWindowMin', 'onTimeTargetPct', 'penaltyAmount'] as const
export type SlaField = (typeof SLA_FIELDS)[number]

/** Un renglón por tipo de servicio del catálogo (más los niveles de tipos que ya no estén en él), con los valores guardados. */
export function slaRowsOf(types: readonly { code: string; label: string }[], levels: readonly ServiceLevel[] | null | undefined): SlaValues {
  const byCode = new Map((levels ?? []).map((l) => [(l.serviceType ?? '').toUpperCase(), l]))
  const rows: SlaRow[] = types.map((t) => {
    const l = byCode.get(t.code.toUpperCase())
    byCode.delete(t.code.toUpperCase())
    return rowOf(t.code, t.label, l)
  })
  for (const l of byCode.values()) rows.push(rowOf(l.serviceType ?? '', l.serviceTypeLabel ?? l.serviceType ?? '', l))
  return { levels: rows }
}

function rowOf(code: string, label: string, l: ServiceLevel | undefined): SlaRow {
  return {
    serviceType: code,
    label,
    maxTransitHours: l?.maxTransitHours ?? null,
    pickupWindowMin: l?.pickupWindowMin ?? null,
    onTimeTargetPct: l?.onTimeTargetPct ?? null,
    penaltyAmount: l?.penaltyAmount ?? null,
  }
}

/** Un renglón con algún dato es un nivel; vacío = ese tipo de servicio no tiene SLA. */
export function isFilledRow(r: Pick<SlaRow, SlaField>): boolean {
  return SLA_FIELDS.some((f) => r[f] != null)
}

export interface SlaMessages {
  number: string
  hoursInt: string
  hoursMin: string
  windowInt: string
  windowMin: string
  pctRange: string
  penaltyMin: string
}

export function slaSchema(m: SlaMessages) {
  const num = z.number(m.number)
  return z.object({
    levels: z.array(
      z.object({
        serviceType: z.string(),
        label: z.string(),
        maxTransitHours: num.int(m.hoursInt).gt(0, m.hoursMin).nullable(),
        pickupWindowMin: num.int(m.windowInt).min(0, m.windowMin).nullable(),
        onTimeTargetPct: num.min(0, m.pctRange).max(100, m.pctRange).nullable(),
        penaltyAmount: num.min(0, m.penaltyMin).nullable(),
      }),
    ),
  })
}

export interface SlaRequest {
  body: ServiceLevelUpsert[]
  /** `indexMap[k]` = renglón del formulario del nivel `k` enviado (el servidor numera lo enviado, no lo mostrado). */
  indexMap: number[]
}

/** PUT /service-levels reemplaza la lista completa: se envían solo los renglones con datos. */
export function buildSlaRequest(rows: readonly SlaRow[]): SlaRequest {
  const body: ServiceLevelUpsert[] = []
  const indexMap: number[] = []
  rows.forEach((r, i) => {
    if (!isFilledRow(r)) return
    body.push({ serviceType: r.serviceType, maxTransitHours: r.maxTransitHours, pickupWindowMin: r.pickupWindowMin, onTimeTargetPct: r.onTimeTargetPct, penaltyAmount: r.penaltyAmount })
    indexMap.push(i)
  })
  return { body, indexMap }
}

/**
 * Los errores del servidor llegan como `serviceLevels[k].campo` (k = posición en lo enviado); se renombran a
 * `levels.<renglón>.campo` para que salgan bajo el campo que corresponde. Lo que no calce se deja tal cual.
 */
export function remapSlaErrors(errors: Record<string, string[]>, indexMap: readonly number[]): Record<string, string[]> {
  const out: Record<string, string[]> = {}
  for (const [key, messages] of Object.entries(errors)) {
    const m = /^serviceLevels\[(\d+)\]\.(\w+)$/i.exec(key.startsWith('$.') ? key.slice(2) : key)
    const row = m ? indexMap[Number(m[1])] : undefined
    const name = m && row != null ? `levels.${row}.${m[2].charAt(0).toLowerCase()}${m[2].slice(1)}` : key
    out[name] = [...(out[name] ?? []), ...messages]
  }
  return out
}

/** Renombra campos de un error del servidor (p. ej. `amount` → `dispatchAmount`) sin tocar el resto. */
export function renameErrors(errors: Record<string, string[]>, names: Record<string, string>): Record<string, string[]> {
  const out: Record<string, string[]> = {}
  for (const [key, messages] of Object.entries(errors)) {
    const k = key.startsWith('$.') ? key.slice(2) : key
    const lower = k.charAt(0).toLowerCase() + k.slice(1)
    out[names[lower] ?? k] = messages
  }
  return out
}

// ---- Tarifas por servicio ----
export interface RateValues {
  serviceType: string
  packageType: string
  rate: number | null
  effectiveFrom: string
}

export interface RateMessages {
  serviceRequired: string
  packageRequired: string
  rateRequired: string
  rateMin: string
  dateRequired: string
  dateBeforeToday: string
}

/**
 * `minDate` = hoy (nueva versión): una vigencia anterior reescribiría el historial; sin `minDate` el alta acepta fechas
 * pasadas. `needsKeys` pide servicio y paquete (alta); `needsRate` pide la tarifa (el componente de pieza extra no lleva).
 */
export function rateSchema(m: RateMessages, opts: { needsKeys: boolean; needsRate: boolean; minDate?: string }) {
  return z
    .object({
      serviceType: z.string(),
      packageType: z.string(),
      rate: z.number(m.rateRequired).min(0, m.rateMin).nullable(),
      effectiveFrom: z.string().min(1, m.dateRequired),
    })
    .superRefine((v, ctx) => {
      if (opts.needsKeys && !v.serviceType) ctx.addIssue({ code: 'custom', path: ['serviceType'], message: m.serviceRequired })
      if (opts.needsKeys && !v.packageType) ctx.addIssue({ code: 'custom', path: ['packageType'], message: m.packageRequired })
      if (opts.needsRate && v.rate == null) ctx.addIssue({ code: 'custom', path: ['rate'], message: m.rateRequired })
      if (opts.minDate && v.effectiveFrom && v.effectiveFrom < opts.minDate) ctx.addIssue({ code: 'custom', path: ['effectiveFrom'], message: m.dateBeforeToday })
    })
}

// ---- Pieza extra: tramos ----
export interface TierValues {
  fromUnit?: number | null
  toUnit?: number | null
  rate?: number | null
  effectiveFrom: string
}

export interface TierMessages {
  fromRequired: string
  rangeInvalid: string
  rateRequired: string
  rateMin: string
  dateRequired: string
  dateBeforeToday: string
}

export function tierSchema(m: TierMessages, opts: { minDate?: string }) {
  return z
    .object({
      fromUnit: z.number(m.fromRequired).nullable(),
      toUnit: z.number(m.rangeInvalid).nullable(),
      rate: z.number(m.rateRequired).min(0, m.rateMin).nullable().refine((v) => v != null, m.rateRequired),
      effectiveFrom: z.string().min(1, m.dateRequired),
    })
    .superRefine((v, ctx) => {
      if (v.fromUnit == null) ctx.addIssue({ code: 'custom', path: ['fromUnit'], message: m.fromRequired })
      else if (!Number.isInteger(v.fromUnit) || v.fromUnit < MIN_EXTRA_PIECE) ctx.addIssue({ code: 'custom', path: ['fromUnit'], message: m.rangeInvalid })
      if (v.toUnit != null && (!Number.isInteger(v.toUnit) || (v.fromUnit != null && v.toUnit < v.fromUnit))) ctx.addIssue({ code: 'custom', path: ['toUnit'], message: m.rangeInvalid })
      if (opts.minDate && v.effectiveFrom && v.effectiveFrom < opts.minDate) ctx.addIssue({ code: 'custom', path: ['effectiveFrom'], message: m.dateBeforeToday })
    })
}

/** Nueva versión de un tramo: solo lo que cambió; «Hasta» vacío con uno anterior = `clearToUnit`. */
export function buildTierPatch(v: TierValues, original: Tier): Schemas['TierPatchRequest'] {
  const req: Schemas['TierPatchRequest'] = { effectiveFrom: v.effectiveFrom }
  if (v.fromUnit != null && v.fromUnit !== original.fromUnit) req.fromUnit = v.fromUnit
  if (v.toUnit == null) {
    if (original.toUnit != null) req.clearToUnit = true
  } else if (v.toUnit !== original.toUnit) req.toUnit = v.toUnit
  if (v.rate != null && v.rate !== original.rate) req.rate = v.rate
  return req
}

/** «2–5» o «6+» para el rango de piezas de un tramo: uno abierto no tiene tope. */
export function tierRangeText(t: Pick<Tier, 'fromUnit' | 'toUnit'>): string {
  return t.toUnit == null ? `${t.fromUnit}+` : `${t.fromUnit}–${t.toUnit}`
}

// ---- Servicios especiales ----
export interface SpecialValues {
  typeId: string
  newTypeName: string
  rate?: number | null
  effectiveFrom: string
}

export interface SpecialMessages {
  typeRequired: string
  nameRequired: string
  nameMax: string
  rateRequired: string
  rateMin: string
  dateRequired: string
  dateBeforeToday: string
}

export function specialSchema(m: SpecialMessages, opts: { needsType: boolean; minDate?: string }) {
  return z
    .object({
      typeId: z.string(),
      newTypeName: z.string().trim().max(SPECIAL_NAME_MAX, m.nameMax),
      rate: z.number(m.rateRequired).min(0, m.rateMin).nullable().refine((v) => v != null, m.rateRequired),
      effectiveFrom: z.string().min(1, m.dateRequired),
    })
    .superRefine((v, ctx) => {
      if (opts.needsType && !v.typeId) ctx.addIssue({ code: 'custom', path: ['typeId'], message: m.typeRequired })
      if (opts.needsType && v.typeId === NEW_SPECIAL_TYPE && !v.newTypeName.trim()) ctx.addIssue({ code: 'custom', path: ['newTypeName'], message: m.nameRequired })
      if (opts.minDate && v.effectiveFrom && v.effectiveFrom < opts.minDate) ctx.addIssue({ code: 'custom', path: ['effectiveFrom'], message: m.dateBeforeToday })
    })
}

/** Alta: un tipo existente (`typeId`) o el nombre de uno nuevo (`newTypeName`), nunca los dos. */
export function buildSpecialCreate(v: SpecialValues): Schemas['SpecialServiceCreateRequest'] {
  const base = { rate: v.rate ?? 0, effectiveFrom: v.effectiveFrom || null }
  return v.typeId === NEW_SPECIAL_TYPE ? { ...base, newTypeName: v.newTypeName.trim() } : { ...base, typeId: Number(v.typeId) }
}

// ---- Ayudas de pantalla sin React ----
/** Opciones de un catálogo: las habilitadas más el valor actual aunque ya esté deshabilitado. */
export function lookupOptions(options: readonly LookupOption[], current?: string | null) {
  const list = options.filter((o) => o.isEnabled !== false || o.code === current).map((o) => ({ value: o.code, label: o.label }))
  // mientras el catálogo carga (o si el valor ya no está en él) el valor actual sigue siendo elegible: el select no queda vacío
  if (current && !list.some((o) => o.value === current)) list.push({ value: current, label: current })
  return list
}

/** Vuelve a lanzar un error del servidor con los nombres de campo cambiados (para que salgan bajo el campo del formulario). */
export function rethrowRenamed(err: unknown, rename: (errors: Record<string, string[]>) => Record<string, string[]>): never {
  if (err instanceof ApiError) throw new ApiError(err.status, { title: err.title, code: err.code, errors: rename(err.errors), correlationId: err.correlationId })
  throw err
}
