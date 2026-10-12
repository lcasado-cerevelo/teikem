// Lote F-D1 — Choferes (Catálogo → Choferes y tarifas). Lógica pura: tipos del API, esquemas zod espejo del servidor
// (DriverService / DriverRules / DriverDocumentService, manual 04 §2), armado de los requests (alta y PATCH solo de lo cambiado),
// estado visible del chofer, usuarios vinculables y reglas de licencias y certificaciones. Sin React ni red.
import { z } from 'zod'
import type { TParams } from '../../kernel/i18n'
import type {
  DriverCertification,
  DriverCertificationRequest,
  DriverCreateRequest,
  DriverDetail,
  DriverLicense,
  DriverLicenseRequest,
  DriverListItem,
  DriverPatchRequest,
  UserSummary,
} from './api'

/** Función de textos (`useT()` o `t`): los esquemas piden sus mensajes por clave para que la prueba los vea EXACTOS. */
export type TFn = (key: string, params?: TParams) => string

// ---- topes (= columnas de dbo.Driver; los mismos de DriverRules) ----
export const CODE_MAX = 30
export const NAME_MAX = 150
export const DOCUMENT_NUMBER_MAX = 60

/** Códigos del catálogo DriverStatus. */
export const DriverStatuses = { Active: 'ACTIVE', Unavailable: 'UNAVAILABLE', Inactive: 'INACTIVE' } as const
export const DRIVER_STATUS_DOMAIN = 'DriverStatus'

/** Valor del filtro de estatus de la lista: sin filtro. */
export const FILTER_ANY = ''

// ---------------------------------------------------------------- estado visible

export type DriverState = 'removed' | 'inactive' | 'unavailable' | 'available'

/**
 * Los tres estados distintos del chofer, de más a menos fuerte: eliminado (estatus terminal INACTIVE), inactivo (casilla «Activo»
 * apagada) y no disponible (estatus UNAVAILABLE); si no, disponible.
 */
export function driverState(d: Pick<DriverListItem, 'statusCode' | 'isActive'>): DriverState {
  if ((d.statusCode ?? '').toUpperCase() === DriverStatuses.Inactive) return 'removed'
  if (!d.isActive) return 'inactive'
  if ((d.statusCode ?? '').toUpperCase() === DriverStatuses.Unavailable) return 'unavailable'
  return 'available'
}

/** «zona · área» de la fila de la lista (la maqueta: `zone · area`, con «—» si falta). */
export function zoneLine(d: Pick<DriverListItem, 'zoneCode' | 'area'>): string {
  return `${d.zoneCode || '—'} · ${d.area || '—'}`
}

// ---------------------------------------------------------------- formulario (alta y edición)

export interface DriverValues {
  /** Solo en el alta (después es inmutable). */
  code: string
  fullName: string
  /** Id de la zona de despacho primaria; '' = sin zona. */
  dispatchZoneId: string
  /** null = sin tope propio (rige el de la compañía). */
  maxStopsPerRoute: number | null
  /** 'YYYY-MM-DD' o ''. */
  hireDate: string
  /** Id del usuario vinculado; '' = ninguno. */
  userId: string
}

export function emptyDriverValues(): DriverValues {
  return { code: '', fullName: '', dispatchZoneId: '', maxStopsPerRoute: null, hireDate: '', userId: '' }
}

export function driverValuesOf(d: DriverDetail): DriverValues {
  return {
    code: d.code ?? '',
    fullName: d.fullName ?? '',
    dispatchZoneId: d.dispatchZoneId != null ? String(d.dispatchZoneId) : '',
    maxStopsPerRoute: d.maxStopsPerRoute ?? null,
    hireDate: d.hireDate ?? '',
    userId: d.user?.userId != null ? String(d.user.userId) : '',
  }
}

export interface DriverSchemaOptions {
  /** true = alta (el código es obligatorio); false = edición (el código no se valida). */
  creating: boolean
  /** El chofer ya tenía fecha de alta: el servidor lee `null` como «no cambiar», así que no se puede vaciar. */
  hadHireDate?: boolean
}

export function driverSchema(t: TFn, opts: DriverSchemaOptions) {
  return z
    .object({
      code: z.string(),
      fullName: z.string().trim().min(1, t('drivers.errors.nameRequired')).max(NAME_MAX, t('drivers.errors.nameMax', { max: NAME_MAX })),
      dispatchZoneId: z.string(),
      maxStopsPerRoute: z
        .number(t('drivers.errors.maxStopsInteger'))
        .int(t('drivers.errors.maxStopsInteger'))
        .min(1, t('drivers.errors.maxStopsMin'))
        .nullable(),
      hireDate: z.string(),
      userId: z.string(),
    })
    .superRefine((v, ctx) => {
      if (opts.creating) {
        const code = v.code.trim()
        if (!code) ctx.addIssue({ code: 'custom', path: ['code'], message: t('drivers.errors.codeRequired') })
        else if (code.length > CODE_MAX) ctx.addIssue({ code: 'custom', path: ['code'], message: t('drivers.errors.codeMax', { max: CODE_MAX }) })
      }
      if (opts.hadHireDate && !v.hireDate) ctx.addIssue({ code: 'custom', path: ['hireDate'], message: t('drivers.errors.hireDateKeep') })
    })
}

/** Alta: código en mayúsculas y recortado (el servidor también lo hace); vacío → null; el usuario solo con `admin.users`. */
export function buildDriverCreate(v: DriverValues, opts: { canLinkUser: boolean }): DriverCreateRequest {
  const req: DriverCreateRequest = {
    code: v.code.trim().toUpperCase(),
    fullName: v.fullName.trim(),
    dispatchZoneId: v.dispatchZoneId ? Number(v.dispatchZoneId) : null,
    maxStopsPerRoute: v.maxStopsPerRoute,
    hireDate: v.hireDate || null,
  }
  if (opts.canLinkUser && v.userId) req.userId = Number(v.userId)
  return req
}

export interface DriverPatchPlan {
  request: DriverPatchRequest
  /** false = no hay nada que mandar. */
  changed: boolean
}

/**
 * PATCH solo de lo cambiado + `rowVersion`. El servidor lee `null` como «no cambiar»: quitar la zona, el tope o el usuario va con
 * `clearZone` / `clearMaxStopsPerRoute` / `clearUser`. El usuario solo se toca con `admin.users` (el servidor lo exige antes de nada).
 */
export function buildDriverPatch(v: DriverValues, driver: DriverDetail, opts: { canLinkUser: boolean }): DriverPatchPlan {
  const original = driverValuesOf(driver)
  const request: DriverPatchRequest = { rowVersion: driver.rowVersion ?? null }
  let changed = false
  const fullName = v.fullName.trim()
  if (fullName !== original.fullName) {
    request.fullName = fullName
    changed = true
  }
  if (v.dispatchZoneId !== original.dispatchZoneId) {
    if (v.dispatchZoneId) request.dispatchZoneId = Number(v.dispatchZoneId)
    else request.clearZone = true
    changed = true
  }
  if (v.maxStopsPerRoute !== original.maxStopsPerRoute) {
    if (v.maxStopsPerRoute != null) request.maxStopsPerRoute = v.maxStopsPerRoute
    else request.clearMaxStopsPerRoute = true
    changed = true
  }
  if (v.hireDate !== original.hireDate && v.hireDate) {
    request.hireDate = v.hireDate
    changed = true
  }
  if (opts.canLinkUser && v.userId !== original.userId) {
    if (v.userId) request.userId = Number(v.userId)
    else request.clearUser = true
    changed = true
  }
  return { request, changed }
}

// ---------------------------------------------------------------- usuario vinculado

/** Usuarios que se pueden vincular: internos, activos y con membresía ACTIVE (el servidor repite la regla y cuida «ya vinculado»). */
export function linkableUsers(users: readonly UserSummary[] | undefined, current: DriverDetail['user'] | null | undefined): { value: string; label: string }[] {
  const options = (users ?? [])
    .filter((u) => (!u.userKind || u.userKind.toUpperCase() === 'INTERNAL') && u.isActive && (u.membershipStatus ?? '').toUpperCase() === 'ACTIVE')
    .map((u) => ({ value: String(u.id), label: userLabel(u.fullName, u.email) }))
  // el usuario ya vinculado aparece aunque la lista no lo traiga
  if (current && !options.some((o) => o.value === String(current.userId))) options.unshift({ value: String(current.userId), label: userLabel(current.fullName, current.email) })
  return options
}

export function userLabel(fullName: string | null | undefined, email: string | null | undefined): string {
  const name = (fullName ?? '').trim()
  const mail = (email ?? '').trim()
  if (name && mail) return `${name} · ${mail}`
  return name || mail
}

// ---------------------------------------------------------------- licencias y certificaciones

export interface DocumentValues {
  /** Clase de licencia o tipo de certificación (código del catálogo). */
  type: string
  number: string
  issuedDate: string
  expiryDate: string
}

export const emptyDocumentValues = (): DocumentValues => ({ type: '', number: '', issuedDate: '', expiryDate: '' })

export function licenseValuesOf(l: DriverLicense): DocumentValues {
  return { type: l.licenseClassCode ?? '', number: l.licenseNumber ?? '', issuedDate: l.issuedDate ?? '', expiryDate: l.expiryDate ?? '' }
}

export function certificationValuesOf(c: DriverCertification): DocumentValues {
  return { type: c.certTypeCode ?? '', number: c.certNumber ?? '', issuedDate: c.issuedDate ?? '', expiryDate: c.expiryDate ?? '' }
}

/** Licencia (número obligatorio) o certificación (número opcional): mismas fechas y mismos topes. */
export function documentSchema(t: TFn, kind: 'license' | 'certification') {
  const k = kind === 'license' ? 'license' : 'cert'
  return z
    .object({
      type: z.string().min(1, t(`drivers.docs.errors.${k}TypeRequired`)),
      number:
        kind === 'license'
          ? z.string().trim().min(1, t('drivers.docs.errors.licenseNumberRequired')).max(DOCUMENT_NUMBER_MAX, t('drivers.docs.errors.licenseNumberMax', { max: DOCUMENT_NUMBER_MAX }))
          : z.string().trim().max(DOCUMENT_NUMBER_MAX, t('drivers.docs.errors.certNumberMax', { max: DOCUMENT_NUMBER_MAX })),
      issuedDate: z.string(),
      expiryDate: z.string(),
    })
    .superRefine((v, ctx) => {
      // el servidor reporta este error en `expiryDate`
      if (v.issuedDate && v.expiryDate && v.expiryDate < v.issuedDate) ctx.addIssue({ code: 'custom', path: ['expiryDate'], message: t('drivers.docs.errors.datesOrder') })
    })
}

/**
 * Licencia: alta con todo lo capturado; edición solo con lo cambiado. Una fecha que se vacía va con `clearIssuedDate` /
 * `clearExpiryDate` (el servidor lee `null` como «no cambiar»).
 */
export function buildLicenseRequest(v: DocumentValues, original?: DocumentValues): DriverLicenseRequest {
  const req: DriverLicenseRequest = {}
  const dates = datesRequest(v, original)
  if (!original || v.type !== original.type) req.licenseClass = v.type
  if (!original || v.number.trim() !== original.number) req.licenseNumber = v.number.trim()
  return { ...req, ...dates }
}

export function buildCertificationRequest(v: DocumentValues, original?: DocumentValues): DriverCertificationRequest {
  const req: DriverCertificationRequest = {}
  const dates = datesRequest(v, original)
  if (!original || v.type !== original.type) req.certType = v.type
  // el número de la certificación puede vaciarse: '' lo borra
  if (!original || v.number.trim() !== original.number) req.certNumber = v.number.trim()
  return { ...req, ...dates }
}

function datesRequest(v: DocumentValues, original?: DocumentValues): { issuedDate?: string; expiryDate?: string; clearIssuedDate?: boolean; clearExpiryDate?: boolean } {
  const out: { issuedDate?: string; expiryDate?: string; clearIssuedDate?: boolean; clearExpiryDate?: boolean } = {}
  if (v.issuedDate) {
    if (!original || v.issuedDate !== original.issuedDate) out.issuedDate = v.issuedDate
  } else if (original?.issuedDate) out.clearIssuedDate = true
  if (v.expiryDate) {
    if (!original || v.expiryDate !== original.expiryDate) out.expiryDate = v.expiryDate
  } else if (original?.expiryDate) out.clearExpiryDate = true
  return out
}

/** ¿Hay algo que mandar al editar un documento? (todas las claves son opcionales). */
export function hasDocumentChanges(req: DriverLicenseRequest | DriverCertificationRequest): boolean {
  return Object.keys(req).length > 0
}
