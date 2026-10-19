// Lote F-D2 — reglas puras de Flota: esquemas con los mensajes EXACTOS del manual 04 (§1 vehículos, §1.5 documentos, §2.7 zonas), armado de
// los requests (alta y PATCH solo de lo cambiado con rowVersion), estado visible del vehículo, filtros de «Documentos por vencer»,
// chip de días, texto claro de los motivos de disponibilidad y paridad de textos es/en.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t, translate } from '../../kernel/i18n'
import enDict from '../../kernel/i18n/en.json'
import esDict from '../../kernel/i18n/es.json'
import type { VehicleDetail, VehicleDocument } from './api'
import {
  buildDocumentRequest,
  buildExpiringQuery,
  buildVehicleCreate,
  buildVehiclePatch,
  buildZoneCreate,
  daysChipInfo,
  decimalIssue,
  documentSchema,
  documentValuesOf,
  emptyDocumentValues,
  emptyVehicleValues,
  emptyZoneValues,
  hasDocumentChanges,
  issueText,
  normalizeVin,
  parseWithinDays,
  sortedIssues,
  tabOf,
  vehicleMeta,
  vehicleSchema,
  vehicleState,
  vehicleValuesOf,
  zoneSchema,
  type VehicleValues,
} from './fleetRules'

beforeAll(() => setLang('es'))

const base: VehicleDetail = {
  id: 7,
  publicId: 'v-7',
  code: 'V-007',
  plateNumber: 'HGR-204',
  vehicleTypeCode: 'VAN',
  vehicleType: 'Van',
  ownershipCode: 'OWNED',
  ownership: 'Propio',
  fuelTypeCode: 'DIESEL',
  fuelType: 'Diésel',
  make: 'Ford',
  model: 'Transit',
  modelYear: 2022,
  vin: '1FTBW3XM5JKA12345',
  maxWeightKg: 1200,
  maxVolumeM3: 9,
  maxStops: 40,
  currentOdometerKm: 48210,
  statusCode: 'ACTIVE',
  status: 'Activo',
  isTerminal: false,
  isActive: true,
  createdAtUtc: '2026-01-01T00:00:00',
  rowVersion: 'AAA=',
}
const YEAR = 2026

const values = (over: Partial<VehicleValues> = {}): VehicleValues => ({ ...vehicleValuesOf(base), ...over })
const issues = (v: VehicleValues, opts: Partial<Parameters<typeof vehicleSchema>[1]> = {}) => {
  const r = vehicleSchema(t, { creating: false, currentYear: YEAR, ...opts }).safeParse(v)
  return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
}

describe('vehicleSchema — mensajes exactos del manual 04 §1.2', () => {
  it('alta: el código es obligatorio', () => {
    expect(issues(emptyVehicleValues(), { creating: true })).toEqual({ code: 'El código del vehículo es obligatorio.' })
  })

  it('alta: código de más de 30 caracteres; en la edición el código no se valida', () => {
    expect(issues({ ...emptyVehicleValues(), code: 'X'.repeat(31) }, { creating: true }).code).toBe('El código no puede exceder 30 caracteres.')
    expect(issues({ ...values(), code: '' })).toEqual({})
  })

  it('placa, marca y modelo: 20, 60 y 60 caracteres', () => {
    const r = issues(values({ plateNumber: 'P'.repeat(21), make: 'M'.repeat(61), model: 'O'.repeat(61) }))
    expect(r).toEqual({ plateNumber: 'No puede exceder 20 caracteres.', make: 'No puede exceder 60 caracteres.', model: 'No puede exceder 60 caracteres.' })
    expect(issues(values({ plateNumber: 'P'.repeat(20), make: 'M'.repeat(60) }))).toEqual({})
  })

  it('VIN: 40 caracteres ya sin espacios', () => {
    expect(issues(values({ vin: 'A'.repeat(41) })).vin).toBe('El VIN admite como máximo 40 caracteres.')
    expect(issues(values({ vin: `${'A'.repeat(40)}   ` }))).toEqual({})
    expect(issues(values({ vin: 'A '.repeat(20) }))).toEqual({})
  })

  it('capacidades, tope de paradas y odómetro', () => {
    const r = issues(values({ maxWeightKg: -1, maxVolumeM3: -0.5, maxStops: 0, currentOdometerKm: -3 }))
    expect(r).toEqual({
      maxWeightKg: 'La capacidad no puede ser negativa.',
      maxVolumeM3: 'La capacidad no puede ser negativa.',
      maxStops: 'El tope de paradas debe ser mayor o igual a 1.',
      currentOdometerKm: 'El odómetro no puede ser negativo.',
    })
    expect(issues(values({ maxStops: 2.5 })).maxStops).toBe('El tope de paradas debe ser un número entero.')
  })

  it('año del modelo entre 1900 y el año en curso + 1', () => {
    expect(issues(values({ modelYear: 1899 })).modelYear).toBe('El año del modelo debe estar entre 1900 y 2027.')
    expect(issues(values({ modelYear: 2028 })).modelYear).toBe('El año del modelo debe estar entre 1900 y 2027.')
    expect(issues(values({ modelYear: 2027 }))).toEqual({})
    expect(issues(values({ modelYear: 1900 }))).toEqual({})
  })

  it('precisión DECIMAL(12,3) / (12,4) / (12,1): demasiados decimales o fuera de rango', () => {
    expect(issues(values({ maxWeightKg: 1.2345 })).maxWeightKg).toBe('El valor admite como máximo 3 decimales y debe ser menor que 1,000,000,000.')
    expect(issues(values({ maxVolumeM3: 1.23456 })).maxVolumeM3).toBe('El valor admite como máximo 4 decimales y debe ser menor que 100,000,000.')
    expect(issues(values({ currentOdometerKm: 10.55 })).currentOdometerKm).toBe('El valor admite como máximo 1 decimales y debe ser menor que 100,000,000,000.')
    expect(issues(values({ maxWeightKg: 1_000_000_000 })).maxWeightKg).toContain('menor que 1,000,000,000')
    expect(issues(values({ maxWeightKg: 999_999_999.999, maxVolumeM3: 12.3456, currentOdometerKm: 48210.5 }))).toEqual({})
    expect(decimalIssue(null, 12, 3)).toBeNull()
  })

  it('edición: un número ya guardado no se puede dejar vacío (el PATCH leería null como «no cambiar»)', () => {
    const original = vehicleValuesOf(base)
    const r = issues(values({ modelYear: null, maxWeightKg: null, maxVolumeM3: null, maxStops: null, currentOdometerKm: null }), { original })
    expect(Object.keys(r).sort()).toEqual(['currentOdometerKm', 'maxStops', 'maxVolumeM3', 'maxWeightKg', 'modelYear'])
    expect(r.maxStops).toBe('Este valor no se puede dejar vacío una vez guardado; escriba otro.')
    // un número que nunca tuvo valor sí puede seguir vacío
    expect(issues(values({ maxStops: null }), { original: { ...original, maxStops: null } })).toEqual({})
  })
})

describe('buildVehicleCreate / buildVehiclePatch', () => {
  it('alta: código en mayúsculas y recortado, VIN sin espacios, textos vacíos → null', () => {
    const req = buildVehicleCreate({ ...emptyVehicleValues(), code: '  v-9 ', plateNumber: ' ab-1 ', vin: ' 1ftb w3 ', make: '  ', vehicleType: 'VAN', modelYear: 2020, maxStops: 30 })
    expect(req).toEqual({
      code: 'V-9',
      plateNumber: 'ab-1',
      vehicleType: 'VAN',
      ownership: null,
      fuelType: null,
      make: null,
      model: null,
      modelYear: 2020,
      vin: '1FTBW3',
      maxWeightKg: null,
      maxVolumeM3: null,
      maxStops: 30,
      currentOdometerKm: 0,
    })
    expect(normalizeVin(' a b\tc ')).toBe('ABC')
  })

  it('PATCH: solo lo cambiado y el rowVersion; sin cambios no hay nada que mandar', () => {
    expect(buildVehiclePatch(values(), base)).toEqual({ request: { rowVersion: 'AAA=' }, changed: false })
    const plan = buildVehiclePatch(values({ plateNumber: ' NEW-1 ', maxStops: 50, vehicleType: 'TRUCK', vin: ' 1ftbw3xm5jka12345 ' }), base)
    expect(plan.changed).toBe(true)
    // el VIN en minúsculas y con espacios es el mismo (el servidor lo normaliza): no cuenta como cambio
    expect(plan.request).toEqual({ rowVersion: 'AAA=', plateNumber: 'NEW-1', maxStops: 50, vehicleType: 'TRUCK' })
  })

  it('PATCH: un texto vacío se manda como cadena vacía (borra) y una lista vacía también; nunca null', () => {
    const plan = buildVehiclePatch(values({ plateNumber: '', make: '', fuelType: '', vin: '' }), base)
    expect(plan.request).toEqual({ rowVersion: 'AAA=', plateNumber: '', make: '', fuelType: '', vin: '' })
  })

  it('PATCH: el odómetro y las capacidades se mandan cuando cambian', () => {
    const plan = buildVehiclePatch(values({ currentOdometerKm: 50000.5, maxWeightKg: 1500, maxVolumeM3: 10, modelYear: 2023 }), base)
    expect(plan.request).toEqual({ rowVersion: 'AAA=', currentOdometerKm: 50000.5, maxWeightKg: 1500, maxVolumeM3: 10, modelYear: 2023 })
  })
})

describe('estado visible del vehículo', () => {
  it('baja definitiva > inactivo > en mantenimiento > disponible', () => {
    expect(vehicleState({ statusCode: 'INACTIVE', isActive: false })).toBe('retired')
    expect(vehicleState({ statusCode: 'MAINTENANCE', isActive: false })).toBe('inactive')
    expect(vehicleState({ statusCode: 'MAINTENANCE', isActive: true })).toBe('maintenance')
    expect(vehicleState({ statusCode: 'ACTIVE', isActive: true })).toBe('available')
    expect(vehicleState({ statusCode: 'active', isActive: true })).toBe('available')
  })

  it('segunda línea de la fila: tipo · propiedad · combustible, lo que falte no se pinta', () => {
    expect(vehicleMeta({ vehicleType: 'Van', ownership: 'Propio', fuelType: 'Diésel' })).toBe('Van · Propio · Diésel')
    expect(vehicleMeta({ vehicleTypeCode: 'VAN' })).toBe('VAN')
    expect(vehicleMeta({})).toBe('')
  })
})

describe('documentos del vehículo (manual 04 §1.5)', () => {
  const doc: VehicleDocument = { id: 3, docTypeCode: 'INSURANCE', docType: 'Seguro', docNumber: 'S-7', issuedDate: '2026-01-01', expiryDate: '2026-12-31', isSuperseded: false, isActive: true }

  it('el tipo es obligatorio; el número admite 80 caracteres; el vencimiento no puede ser anterior a la emisión', () => {
    const run = (v: ReturnType<typeof emptyDocumentValues>) => {
      const r = documentSchema(t).safeParse(v)
      return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
    }
    expect(run(emptyDocumentValues())).toEqual({ docType: 'El tipo de documento es obligatorio.' })
    expect(run({ ...emptyDocumentValues(), docType: 'PERMIT', docNumber: 'N'.repeat(81) })).toEqual({ docNumber: 'No puede exceder 80 caracteres.' })
    expect(run({ docType: 'PERMIT', docNumber: '', issuedDate: '2026-05-02', expiryDate: '2026-05-01' })).toEqual({
      expiryDate: 'La fecha de vencimiento no puede ser anterior a la de emisión.',
    })
    expect(run({ docType: 'PERMIT', docNumber: '', issuedDate: '2026-05-01', expiryDate: '2026-05-01' })).toEqual({})
  })

  it('alta con todo lo capturado; edición solo con lo cambiado', () => {
    const v = { docType: 'INSURANCE', docNumber: ' S-8 ', issuedDate: '2026-02-01', expiryDate: '2027-02-01' }
    expect(buildDocumentRequest(v)).toEqual({ docType: 'INSURANCE', docNumber: 'S-8', issuedDate: '2026-02-01', expiryDate: '2027-02-01' })
    const original = documentValuesOf(doc)
    expect(buildDocumentRequest(original, original)).toEqual({})
    expect(hasDocumentChanges({})).toBe(false)
    expect(buildDocumentRequest({ ...original, expiryDate: '2027-01-01' }, original)).toEqual({ expiryDate: '2027-01-01' })
  })

  it('una fecha que se vacía va con clearIssuedDate / clearExpiryDate; el número vacío como cadena vacía', () => {
    const original = documentValuesOf(doc)
    expect(buildDocumentRequest({ ...original, issuedDate: '', expiryDate: '', docNumber: '' }, original)).toEqual({ clearIssuedDate: true, clearExpiryDate: true, docNumber: '' })
    expect(hasDocumentChanges({ clearExpiryDate: true })).toBe(true)
  })
})

describe('zonas de despacho (manual 04 §2.7)', () => {
  const run = (v: { code: string; name: string }, creating = true) => {
    const r = zoneSchema(t, { creating }).safeParse(v)
    return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
  }

  it('código obligatorio y de hasta 20 caracteres; nombre de hasta 120; el nombre es opcional', () => {
    expect(run(emptyZoneValues())).toEqual({ code: 'El código de la zona es obligatorio.' })
    expect(run({ code: 'Z'.repeat(21), name: '' })).toEqual({ code: 'El código no puede exceder 20 caracteres.' })
    expect(run({ code: 'Z-1', name: 'N'.repeat(121) })).toEqual({ name: 'El nombre de la zona admite como máximo 120 caracteres.' })
    expect(run({ code: 'Z-1', name: '' })).toEqual({})
    // en la edición el código no se valida
    expect(run({ code: '', name: 'Caguas' }, false)).toEqual({})
  })

  it('alta: código en mayúsculas y recortado, nombre recortado', () => {
    expect(buildZoneCreate({ zoneGroupId: '', code: ' z-01 ', name: ' Toa Baja ' })).toEqual({ code: 'Z-01', name: 'Toa Baja', zoneGroupId: null })
  })
})

describe('documentos por vencer (manual 04 §3)', () => {
  it('la consulta lleva la ventana y «incluir vencidos»; los arreglos vacíos no se mandan', () => {
    expect(buildExpiringQuery({ withinDays: 30, includeExpired: true, docTypes: [], entities: [] })).toEqual({ withinDays: 30, includeExpired: true })
    expect(buildExpiringQuery({ withinDays: 90, includeExpired: false, docTypes: ['LICENSE', 'INSURANCE'], entities: ['DRIVER'] })).toEqual({
      withinDays: 90,
      includeExpired: false,
      docType: ['LICENSE', 'INSURANCE'],
      entity: ['DRIVER'],
    })
  })

  it('«Dentro de N días»: entero entre 0 y 365; si no, no se pide', () => {
    expect(parseWithinDays('30')).toBe(30)
    expect(parseWithinDays(' 0 ')).toBe(0)
    expect(parseWithinDays('365')).toBe(365)
    for (const bad of ['', '366', '-1', '1.5', 'abc', '3e2']) expect(parseWithinDays(bad), bad).toBeNull()
  })

  it('chip de días de la maqueta: vencido, hasta 14, hasta 30 y vigente', () => {
    expect(daysChipInfo(-5)).toEqual({ tone: 'fail', key: 'fleet.docs.expired', params: { days: 5 } })
    expect(daysChipInfo(0)).toEqual({ tone: 'route', key: 'fleet.docs.today' })
    expect(daysChipInfo(1)).toEqual({ tone: 'route', key: 'fleet.docs.daysOne', params: { days: 1 } })
    expect(daysChipInfo(14)).toEqual({ tone: 'route', key: 'fleet.docs.days', params: { days: 14 } })
    expect(daysChipInfo(15).tone).toBe('cap')
    expect(daysChipInfo(30).tone).toBe('cap')
    expect(daysChipInfo(31).tone).toBe('deliv')
    expect(translate('es', 'fleet.docs.days', { days: 14 })).toBe('14 días')
  })

  it('la pestaña de ?tab=: sin valor o desconocida es la primera', () => {
    expect(tabOf(null)).toBe('vehicles')
    expect(tabOf('zones')).toBe('zones')
    expect(tabOf('availability')).toBe('availability')
    expect(tabOf('payroll')).toBe('vehicles')
    // F-D3: las pestañas de mantenimiento, órdenes de trabajo y combustible
    expect(tabOf('maintenance')).toBe('maintenance')
    expect(tabOf('workOrders')).toBe('workOrders')
    expect(tabOf('fuel')).toBe('fuel')
  })
})

describe('motivos de disponibilidad (manual 04 §4)', () => {
  const date = (iso: string) => iso.split('-').reverse().join('/')
  const text = (code: string, message: string, blocking = true) => issueText({ code, message, blocking }, t, date)

  it('cada código se traduce a texto claro con los datos del mensaje del servidor', () => {
    expect(text('DRIVER_INACTIVE', 'El chofer está inactivo.')).toBe('El chofer está inactivo.')
    expect(text('DRIVER_STATUS', "Chofer en estatus 'No disponible'.")).toBe('El chofer está en estatus «No disponible».')
    expect(text('NO_VALID_LICENSE', 'Sin licencia vigente.')).toBe('Sin licencia vigente.')
    expect(text('NO_VALID_LICENSE', 'Licencia vencida el 2026-08-30.')).toBe('Su licencia venció el 30/08/2026.')
    expect(text('VEHICLE_INACTIVE', 'El vehículo está inactivo.')).toBe('El vehículo está inactivo.')
    expect(text('VEHICLE_STATUS', "Vehículo en estatus 'En mant.'.")).toBe('El vehículo está en estatus «En mant.».')
    expect(text('VEHICLE_DOC_EXPIRED', 'Inspección vencido el 2026-09-01.')).toBe('Inspección vencido el 01/09/2026.')
    expect(text('WORK_ORDER_IN_PROGRESS', 'Orden de trabajo WO-501 en proceso.')).toBe('Tiene la orden de trabajo WO-501 en proceso.')
    expect(text('CERT_EXPIRED', 'Certificación HazMat vencida el 2026-09-01.', false)).toBe('Certificación HazMat vencida el 01/09/2026.')
    expect(text('VEHICLE_NO_DOCUMENTS', 'El vehículo no tiene documentos registrados.', false)).toBe('El vehículo no tiene documentos registrados.')
  })

  it('DOC_EXPIRING distingue licencia, certificación y documento de vehículo', () => {
    expect(text('DOC_EXPIRING', 'Licencia CDL-A vence el 2026-10-20.', false)).toBe('Licencia CDL-A vence el 20/10/2026.')
    expect(text('DOC_EXPIRING', 'Certificación HazMat vence el 2026-10-20.', false)).toBe('Certificación HazMat vence el 20/10/2026.')
    expect(text('DOC_EXPIRING', 'Seguro vence el 2026-10-20.', false)).toBe('Seguro vence el 20/10/2026.')
  })

  it('si el mensaje no calza o el código es nuevo, se muestra el mensaje del servidor tal cual', () => {
    expect(text('DRIVER_STATUS', 'otra cosa')).toBe('otra cosa')
    expect(text('ALGO_NUEVO', 'Mensaje nuevo del servidor.')).toBe('Mensaje nuevo del servidor.')
    expect(text('ALGO_NUEVO', '')).toBe('ALGO_NUEVO')
  })

  it('en inglés los motivos salen en inglés', () => {
    expect(issueText({ code: 'DRIVER_STATUS', message: "Chofer en estatus 'No disponible'.", blocking: true }, (k, p) => translate('en', k, p), date)).toBe('The driver has status “No disponible”.')
  })

  it('primero los que bloquean y luego los avisos, sin cambiar el orden dentro de cada grupo', () => {
    const list = [
      { code: 'A', blocking: false },
      { code: 'B', blocking: true },
      { code: 'C', blocking: false },
      { code: 'D', blocking: true },
    ]
    expect(sortedIssues(list).map((i) => i.code)).toEqual(['B', 'D', 'A', 'C'])
    expect(sortedIssues(undefined)).toEqual([])
  })
})

describe('textos', () => {
  function keysOf(obj: unknown, prefix = ''): string[] {
    if (obj === null || typeof obj !== 'object') return [prefix]
    return Object.entries(obj as Record<string, unknown>).flatMap(([k, v]) => keysOf(v, prefix ? `${prefix}.${k}` : k))
  }

  it('fleet.* tiene las mismas claves en español e inglés', () => {
    expect(keysOf(enDict.fleet).sort()).toEqual(keysOf(esDict.fleet).sort())
  })

  it('los mensajes de validación del navegador son los del manual (es) y están traducidos (en)', () => {
    expect(translate('es', 'fleet.errors.codeRequired')).toBe('El código del vehículo es obligatorio.')
    expect(translate('es', 'fleet.errors.zoneNameMax', { max: 120 })).toBe('El nombre de la zona admite como máximo 120 caracteres.')
    expect(translate('en', 'fleet.errors.modelYear', { min: '1900', max: '2027' })).toBe('The model year must be between 1900 and 2027.')
  })
})
