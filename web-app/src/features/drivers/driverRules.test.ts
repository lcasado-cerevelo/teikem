// Lote F-D1 — reglas puras de Choferes: esquema del alta/edición con los mensajes EXACTOS del manual 04 §2, armado de los requests
// (alta y PATCH solo de lo cambiado con clearZone / clearMaxStopsPerRoute / clearUser y rowVersion), estados del chofer, usuarios
// vinculables, licencias y certificaciones, y paridad de textos es/en.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t, translate } from '../../kernel/i18n'
import esDict from '../../kernel/i18n/es.json'
import enDict from '../../kernel/i18n/en.json'
import type { DriverDetail, UserSummary } from './api'
import {
  buildCertificationRequest,
  buildDriverCreate,
  buildDriverPatch,
  buildLicenseRequest,
  certificationValuesOf,
  CODE_MAX,
  documentSchema,
  driverSchema,
  driverState,
  driverValuesOf,
  emptyDocumentValues,
  emptyDriverValues,
  hasDocumentChanges,
  licenseValuesOf,
  linkableUsers,
  NAME_MAX,
  userLabel,
  zoneLine,
  type DriverValues,
} from './driverRules'
import { zoneOptions } from './zoneOptions'

beforeAll(() => setLang('es'))

const base: DriverDetail = {
  id: 7,
  publicId: 'd-7',
  code: 'D-007',
  fullName: 'Juan Pérez',
  dispatchZoneId: 3,
  zoneCode: 'R-03',
  area: 'Carolina · Trujillo Alto',
  maxStopsPerRoute: 20,
  effectiveMaxStops: 20,
  hireDate: '2024-03-01',
  user: { userId: 5, email: 'ana@x.com', fullName: 'Ana Cruz' },
  statusCode: 'ACTIVE',
  status: 'Activo',
  isTerminal: false,
  isActive: true,
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'AAA=',
}

const values = (over: Partial<DriverValues> = {}): DriverValues => ({ ...driverValuesOf(base), ...over })
const issues = (v: DriverValues, opts: Parameters<typeof driverSchema>[1]) => {
  const r = driverSchema(t, opts).safeParse(v)
  return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
}

describe('driverSchema — mensajes exactos del manual 04 §2.1', () => {
  it('alta: código y nombre obligatorios', () => {
    expect(issues(emptyDriverValues(), { creating: true })).toEqual({
      code: 'El código del chofer es obligatorio.',
      fullName: 'El nombre del chofer es obligatorio.',
    })
  })

  it('alta: código de más de 30 y nombre de más de 150 caracteres', () => {
    const r = issues({ ...emptyDriverValues(), code: 'A'.repeat(CODE_MAX + 1), fullName: 'N'.repeat(NAME_MAX + 1) }, { creating: true })
    expect(r.code).toBe('El código no puede exceder 30 caracteres.')
    expect(r.fullName).toBe('El nombre del chofer admite como máximo 150 caracteres.')
    expect(issues({ ...emptyDriverValues(), code: 'A'.repeat(CODE_MAX), fullName: 'N'.repeat(NAME_MAX) }, { creating: true })).toEqual({})
  })

  it('edición: el código no se valida (es inmutable)', () => {
    expect(issues(values({ code: '' }), { creating: false })).toEqual({})
  })

  it('tope de paradas: vacío vale (rige el de la compañía), 0 y negativos no, y debe ser entero', () => {
    expect(issues(values({ maxStopsPerRoute: null }), { creating: false })).toEqual({})
    expect(issues(values({ maxStopsPerRoute: 1 }), { creating: false })).toEqual({})
    expect(issues(values({ maxStopsPerRoute: 0 }), { creating: false }).maxStopsPerRoute).toBe('El tope de paradas debe ser mayor o igual a 1.')
    expect(issues(values({ maxStopsPerRoute: -3 }), { creating: false }).maxStopsPerRoute).toBe('El tope de paradas debe ser mayor o igual a 1.')
    expect(issues(values({ maxStopsPerRoute: 2.5 }), { creating: false }).maxStopsPerRoute).toBe('El tope de paradas debe ser un número entero.')
  })

  it('la fecha de alta de un chofer que ya la tiene no se puede vaciar (el servidor lee null como «no cambiar»)', () => {
    expect(issues(values({ hireDate: '' }), { creating: false, hadHireDate: true }).hireDate).toBe('La fecha de alta no se puede quitar; elija otra.')
    expect(issues(values({ hireDate: '' }), { creating: false, hadHireDate: false })).toEqual({})
  })
})

describe('buildDriverCreate', () => {
  it('código en mayúsculas y recortado, nombre recortado, vacíos → null', () => {
    expect(buildDriverCreate({ code: ' d-001 ', fullName: ' Ana ', dispatchZoneId: '', maxStopsPerRoute: null, hireDate: '', userId: '' }, { canLinkUser: false })).toEqual({
      code: 'D-001',
      fullName: 'Ana',
      dispatchZoneId: null,
      maxStopsPerRoute: null,
      hireDate: null,
    })
  })

  it('zona y tope como números; el usuario solo se manda con admin.users', () => {
    const v = { code: 'D1', fullName: 'Ana', dispatchZoneId: '4', maxStopsPerRoute: 18, hireDate: '2026-10-01', userId: '9' }
    expect(buildDriverCreate(v, { canLinkUser: false })).not.toHaveProperty('userId')
    expect(buildDriverCreate(v, { canLinkUser: true })).toMatchObject({ dispatchZoneId: 4, maxStopsPerRoute: 18, hireDate: '2026-10-01', userId: 9 })
  })
})

describe('buildDriverPatch — solo lo cambiado + rowVersion', () => {
  it('sin cambios no hay nada que mandar', () => {
    expect(buildDriverPatch(values(), base, { canLinkUser: true })).toEqual({ request: { rowVersion: 'AAA=' }, changed: false })
  })

  it('nombre recortado; el resto no viaja', () => {
    const plan = buildDriverPatch(values({ fullName: '  Juan P.  ' }), base, { canLinkUser: true })
    expect(plan.changed).toBe(true)
    expect(plan.request).toEqual({ fullName: 'Juan P.', rowVersion: 'AAA=' })
  })

  it('cambiar de zona manda el id; quitarla manda clearZone (null significaría «no cambiar»)', () => {
    expect(buildDriverPatch(values({ dispatchZoneId: '8' }), base, { canLinkUser: true }).request).toEqual({ dispatchZoneId: 8, rowVersion: 'AAA=' })
    const cleared = buildDriverPatch(values({ dispatchZoneId: '' }), base, { canLinkUser: true }).request
    expect(cleared).toEqual({ clearZone: true, rowVersion: 'AAA=' })
    expect(cleared).not.toHaveProperty('dispatchZoneId')
  })

  it('tope de paradas: nuevo valor o clearMaxStopsPerRoute', () => {
    expect(buildDriverPatch(values({ maxStopsPerRoute: 30 }), base, { canLinkUser: true }).request).toEqual({ maxStopsPerRoute: 30, rowVersion: 'AAA=' })
    expect(buildDriverPatch(values({ maxStopsPerRoute: null }), base, { canLinkUser: true }).request).toEqual({ clearMaxStopsPerRoute: true, rowVersion: 'AAA=' })
  })

  it('fecha de alta nueva viaja; vaciarla no manda nada (el esquema ya lo impide)', () => {
    expect(buildDriverPatch(values({ hireDate: '2025-01-02' }), base, { canLinkUser: true }).request).toEqual({ hireDate: '2025-01-02', rowVersion: 'AAA=' })
    expect(buildDriverPatch(values({ hireDate: '' }), base, { canLinkUser: true }).changed).toBe(false)
  })

  it('usuario: vincular otro manda userId, quitarlo clearUser, y sin admin.users no se toca', () => {
    expect(buildDriverPatch(values({ userId: '6' }), base, { canLinkUser: true }).request).toEqual({ userId: 6, rowVersion: 'AAA=' })
    expect(buildDriverPatch(values({ userId: '' }), base, { canLinkUser: true }).request).toEqual({ clearUser: true, rowVersion: 'AAA=' })
    expect(buildDriverPatch(values({ userId: '' }), base, { canLinkUser: false })).toEqual({ request: { rowVersion: 'AAA=' }, changed: false })
  })

  it('un chofer sin zona, tope ni usuario (valores vacíos del API) no genera cambios', () => {
    const bare: DriverDetail = { ...base, dispatchZoneId: null, maxStopsPerRoute: null, user: null, hireDate: null }
    expect(buildDriverPatch(driverValuesOf(bare), bare, { canLinkUser: true }).changed).toBe(false)
  })
})

describe('estado visible y lista', () => {
  it('driverState: eliminado > inactivo > no disponible > disponible', () => {
    expect(driverState({ statusCode: 'INACTIVE', isActive: false })).toBe('removed')
    expect(driverState({ statusCode: 'ACTIVE', isActive: false })).toBe('inactive')
    expect(driverState({ statusCode: 'UNAVAILABLE', isActive: false })).toBe('inactive')
    expect(driverState({ statusCode: 'UNAVAILABLE', isActive: true })).toBe('unavailable')
    expect(driverState({ statusCode: 'ACTIVE', isActive: true })).toBe('available')
  })

  it('zoneLine: «zona · área» con «—» donde falta (como la maqueta)', () => {
    expect(zoneLine({ zoneCode: 'R-01', area: 'Toa Baja · Bayamón' })).toBe('R-01 · Toa Baja · Bayamón')
    expect(zoneLine({})).toBe('— · —')
    expect(zoneLine({ zoneCode: 'R-02' })).toBe('R-02 · —')
  })

  it('zoneOptions: zonas activas y la actual del chofer aunque esté inactiva', () => {
    const zones = [
      { id: 1, code: 'R-01', name: 'Norte', driverCount: 0, isActive: true },
      { id: 2, code: 'R-02', name: 'Sur', driverCount: 0, isActive: false },
    ]
    expect(zoneOptions(zones)).toEqual([{ value: '1', label: 'R-01 · Norte' }])
    expect(zoneOptions(zones, { id: 2, code: 'R-02', name: 'Sur' })).toEqual([
      { value: '1', label: 'R-01 · Norte' },
      { value: '2', label: 'R-02 · Sur' },
    ])
    expect(zoneOptions(undefined)).toEqual([])
  })
})

describe('linkableUsers', () => {
  const user = (over: Partial<UserSummary>): UserSummary => ({ id: 1, fullName: 'A', email: 'a@x.com', userKind: 'INTERNAL', isActive: true, membershipStatus: 'ACTIVE', mfaEnabled: false, isPlatformAdmin: false, hasPin: false, mfaRequired: false, ...over })

  it('solo internos, activos y con membresía ACTIVE; el vinculado actual siempre aparece', () => {
    const users = [
      user({ id: 1, fullName: 'Ana', email: 'ana@x.com' }),
      user({ id: 2, userKind: 'PORTAL' }),
      user({ id: 3, isActive: false }),
      user({ id: 4, membershipStatus: 'SUSPENDED' }),
      user({ id: 5, userKind: null, fullName: 'Sin tipo', email: 's@x.com' }),
    ]
    expect(linkableUsers(users, null).map((o) => o.value)).toEqual(['1', '5'])
    expect(linkableUsers(users, { userId: 9, email: 'z@x.com', fullName: 'Zeta' })[0]).toEqual({ value: '9', label: 'Zeta · z@x.com' })
    expect(linkableUsers(undefined, null)).toEqual([])
    expect(userLabel('Ana', null)).toBe('Ana')
    expect(userLabel(null, 'a@x.com')).toBe('a@x.com')
  })
})

describe('licencias y certificaciones', () => {
  const doc = (over = {}) => ({ ...emptyDocumentValues(), type: 'CDL_A', number: ' L-1 ', ...over })
  const msgs = (kind: 'license' | 'certification', v: ReturnType<typeof doc>) => {
    const r = documentSchema(t, kind).safeParse(v)
    return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
  }

  it('licencia: clase y número obligatorios, número ≤ 60', () => {
    expect(msgs('license', doc({ type: '', number: '' }))).toEqual({ type: 'La clase de licencia es obligatoria.', number: 'El número de licencia es obligatorio.' })
    expect(msgs('license', doc({ number: 'N'.repeat(61) })).number).toBe('El número de licencia admite como máximo 60 caracteres.')
    expect(msgs('license', doc())).toEqual({})
  })

  it('certificación: tipo obligatorio, número opcional ≤ 60', () => {
    expect(msgs('certification', doc({ type: '', number: '' }))).toEqual({ type: 'El tipo de certificación es obligatorio.' })
    expect(msgs('certification', doc({ number: 'N'.repeat(61) })).number).toBe('El número de certificación admite como máximo 60 caracteres.')
  })

  it('el vencimiento no puede ser anterior a la emisión (error en expiryDate, como el servidor)', () => {
    expect(msgs('license', doc({ issuedDate: '2026-05-01', expiryDate: '2026-04-30' })).expiryDate).toBe('La fecha de vencimiento no puede ser anterior a la de emisión.')
    expect(msgs('license', doc({ issuedDate: '2026-05-01', expiryDate: '2026-05-01' }))).toEqual({})
    expect(msgs('license', doc({ expiryDate: '2026-04-30' }))).toEqual({})
  })

  it('alta de licencia manda todo lo capturado (recortado); editar manda solo lo cambiado', () => {
    expect(buildLicenseRequest(doc({ issuedDate: '2026-01-01', expiryDate: '2027-01-01' }))).toEqual({ licenseClass: 'CDL_A', licenseNumber: 'L-1', issuedDate: '2026-01-01', expiryDate: '2027-01-01' })
    const original = licenseValuesOf({ id: 1, licenseClassCode: 'CDL_A', licenseNumber: 'L-1', issuedDate: '2026-01-01', expiryDate: '2027-01-01', isSuperseded: false, isActive: true })
    expect(buildLicenseRequest({ ...original, expiryDate: '2028-01-01' }, original)).toEqual({ expiryDate: '2028-01-01' })
    expect(hasDocumentChanges(buildLicenseRequest(original, original))).toBe(false)
  })

  it('vaciar una fecha va con clearIssuedDate / clearExpiryDate (null significaría «no cambiar»)', () => {
    const original = licenseValuesOf({ id: 1, licenseClassCode: 'CDL_A', licenseNumber: 'L-1', issuedDate: '2026-01-01', expiryDate: '2027-01-01', isSuperseded: false, isActive: true })
    const req = buildLicenseRequest({ ...original, expiryDate: '', issuedDate: '' }, original)
    expect(req).toEqual({ clearIssuedDate: true, clearExpiryDate: true })
    expect(req).not.toHaveProperty('expiryDate')
  })

  it('certificación: el número se puede vaciar con cadena vacía', () => {
    const original = certificationValuesOf({ id: 2, certTypeCode: 'HAZMAT', certNumber: 'C-9', isSuperseded: false, isActive: true })
    expect(buildCertificationRequest({ ...original, number: '' }, original)).toEqual({ certNumber: '' })
    expect(buildCertificationRequest({ type: 'REEFER', number: '', issuedDate: '', expiryDate: '' })).toEqual({ certType: 'REEFER', certNumber: '' })
  })
})

describe('textos', () => {
  function keysOf(obj: unknown, prefix = ''): string[] {
    if (obj === null || typeof obj !== 'object') return [prefix]
    return Object.entries(obj as Record<string, unknown>).flatMap(([k, v]) => keysOf(v, prefix ? `${prefix}.${k}` : k))
  }

  it('drivers.* y ui.expiry.* tienen las mismas claves en español e inglés', () => {
    expect(keysOf(enDict.drivers).sort()).toEqual(keysOf(esDict.drivers).sort())
    expect(keysOf(enDict.ui.expiry).sort()).toEqual(keysOf(esDict.ui.expiry).sort())
  })

  it('los mensajes de validación del navegador son los del manual (es) y están traducidos (en)', () => {
    expect(translate('es', 'drivers.errors.codeMax', { max: 30 })).toBe('El código no puede exceder 30 caracteres.')
    expect(translate('en', 'drivers.errors.codeMax', { max: 30 })).toBe('The code can’t exceed 30 characters.')
    expect(translate('es', 'drivers.docs.errors.datesOrder')).toBe('La fecha de vencimiento no puede ser anterior a la de emisión.')
  })
})
