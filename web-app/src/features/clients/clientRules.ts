// Lógica pura de Clientes (F-A1): validaciones espejo del servidor (ClientService, NumberFormat), numeración de ejemplo y
// armado de los requests. Sin React ni red, para probarla sola (clientRules.test.ts).
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type ClientListItem = Schemas['ClientListItemDto']
export type ClientDetail = Schemas['ClientDetailDto']
export type ClientContact = Schemas['ClientContactDto']
export type ContactPoint = Schemas['ContactPointDto']
export type ClientCreateRequest = Schemas['ClientCreateRequest']
export type ClientProfileUpdateRequest = Schemas['ClientProfileUpdateRequest']
export type ClientNumberSettingsRequest = Schemas['ClientNumberSettingsRequest']

/** Dominio de estatus y EntityType del cliente (CatalogDomains.ClientStatus / EntityTypes.Client). */
export const CLIENT_STATUS_DOMAIN = 'ClientStatus'
export const CLIENT_ENTITY_TYPE = 'CLIENT'

// ---- Límites del servidor (ClientService) ----
export const NAME_MAX = 200
export const CODE_MAX = 30
export const LEGAL_NAME_MAX = 250
export const TAX_ID_MAX = 50
export const CONTRACT_TITLE_MAX = 200
export const CONTACT_NAME_MAX = 150
export const CONTACT_ROLE_MAX = 80
/** "Contrato marco": título por defecto del contrato inicial (ClientService.DefaultContractTitle). */
export const DEFAULT_CONTRACT_TITLE = 'Contrato marco'

// ---- Numeración (Teikem.Domain.Clients.NumberFormat) ----
export const PATTERN_MAX = 40
export const PATTERN_DEFAULTS = { order: 'ORD-#####', invoice: 'FAC-#####', package: 'PQT-#####' } as const
export type PatternKind = keyof typeof PATTERN_DEFAULTS
const ALLOWED_SYMBOLS = '-_/.#@'

export type PatternIssue = { code: 'required' } | { code: 'tooLong' } | { code: 'noDigit' } | { code: 'badChar'; ch: string }

/** Misma validación que `NumberFormat.Validate`: 1..40, al menos un '#', letras ASCII, dígitos y - _ / . # @. null = válido. */
export function patternIssue(pattern: string | null | undefined): PatternIssue | null {
  if (pattern == null || pattern.trim() === '') return { code: 'required' }
  if (pattern.length > PATTERN_MAX) return { code: 'tooLong' }
  if (!pattern.includes('#')) return { code: 'noDigit' }
  for (const ch of pattern) {
    if (/^[A-Za-z0-9]$/.test(ch)) continue
    if (ALLOWED_SYMBOLS.includes(ch)) continue
    return { code: 'badChar', ch }
  }
  return null
}

/** Espejo de `NumberFormat.Resolve`: 'AX-#####', 1 → 'AX-00001'; los dígitos que no caben se anteponen al primer '#'; '@' = letra. */
export function resolvePattern(pattern: string, n: number, letter = 'A'): string {
  const slots = [...pattern].filter((c) => c === '#').length
  const digits = String(Math.max(0, Math.trunc(n))).padStart(slots, '0')
  let overflow = digits.length - slots
  let next = 0
  let out = ''
  for (const ch of pattern) {
    if (ch === '#') {
      if (overflow > 0) {
        out += digits.slice(0, overflow)
        next = overflow
        overflow = 0
      }
      out += digits[next++]
    } else if (ch === '@') out += letter
    else out += ch
  }
  return out
}

/** Patrón que ve el usuario en la ficha: el del cliente o, si no tiene, el del sistema. */
export function effectivePattern(kind: PatternKind, own: string | null | undefined): string {
  return own && own.trim() !== '' ? own.trim() : PATTERN_DEFAULTS[kind]
}

// ---- Alta ----
export interface CreateClientValues {
  name: string
  code: string
  legalName: string
  taxId: string
  paymentTerm: string
  currency: string
  creditLimit: number | null
  createContract: boolean
  /** Contrato inicial (el servidor nombra sus errores `contract.startDate` / `contract.title`). */
  contract: { startDate: string; title: string }
}

/** Mensajes ya traducidos que usa el esquema (las claves viven en `clients.errors.*`). */
export interface CreateMessages {
  nameRequired: string
  nameMax: string
  codeMax: string
  legalNameMax: string
  taxIdMax: string
  creditLimitNumber: string
  creditLimitMin: string
  startRequired: string
  titleMax: string
}

export function createClientSchema(m: CreateMessages) {
  return z
    .object({
      name: z.string().trim().min(1, m.nameRequired).max(NAME_MAX, m.nameMax),
      code: z.string().trim().max(CODE_MAX, m.codeMax),
      legalName: z.string().trim().max(LEGAL_NAME_MAX, m.legalNameMax),
      taxId: z.string().trim().max(TAX_ID_MAX, m.taxIdMax),
      paymentTerm: z.string(),
      currency: z.string(),
      creditLimit: z.number(m.creditLimitNumber).min(0, m.creditLimitMin).nullable(),
      createContract: z.boolean(),
      contract: z.object({ startDate: z.string(), title: z.string().trim().max(CONTRACT_TITLE_MAX, m.titleMax) }),
    })
    .superRefine((v, ctx) => {
      if (v.createContract && !v.contract.startDate) ctx.addIssue({ code: 'custom', path: ['contract', 'startDate'], message: m.startRequired })
    })
}

/** Valores iniciales del alta: contrato inicial encendido, «Cliente desde» = hoy en la zona de la compañía. */
export function emptyCreateValues(today: string): CreateClientValues {
  return {
    name: '',
    code: '',
    legalName: '',
    taxId: '',
    paymentTerm: '',
    currency: '',
    creditLimit: null,
    createContract: true,
    contract: { startDate: today, title: DEFAULT_CONTRACT_TITLE },
  }
}

/** Vacío → null (el servidor genera el código, deja sin término, etc.). El título vacío lo resuelve el servidor. */
/** El esquema zod infiere `creditLimit?` opcional; aquí se acepta ausente o null. */
export type CreateInput = Omit<CreateClientValues, 'creditLimit'> & { creditLimit?: number | null }

export function buildCreateRequest(v: CreateInput): ClientCreateRequest {
  return {
    name: v.name.trim(),
    code: v.code.trim() || null,
    legalName: v.legalName.trim() || null,
    taxId: v.taxId.trim() || null,
    paymentTerm: v.paymentTerm || null,
    currency: v.currency || null,
    creditLimit: v.creditLimit ?? null,
    contract: v.createContract ? { startDate: v.contract.startDate, title: v.contract.title.trim() || null } : undefined,
  }
}

// ---- Perfil fiscal ----
// (El punto de recogido por defecto y las direcciones viven en el panel «Direcciones»: ver addressRules.ts.)
export interface ProfileValues {
  legalName: string
  taxId: string
  creditLimit: number | null
  paymentTerm: string
  currency: string
  /** Servicio y paquete que la entrada rápida de envíos trae puestos para este cliente ('' = los de la compañía). */
  defaultServiceType: string
  defaultPackageType: string
  /** Lote 48: «Imprimir los labels automáticamente» (sus órdenes imprimen sus labels al guardarse). */
  autoPrintLabels: boolean
  /** Lote 43: «Imprimir la factura automáticamente» (solo la bandera; la impresión de la factura llega con facturación). */
  autoPrintInvoice: boolean
  /** Lote 156: lo que el cliente crea solo desde su portal de almacén. */
  portalCanCreateProducts: boolean
  portalCanCreateArrivals: boolean
  portalCanCreateExits: boolean
  /** App del chofer: firma de quien recibe / foto obligatorias al entregar sus envíos. */
  requireDeliverySignature: boolean
  requireDeliveryPhoto: boolean
}

export function profileValuesOf(c: ClientDetail): ProfileValues {
  return {
    legalName: c.legalName ?? '',
    taxId: c.taxId ?? '',
    creditLimit: c.creditLimit ?? null,
    paymentTerm: c.paymentTerm ?? '',
    currency: c.currency ?? '',
    defaultServiceType: c.defaultServiceType ?? '',
    defaultPackageType: c.defaultPackageType ?? '',
    autoPrintLabels: c.autoPrintLabels ?? false,
    autoPrintInvoice: c.autoPrintInvoice ?? false,
    portalCanCreateProducts: c.portalCanCreateProducts ?? false,
    portalCanCreateArrivals: c.portalCanCreateArrivals ?? false,
    portalCanCreateExits: c.portalCanCreateExits ?? false,
    requireDeliverySignature: c.requireDeliverySignature ?? false,
    requireDeliveryPhoto: c.requireDeliveryPhoto ?? false,
  }
}

export function profileSchema(m: Pick<CreateMessages, 'legalNameMax' | 'taxIdMax' | 'creditLimitNumber' | 'creditLimitMin'> & { creditLimitRequired: string }, hadCreditLimit: boolean) {
  return z.object({
    legalName: z.string().trim().max(LEGAL_NAME_MAX, m.legalNameMax),
    taxId: z.string().trim().max(TAX_ID_MAX, m.taxIdMax),
    // En el PATCH, creditLimit null = «no cambiar» (el API no puede borrarlo): si ya tenía valor no se deja vacío.
    creditLimit: z
      .number(m.creditLimitNumber)
      .min(0, m.creditLimitMin)
      .nullable()
      .refine((v) => v != null || !hadCreditLimit, m.creditLimitRequired),
    paymentTerm: z.string(),
    currency: z.string(),
    defaultServiceType: z.string(),
    defaultPackageType: z.string(),
    autoPrintLabels: z.boolean(),
    autoPrintInvoice: z.boolean(),
    portalCanCreateProducts: z.boolean(),
    portalCanCreateArrivals: z.boolean(),
    portalCanCreateExits: z.boolean(),
    requireDeliverySignature: z.boolean(),
    requireDeliveryPhoto: z.boolean(),
  })
}

/**
 * PATCH /profile: texto null = no cambiar, '' = borrar; por eso se envía tal cual. Término y moneda '' los borran.
 * «Imprimir los labels automáticamente» (Lote 48) y «Imprimir la factura automáticamente» (Lote 43): solo si cambiaron.
 * El punto de recogido ya no va aquí (panel «Direcciones»).
 */
export type ProfileInput = Omit<ProfileValues, 'creditLimit'> & { creditLimit?: number | null }

export function buildProfileRequest(v: ProfileInput, original: ProfileValues, rowVersion: string | null | undefined): ClientProfileUpdateRequest {
  const req: ClientProfileUpdateRequest = {
    legalName: v.legalName.trim(),
    taxId: v.taxId.trim(),
    creditLimit: v.creditLimit ?? null,
    paymentTerm: v.paymentTerm,
    currency: v.currency,
    rowVersion,
  }
  // servicio y paquete por omisión: solo si cambiaron ('' = vuelve a los de la compañía)
  if (v.defaultServiceType !== original.defaultServiceType) req.defaultServiceType = v.defaultServiceType
  if (v.defaultPackageType !== original.defaultPackageType) req.defaultPackageType = v.defaultPackageType
  if (v.autoPrintLabels !== original.autoPrintLabels) req.autoPrintLabels = v.autoPrintLabels
  if (v.autoPrintInvoice !== original.autoPrintInvoice) req.autoPrintInvoice = v.autoPrintInvoice
  if (v.portalCanCreateProducts !== original.portalCanCreateProducts) req.portalCanCreateProducts = v.portalCanCreateProducts
  if (v.portalCanCreateArrivals !== original.portalCanCreateArrivals) req.portalCanCreateArrivals = v.portalCanCreateArrivals
  if (v.portalCanCreateExits !== original.portalCanCreateExits) req.portalCanCreateExits = v.portalCanCreateExits
  if (v.requireDeliverySignature !== original.requireDeliverySignature) req.requireDeliverySignature = v.requireDeliverySignature
  if (v.requireDeliveryPhoto !== original.requireDeliveryPhoto) req.requireDeliveryPhoto = v.requireDeliveryPhoto
  return req
}

/** Una línea con la dirección (línea 1, línea 2, ciudad, estado, ZIP, país), sin partes vacías. */
export function addressLine(a: Schemas['ClientAddressDto'] | null | undefined): string {
  if (!a) return ''
  const cityState = [a.city, [a.state, a.postalCode].filter(Boolean).join(' ')].filter(Boolean).join(', ')
  return [a.line1, a.line2, cityState, a.countryLabel ?? a.country].filter(Boolean).join(' · ')
}

// ---- Numeración: formulario y request ----
/** Esquema del panel: cada patrón vacío (= el del sistema) o válido según `patternIssue` (mensajes por código). */
export function numberingSchema(message: (issue: PatternIssue) => string) {
  const pattern = z.string().refine((v) => v.trim() === '' || patternIssue(v.trim()) === null, { error: (iss) => message(patternIssue(String(iss.input).trim()) ?? { code: 'required' }) })
  return z.object({
    orderAssigner: z.enum(['client', 'teikem']),
    invoiceAssigner: z.enum(['client', 'teikem']),
    orderPattern: pattern,
    invoicePattern: pattern,
    packagePattern: pattern,
  })
}

/** Quién asigna el número: el cliente lo escribe o Teikem lo genera (clientAssigns* = false). */
export type Assigner = 'client' | 'teikem'

export interface NumberingValues {
  orderAssigner: Assigner
  invoiceAssigner: Assigner
  orderPattern: string
  invoicePattern: string
  packagePattern: string
}

export function numberingValuesOf(c: ClientDetail): NumberingValues {
  const n = c.numberSettings
  return {
    orderAssigner: (n?.clientAssignsOrderNumber ?? true) ? 'client' : 'teikem',
    invoiceAssigner: (n?.clientAssignsInvoiceNumber ?? false) ? 'client' : 'teikem',
    orderPattern: n?.orderNumberFormat ?? '',
    invoicePattern: n?.invoiceNumberFormat ?? '',
    packagePattern: n?.packageNumberFormat ?? '',
  }
}

/** PATCH /number-settings: patrón vacío = '' (vuelve al del sistema); «Teikem asigna» = clientAssigns* false. */
export function buildNumberingRequest(v: NumberingValues): ClientNumberSettingsRequest {
  return {
    clientAssignsOrderNumber: v.orderAssigner === 'client',
    clientAssignsInvoiceNumber: v.invoiceAssigner === 'client',
    orderNumberFormat: v.orderPattern.trim(),
    invoiceNumberFormat: v.invoicePattern.trim(),
    packageNumberFormat: v.packagePattern.trim(),
  }
}

/** Ejemplo local (consecutivo 1) para un patrón válido; null si está vacío o es inválido. Respaldo del preview del API. */
export function localPreview(kind: PatternKind, pattern: string): string | null {
  const p = pattern.trim()
  if (p === '') return resolvePattern(PATTERN_DEFAULTS[kind], 1)
  return patternIssue(p) ? null : resolvePattern(p, 1)
}

// ---- Personas de contacto ----
/** Principal primero y luego por nombre; las inactivas al final. */
export function sortContacts(list: readonly ClientContact[]): ClientContact[] {
  return [...list].sort(
    (a, b) =>
      Number(b.isActive) - Number(a.isActive) ||
      Number(b.isPrimary) - Number(a.isPrimary) ||
      (a.fullName ?? '').localeCompare(b.fullName ?? '', undefined, { sensitivity: 'base' }),
  )
}

export interface PersonValues {
  fullName: string
  role: string
  isPrimary: boolean
  phone: string
  /** Prefijo internacional del teléfono, con «+» ("+1"). */
  countryCallingCode: string
  email: string
}

export function personSchema(m: { nameRequired: string; nameMax: string; roleMax: string }, isValidPhone: (v: string, cc?: string) => boolean, phoneInvalid: string, emailInvalid: string) {
  return z.object({
    fullName: z.string().trim().min(1, m.nameRequired).max(CONTACT_NAME_MAX, m.nameMax),
    role: z.string().trim().max(CONTACT_ROLE_MAX, m.roleMax),
    isPrimary: z.boolean(),
    phone: z.string(),
    countryCallingCode: z.string().trim(),
    email: z.string().trim().refine((v) => v === '' || isValidEmail(v), emailInvalid),
  }).superRefine((v, ctx) => {
    if (!isValidPhone(v.phone, v.countryCallingCode)) ctx.addIssue({ code: 'custom', path: ['phone'], message: phoneInvalid })
  })
}

/** Espejo de `ContactPointService.ValidateValue` para correo: dirección simple con punto en el dominio. */
export function isValidEmail(v: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim())
}

/** Teléfono y correo de una persona (primer punto de cada tipo), para la tabla y para precargar la edición. */
export function personPhone(c: ClientContact): ContactPoint | undefined {
  return (c.contactPoints ?? []).find((p) => p.isActive && p.contactType !== 'EMAIL')
}
export function personEmail(c: ClientContact): ContactPoint | undefined {
  return (c.contactPoints ?? []).find((p) => p.isActive && p.contactType === 'EMAIL')
}

// ---- Teléfonos y correos del propio cliente ----
export const EMAIL_TYPE = 'EMAIL'
export const PHONE_TYPE = 'PHONE'
export const isEmailType = (code: string | null | undefined) => (code ?? '').toUpperCase() === EMAIL_TYPE

/** Solo puntos activos, teléfonos antes que correos y el principal de cada tipo primero. */
export function sortContactPoints(list: readonly ContactPoint[]): ContactPoint[] {
  return list
    .filter((p) => p.isActive)
    .sort(
      (a, b) =>
        Number(isEmailType(a.contactType)) - Number(isEmailType(b.contactType)) ||
        (a.contactType ?? '').localeCompare(b.contactType ?? '') ||
        Number(b.isPrimary) - Number(a.isPrimary) ||
        a.id - b.id,
    )
}

export interface PointValues {
  contactType: string
  value: string
  /** Prefijo internacional del teléfono, con «+» ("+1"); en los correos no se usa. */
  countryCallingCode?: string
  extension: string
  label: string
  isPrimary: boolean
}

export function pointSchema(m: { valueRequired: string; emailInvalid: string; phoneInvalid: string; countryCodeInvalid?: string }, isValidPhone: (v: string, cc?: string) => boolean, isValidCallingCode: (cc: string) => boolean = () => true) {
  return z
    .object({
      contactType: z.string().min(1),
      value: z.string().trim().min(1, m.valueRequired),
      countryCallingCode: z.string().trim().optional(),
      extension: z.string().trim().max(20),
      label: z.string().trim().max(60),
      isPrimary: z.boolean(),
    })
    .superRefine((v, ctx) => {
      if (!v.value) return
      if (isEmailType(v.contactType)) {
        if (!isValidEmail(v.value)) ctx.addIssue({ code: 'custom', path: ['value'], message: m.emailInvalid })
      } else if (!isValidCallingCode(v.countryCallingCode ?? '')) ctx.addIssue({ code: 'custom', path: ['countryCallingCode'], message: m.countryCodeInvalid ?? m.phoneInvalid })
      else if (!isValidPhone(v.value, v.countryCallingCode ?? '')) ctx.addIssue({ code: 'custom', path: ['value'], message: m.phoneInvalid })
    })
}

/** Cuerpo de POST/PUT /contacts: teléfono con `normalizePhone` (solo dígitos) y su prefijo internacional (solo dígitos); correo en minúsculas, sin prefijo. */
export function buildPointRequest(v: PointValues, normalizePhone: (s: string, cc?: string) => string): Schemas['ContactPointUpsertRequest'] {
  const email = isEmailType(v.contactType)
  const cc = (v.countryCallingCode ?? '').replace(/\D/g, '')
  return {
    contactType: v.contactType,
    value: email ? v.value.trim().toLowerCase() : normalizePhone(v.value, v.countryCallingCode ?? ''),
    countryCallingCode: email || !cc ? null : cc,
    extension: v.extension.trim() || null,
    label: v.label.trim() || null,
    isPrimary: v.isPrimary,
  }
}

// ---- Contratos (solo lectura) ----
export function contractName(c: Schemas['ClientContractSummaryDto']): string {
  return [c.contractNumber, c.title].filter(Boolean).join(' · ')
}
