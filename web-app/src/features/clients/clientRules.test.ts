import { describe, expect, it } from 'vitest'
import {
  addressLine,
  buildCreateRequest,
  buildNumberingRequest,
  buildPointRequest,
  buildProfileRequest,
  createClientSchema,
  effectivePattern,
  emptyCreateValues,
  isValidEmail,
  localPreview,
  numberingValuesOf,
  patternIssue,
  profileValuesOf,
  resolvePattern,
  sortContactPoints,
  sortContacts,
  type ClientContact,
  type ClientDetail,
  type ContactPoint,
} from './clientRules'

const messages = {
  nameRequired: 'nombre',
  nameMax: 'nombreMax',
  codeMax: 'codigoMax',
  legalNameMax: 'razonMax',
  taxIdMax: 'fiscalMax',
  creditLimitNumber: 'numero',
  creditLimitMin: 'negativo',
  startRequired: 'inicio',
  titleMax: 'tituloMax',
}

describe('patrones de numeración (espejo de NumberFormat)', () => {
  it('valida como el servidor', () => {
    expect(patternIssue('AX-#####')).toBeNull()
    expect(patternIssue('@@-###/2026_A.1')).toBeNull()
    expect(patternIssue('')).toEqual({ code: 'required' })
    expect(patternIssue('   ')).toEqual({ code: 'required' })
    expect(patternIssue(null)).toEqual({ code: 'required' })
    expect(patternIssue('#'.repeat(41))).toEqual({ code: 'tooLong' })
    expect(patternIssue('#'.repeat(40))).toBeNull()
    expect(patternIssue('AX-')).toEqual({ code: 'noDigit' })
    expect(patternIssue('AX #')).toEqual({ code: 'badChar', ch: ' ' })
    expect(patternIssue('ÁX-#')).toEqual({ code: 'badChar', ch: 'Á' })
  })

  it('resuelve el consecutivo: relleno con ceros, sobrantes antes del primer #, @ = letra', () => {
    expect(resolvePattern('AX-#####', 1)).toBe('AX-00001')
    expect(resolvePattern('AX-#####', 12345)).toBe('AX-12345')
    expect(resolvePattern('AX-##', 12345)).toBe('AX-12345')
    expect(resolvePattern('@@-###', 7)).toBe('AA-007')
    expect(resolvePattern('#-#', 123)).toBe('12-3')
  })

  it('patrón vacío = el del sistema', () => {
    expect(effectivePattern('order', '')).toBe('ORD-#####')
    expect(effectivePattern('invoice', null)).toBe('FAC-#####')
    expect(effectivePattern('package', ' PX-## ')).toBe('PX-##')
    expect(localPreview('order', '')).toBe('ORD-00001')
    expect(localPreview('order', 'AX-####')).toBe('AX-0001')
    expect(localPreview('order', 'AX-')).toBeNull()
  })

  it('arma el request de numeración: «Teikem asigna» = clientAssigns* false; vacío se manda como ""', () => {
    const detail = {
      numberSettings: { clientAssignsOrderNumber: true, clientAssignsInvoiceNumber: false, orderNumberFormat: 'AX-#####', invoiceNumberFormat: null, packageNumberFormat: null },
    } as ClientDetail
    const values = numberingValuesOf(detail)
    expect(values).toMatchObject({ orderAssigner: 'client', invoiceAssigner: 'teikem', orderPattern: 'AX-#####', invoicePattern: '' })
    expect(buildNumberingRequest({ ...values, orderAssigner: 'teikem', invoiceAssigner: 'client', packagePattern: ' PQ-### ' })).toEqual({
      clientAssignsOrderNumber: false,
      clientAssignsInvoiceNumber: true,
      orderNumberFormat: 'AX-#####',
      invoiceNumberFormat: '',
      packageNumberFormat: 'PQ-###',
    })
  })
})

describe('alta de cliente', () => {
  const ok = { ...emptyCreateValues('2026-10-10'), name: ' Acme ' }

  it('valores iniciales: contrato inicial encendido, desde hoy, «Contrato marco»', () => {
    expect(emptyCreateValues('2026-10-10')).toMatchObject({ createContract: true, contract: { startDate: '2026-10-10', title: 'Contrato marco' } })
  })

  it('valida nombre, longitudes, límite de crédito y fecha del contrato', () => {
    const schema = createClientSchema(messages)
    expect(schema.safeParse(ok).success).toBe(true)
    const bad = schema.safeParse({ ...ok, name: ' ', code: 'x'.repeat(31), creditLimit: -1 })
    expect(bad.success).toBe(false)
    const msgs = (bad.error?.issues ?? []).map((i) => i.message)
    expect(msgs).toEqual(expect.arrayContaining(['nombre', 'codigoMax', 'negativo']))
    const noDate = schema.safeParse({ ...ok, contract: { startDate: '', title: '' } })
    expect(noDate.error?.issues.map((i) => i.path.join('.'))).toContain('contract.startDate')
    // sin contrato no se exige la fecha
    expect(schema.safeParse({ ...ok, createContract: false, contract: { startDate: '', title: '' } }).success).toBe(true)
  })

  it('arma el request: vacíos como null y contrato solo si está encendido', () => {
    expect(buildCreateRequest({ ...ok, code: ' ', paymentTerm: 'NET30', creditLimit: 500 })).toEqual({
      name: 'Acme',
      code: null,
      legalName: null,
      taxId: null,
      paymentTerm: 'NET30',
      currency: null,
      creditLimit: 500,
      contract: { startDate: '2026-10-10', title: 'Contrato marco' },
    })
    const none = buildCreateRequest({ ...ok, createContract: false, contract: { startDate: '2026-10-10', title: '' } })
    expect(none.contract).toBeUndefined()
    expect(buildCreateRequest({ ...ok, contract: { startDate: '2026-10-10', title: '  ' } }).contract?.title).toBeNull()
  })
})

describe('perfil', () => {
  const detail = {
    legalName: 'Acme LLC',
    taxId: '66-123',
    creditLimit: 100,
    paymentTerm: 'NET30',
    currency: 'USD',
    pickupAddress: { publicId: 'loc-1', isDefaultFromCorporate: false },
  } as ClientDetail

  it('lee el punto de recogido propio; el corporativo por defecto cuenta como ninguno', () => {
    expect(profileValuesOf(detail).pickup).toBe('loc-1')
    expect(profileValuesOf({ ...detail, pickupAddress: { publicId: 'corp', isDefaultFromCorporate: true } } as ClientDetail).pickup).toBe('')
  })

  it('PATCH: textos tal cual (vacío borra), punto solo si cambió, quitar = clearDefaultPickup', () => {
    const original = profileValuesOf(detail)
    const same = buildProfileRequest(original, original, 'AAA=')
    expect(same).toEqual({ legalName: 'Acme LLC', taxId: '66-123', creditLimit: 100, paymentTerm: 'NET30', currency: 'USD', rowVersion: 'AAA=' })
    expect(buildProfileRequest({ ...original, pickup: '', legalName: '' }, original, 'AAA=')).toMatchObject({ legalName: '', clearDefaultPickup: true })
    const other = buildProfileRequest({ ...original, pickup: 'loc-2' }, original, 'AAA=')
    expect(other.defaultPickupLocationPublicId).toBe('loc-2')
    expect(other.clearDefaultPickup).toBeUndefined()
  })

  it('dirección en una línea sin partes vacías', () => {
    expect(addressLine(null)).toBe('')
    expect(addressLine({ line1: 'Calle 1', city: 'San Juan', state: 'PR', postalCode: '00901', countryLabel: 'Puerto Rico' } as never)).toBe('Calle 1 · San Juan, PR 00901 · Puerto Rico')
  })
})

describe('contactos', () => {
  it('ordena las personas: activas, principal primero, luego por nombre', () => {
    const people = [
      { id: 1, fullName: 'Zoe', isPrimary: false, isActive: true },
      { id: 2, fullName: 'Ana', isPrimary: false, isActive: true },
      { id: 3, fullName: 'Mía', isPrimary: true, isActive: true },
      { id: 4, fullName: 'Beto', isPrimary: true, isActive: false },
    ] as ClientContact[]
    expect(sortContacts(people).map((p) => p.id)).toEqual([3, 2, 1, 4])
  })

  it('teléfonos antes que correos, solo activos, principal primero', () => {
    const points = [
      { id: 1, contactType: 'EMAIL', isPrimary: true, isActive: true },
      { id: 2, contactType: 'PHONE', isPrimary: false, isActive: true },
      { id: 3, contactType: 'PHONE', isPrimary: true, isActive: true },
      { id: 4, contactType: 'PHONE', isPrimary: false, isActive: false },
    ] as ContactPoint[]
    expect(sortContactPoints(points).map((p) => p.id)).toEqual([3, 2, 1])
  })

  it('teléfono se guarda con dígitos, correo en minúsculas', () => {
    const digits = (s: string) => s.replace(/\D/g, '')
    const base = { contactType: 'PHONE', value: '(787) 555-1234', extension: ' 12 ', label: '', isPrimary: true }
    expect(buildPointRequest(base, digits)).toEqual({ contactType: 'PHONE', value: '7875551234', extension: '12', label: null, isPrimary: true })
    expect(buildPointRequest({ ...base, contactType: 'EMAIL', value: ' Ventas@ACME.com ' }, digits).value).toBe('ventas@acme.com')
  })

  it('correo: simple, con punto en el dominio', () => {
    expect(isValidEmail('a@b.co')).toBe(true)
    expect(isValidEmail('a@b')).toBe(false)
    expect(isValidEmail('a b@c.d')).toBe(false)
  })
})
