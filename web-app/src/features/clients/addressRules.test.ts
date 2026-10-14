// Reglas puras del panel «Direcciones»: tipos de localización, esquema (el nombre solo en los puntos), requests de alta y cambio,
// opciones de «Se recoge en» (la física siempre; la postal solo si es propia; cada punto) y el valor actual según la ficha.
import { describe, expect, it } from 'vitest'
import {
  addressSchema,
  addressValuesOf,
  buildAddressCreate,
  buildAddressPatch,
  currentPickupValue,
  defaultAddressName,
  emptyAddressValues,
  pickupOptions,
  PICKUP_PHYSICAL,
  shortAddress,
  type ClientAddress,
} from './addressRules'

const messages = {
  nameRequired: 'Escriba el nombre del punto.',
  line1Required: 'La dirección (línea 1) es obligatoria.',
  cityRequired: 'La ciudad es obligatoria.',
  countryRequired: 'El país es obligatorio.',
  max: (n: number) => `Máximo ${n} caracteres.`,
}

const address = (over: Partial<ClientAddress> = {}): ClientAddress => ({
  id: 1,
  publicId: 'a-1',
  name: 'Almacén Norte',
  locationType: 'PICKUP',
  line1: 'Carr. 2 km 10',
  line2: null,
  city: 'Bayamón',
  state: 'PR',
  postalCode: '00959',
  country: 'PR',
  isActive: true,
  ...over,
})

describe('esquema', () => {
  const ok = { ...emptyAddressValues(), line1: 'Calle 1', city: 'Caguas' }

  it('la física y la postal no piden nombre; el punto de recogida sí', () => {
    expect(addressSchema(messages, false).safeParse(ok).success).toBe(true)
    const point = addressSchema(messages, true).safeParse(ok)
    expect(point.success).toBe(false)
    expect(point.error?.issues[0].message).toBe('Escriba el nombre del punto.')
    expect(addressSchema(messages, true).safeParse({ ...ok, name: 'Almacén' }).success).toBe(true)
  })

  it('dirección, ciudad y país son obligatorios y respetan los topes', () => {
    const r = addressSchema(messages, false).safeParse({ ...emptyAddressValues(), line1: ' ', city: '', country: '' })
    expect(r.error?.issues.map((i) => i.message).sort()).toEqual(['El país es obligatorio.', 'La ciudad es obligatoria.', 'La dirección (línea 1) es obligatoria.'].sort())
    expect(addressSchema(messages, false).safeParse({ ...ok, line1: 'x'.repeat(201) }).error?.issues[0].message).toBe('Máximo 200 caracteres.')
  })
})

describe('requests', () => {
  const v = { name: ' Almacén ', line1: ' Calle 1 ', line2: '', city: 'Caguas', state: 'PR', postalCode: '00725', country: 'PR' }

  it('alta: CORPORATE (física), BILLING (postal) o PICKUP; la física y la postal llevan un nombre interno y el punto el suyo', () => {
    const physical = buildAddressCreate('physical', 'c-1', 'Acme', v)
    expect(physical).toMatchObject({ clientPublicId: 'c-1', locationType: 'CORPORATE', name: 'Dirección física — Acme', line1: 'Calle 1', line2: null, city: 'Caguas', postalCode: '00725' })
    expect(buildAddressCreate('postal', 'c-1', 'Acme', v)).toMatchObject({ locationType: 'BILLING', name: 'Dirección postal — Acme' })
    expect(buildAddressCreate('pickup', 'c-1', 'Acme', v)).toMatchObject({ locationType: 'PICKUP', name: 'Almacén' })
  })

  it('el nombre interno se recorta a 200 caracteres', () => {
    expect(defaultAddressName('physical', 'x'.repeat(300))).toHaveLength(200)
  })

  it('cambio: solo lo que cambió; el nombre solo en un punto de recogida; sin cambios no hay PATCH', () => {
    const original = addressValuesOf(address())
    expect(buildAddressPatch('pickup', { ...original }, original)).toEqual({ request: {}, changed: false })
    const edited = buildAddressPatch('pickup', { ...original, name: 'Almacén Sur', city: 'Ponce', postalCode: '00730' }, original)
    expect(edited.changed).toBe(true)
    expect(edited.request).toEqual({ name: 'Almacén Sur', city: 'Ponce', postalCode: '00730' })
    // la física y la postal no renombran su localización
    expect(buildAddressPatch('physical', { ...original, name: 'Otro' }, original).changed).toBe(false)
  })
})

describe('«Se recoge en»', () => {
  const labels = { physical: 'La dirección física', postal: 'La dirección postal' }

  it('la física siempre; la postal solo si es una dirección propia; cada punto con su dirección', () => {
    expect(pickupOptions({ postalAddress: null, pickupLocations: [] }, labels).map((o) => o.value)).toEqual([PICKUP_PHYSICAL])
    const withAll = pickupOptions({ postalAddress: address({ publicId: 'p-1', locationType: 'BILLING' }), pickupLocations: [address({ publicId: 'w-1' })] }, labels)
    expect(withAll.map((o) => [o.value, o.kind])).toEqual([
      [PICKUP_PHYSICAL, 'physical'],
      ['p-1', 'postal'],
      ['w-1', 'pickup'],
    ])
    expect(withAll[2].label).toBe('Almacén Norte — Carr. 2 km 10, Bayamón, PR 00959')
  })

  it('el valor actual: la física si recoge en la corporativa o no hay punto; si no, el id de la dirección elegida', () => {
    expect(currentPickupValue({ pickupAddress: null })).toBe(PICKUP_PHYSICAL)
    expect(currentPickupValue({ pickupAddress: { publicId: 'corp', isDefaultFromCorporate: true } })).toBe(PICKUP_PHYSICAL)
    expect(currentPickupValue({ pickupAddress: { publicId: 'w-1', isDefaultFromCorporate: false } })).toBe('w-1')
  })

  it('shortAddress: línea 1, pueblo, estado y código postal sin partes vacías', () => {
    expect(shortAddress(address())).toBe('Carr. 2 km 10, Bayamón, PR 00959')
    expect(shortAddress(address({ state: null, postalCode: null }))).toBe('Carr. 2 km 10, Bayamón')
    expect(shortAddress(null)).toBe('')
  })
})
