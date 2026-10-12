// Lote F-D1 — reglas puras de tarifas, vista previa y viajes: validación de la tarifa (negativa, 4 decimales, rango), esquema por tipo
// de tarifa con la vigencia no anterior a hoy, filtros, filas editables, ejemplo de la vista previa y viajes.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../../kernel/i18n'
import type { AttemptRate, DeliveryRate, DriverRates, DriverTrip, TripRate } from './api'
import {
  attemptCellKind,
  buildDeliveryRateCreate,
  buildRateUpdate,
  buildTripQuery,
  buildTripRateCreate,
  buildTripRequest,
  canCancelTrip,
  emptyRateValues,
  emptyTripValues,
  filterDeliveryRows,
  filterTripRows,
  isOpenRow,
  PREVIEW_ATTEMPTS,
  previewRequestFor,
  rateIssue,
  rateSchema,
  tripSchema,
  uniqueOptions,
} from './rateRules'

beforeAll(() => setLang('es'))

const TODAY = '2026-10-12'

describe('rateIssue — espejo de DriverRateService.ValidateRate', () => {
  it('válidas: cero, enteros y hasta 4 decimales', () => {
    for (const ok of [0, 5, 3.5, 0.0001, 99999999999999]) expect(rateIssue(ok, t), String(ok)).toBeNull()
  })

  it('vacía, negativa, más de 4 decimales o fuera de rango (mensajes del manual)', () => {
    expect(rateIssue(null, t)).toBe('Indique la tarifa.')
    expect(rateIssue(-0.01, t)).toBe('La tarifa no puede ser negativa.')
    expect(rateIssue(1.00001, t)).toBe('El valor admite como máximo 4 decimales y debe ser menor que 100,000,000,000,000.')
    expect(rateIssue(1e14, t)).toBe('El valor admite como máximo 4 decimales y debe ser menor que 100,000,000,000,000.')
  })
})

describe('rateSchema', () => {
  const run = (needs: 'delivery' | 'trip' | false, needsDate: boolean, over = {}) => {
    const r = rateSchema(t, { needs, needsDate, today: TODAY }).safeParse({ ...emptyRateValues(TODAY), rate: 5, ...over })
    return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
  }

  it('por entrega: servicio y paquete obligatorios (mismo mensaje del servidor)', () => {
    expect(run('delivery', false)).toEqual({ serviceType: 'Indique el servicio y el tipo de paquete de la tarifa.', packageType: 'Indique el servicio y el tipo de paquete de la tarifa.' })
    expect(run('delivery', false, { serviceType: 'STANDARD', packageType: 'BOX' })).toEqual({})
  })

  it('por viaje: el tipo de viaje es obligatorio', () => {
    expect(run('trip', false)).toEqual({ tripTypeId: 'El tipo de viaje es obligatorio.' })
    expect(run('trip', false, { tripTypeId: '4' })).toEqual({})
  })

  it('la tarifa es obligatoria y no negativa; $0 es válido (distinto de «sin tarifa»)', () => {
    expect(run(false, false, { rate: null })).toEqual({ rate: 'Indique la tarifa.' })
    expect(run(false, false, { rate: -1 })).toEqual({ rate: 'La tarifa no puede ser negativa.' })
    expect(run(false, false, { rate: 0 })).toEqual({})
  })

  it('la vigencia no puede ser anterior a hoy; hoy y el futuro valen; vacía no vale', () => {
    expect(run(false, true, { effectiveFrom: '2026-10-11' })).toEqual({ effectiveFrom: 'La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.' })
    expect(run(false, true, { effectiveFrom: TODAY })).toEqual({})
    expect(run(false, true, { effectiveFrom: '2027-01-01' })).toEqual({})
    expect(run(false, true, { effectiveFrom: '' })).toEqual({ effectiveFrom: 'Indique desde cuándo rige.' })
    // sin fecha en el formulario (alta inline) no se valida
    expect(run(false, false, { effectiveFrom: '' })).toEqual({})
  })
})

describe('requests de tarifa', () => {
  it('alta por entrega y por viaje no llevan fecha (el servidor usa hoy)', () => {
    const v = { ...emptyRateValues(TODAY), serviceType: 'STANDARD', packageType: 'BOX', tripTypeId: '4', rate: 3.5 }
    expect(buildDeliveryRateCreate(v)).toEqual({ serviceType: 'STANDARD', packageType: 'BOX', rate: 3.5 })
    expect(buildTripRateCreate(v)).toEqual({ specialServiceTypeId: 4, rate: 3.5 })
  })

  it('editar manda la tarifa nueva y desde cuándo rige', () => {
    expect(buildRateUpdate({ ...emptyRateValues('2026-11-01'), rate: 0 })).toEqual({ rate: 0, effectiveFrom: '2026-11-01' })
  })
})

describe('filas y filtros', () => {
  const d = (id: number, svc: string, pkg: string, extra = {}): DeliveryRate => ({
    id,
    serviceTypeCode: svc,
    serviceType: svc,
    packageTypeCode: pkg,
    packageType: pkg,
    rate: 1,
    effectiveFrom: '2026-10-01',
    isCurrent: true,
    ...extra,
  })
  const rows = [d(1, 'STANDARD', 'BOX'), d(2, 'STANDARD', 'ENVELOPE'), d(3, 'NEXT_DAY', 'BOX')]

  it('Servicio y Paquete filtran por código; vacío = todos', () => {
    expect(filterDeliveryRows(rows, [], []).map((r) => r.id)).toEqual([1, 2, 3])
    expect(filterDeliveryRows(rows, ['STANDARD'], []).map((r) => r.id)).toEqual([1, 2])
    expect(filterDeliveryRows(rows, [], ['BOX']).map((r) => r.id)).toEqual([1, 3])
    expect(filterDeliveryRows(rows, ['STANDARD'], ['BOX']).map((r) => r.id)).toEqual([1])
    expect(filterDeliveryRows(rows, ['NADA'], [])).toEqual([])
  })

  it('«Tipo de viaje» filtra por id', () => {
    const trips: TripRate[] = [
      { id: 1, specialServiceTypeId: 4, tripType: 'Vagón', tripTypeActive: true, rate: 75, effectiveFrom: '2026-10-01', isCurrent: true },
      { id: 2, specialServiceTypeId: 5, tripType: 'Traslado', tripTypeActive: true, rate: 50, effectiveFrom: '2026-10-01', isCurrent: true },
    ]
    expect(filterTripRows(trips, ['5']).map((r) => r.id)).toEqual([2])
    expect(filterTripRows(trips, []).length).toBe(2)
  })

  it('uniqueOptions: sin repetidos ni vacíos, ordenadas por etiqueta', () => {
    expect(uniqueOptions(rows, (r) => ({ value: r.serviceTypeCode ?? '', label: r.serviceType ?? '' }))).toEqual([
      { value: 'NEXT_DAY', label: 'NEXT_DAY' },
      { value: 'STANDARD', label: 'STANDARD' },
    ])
    expect(uniqueOptions([{ v: '' }], (r) => ({ value: r.v, label: 'x' }))).toEqual([])
  })

  it('solo una fila abierta (sin effectiveTo) se edita o se cierra; una cerrada a futuro tampoco', () => {
    expect(isOpenRow({ effectiveTo: null })).toBe(true)
    expect(isOpenRow({})).toBe(true)
    expect(isOpenRow({ effectiveTo: '2026-12-31' })).toBe(false)
  })

  it('«sin tarifa» (sin fila) es distinto de abierta y de cerrada', () => {
    const cell = (over: Partial<AttemptRate>): AttemptRate => ({ attemptNumber: 1, isCurrent: false, ...over })
    expect(attemptCellKind(cell({}))).toBe('none')
    expect(attemptCellKind(cell({ id: 3, rate: 0, isCurrent: true }))).toBe('open')
    expect(attemptCellKind(cell({ id: 3, rate: 2, effectiveTo: '2026-10-01' }))).toBe('closed')
  })
})

describe('previewRequestFor — el ejemplo de la maqueta (2 intentos: 1 fallido + 1 entregado)', () => {
  const rates = (over: Partial<DriverRates> = {}): DriverRates => ({
    driverPublicId: 'd',
    readOnly: false,
    asOf: TODAY,
    attemptLevels: 3,
    deliveryRates: [
      { id: 9, serviceTypeCode: 'S', packageTypeCode: 'B', rate: 6, effectiveFrom: '2026-01-01', effectiveTo: '2026-06-01', isCurrent: false },
      { id: 10, serviceTypeCode: 'S', packageTypeCode: 'B', rate: 7.5, effectiveFrom: '2026-06-01', isCurrent: true },
    ],
    attemptRates: [
      { attemptNumber: 1, id: 1, rate: 0, effectiveFrom: '2026-01-01', isCurrent: true },
      { attemptNumber: 2, id: 2, rate: 3, effectiveFrom: '2026-01-01', isCurrent: true },
      { attemptNumber: 3, isCurrent: false },
    ],
    tripRates: [],
    ...over,
  })

  it('usa la tarifa de entrega vigente y las tarifas vigentes de cada intento (un $0 se manda; un nivel sin tarifa no)', () => {
    expect(previewRequestFor(rates())).toEqual({
      attempts: [
        { number: 1, delivered: false },
        { number: 2, delivered: true },
      ],
      deliveryRate: 7.5,
      attemptRates: [
        { number: 1, rate: 0 },
        { number: 2, rate: 3 },
      ],
    })
  })

  it('sin chofer o sin tarifas: deliveryRate null y sin tarifas de intento (el servidor pone ceros con nota)', () => {
    expect(previewRequestFor(undefined)).toEqual({ attempts: PREVIEW_ATTEMPTS, deliveryRate: null, attemptRates: [] })
    expect(previewRequestFor(rates({ deliveryRates: [], attemptRates: [] }))).toMatchObject({ deliveryRate: null, attemptRates: [] })
  })
})

describe('viajes pagados', () => {
  const run = (v: Partial<ReturnType<typeof emptyTripValues>>) => {
    const r = tripSchema(t, TODAY).safeParse({ ...emptyTripValues(TODAY), specialServiceTypeId: '4', ...v })
    return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
  }

  it('tipo obligatorio, fecha no futura y notas ≤ 500 (mensajes del manual 04 §9)', () => {
    expect(run({ specialServiceTypeId: '' })).toEqual({ specialServiceTypeId: 'El tipo de viaje es obligatorio.' })
    expect(run({ tripDate: '2026-10-13' })).toEqual({ tripDate: 'La fecha del viaje no puede ser futura.' })
    expect(run({ tripDate: '' })).toEqual({ tripDate: 'Indique la fecha del viaje.' })
    expect(run({ tripDate: TODAY })).toEqual({})
    expect(run({ notes: 'x'.repeat(501) })).toEqual({ notes: 'Las notas del viaje admiten como máximo 500 caracteres.' })
    expect(run({ notes: 'x'.repeat(500) })).toEqual({})
  })

  it('request: tipo numérico, fecha y notas recortadas (vacías → null)', () => {
    expect(buildTripRequest({ specialServiceTypeId: '4', tripDate: TODAY, notes: '  entrega  ' })).toEqual({ specialServiceTypeId: 4, tripDate: TODAY, notes: 'entrega' })
    expect(buildTripRequest({ specialServiceTypeId: '4', tripDate: TODAY, notes: '   ' }).notes).toBeNull()
  })

  it('consulta: fechas y estatus solo si se eligieron', () => {
    expect(buildTripQuery({ from: '', to: '', status: '' })).toEqual({})
    expect(buildTripQuery({ from: '2026-10-01', to: '2026-10-12', status: 'CANCELLED' })).toEqual({ from: '2026-10-01', to: '2026-10-12', status: ['CANCELLED'] })
  })

  it('«Cancelar» solo en un viaje abierto y vigente', () => {
    const trip = (statusCode: string, isActive = true) => ({ statusCode, isActive }) as Pick<DriverTrip, 'statusCode' | 'isActive'>
    expect(canCancelTrip(trip('OPEN'))).toBe(true)
    expect(canCancelTrip(trip('SETTLED'))).toBe(false)
    expect(canCancelTrip(trip('CANCELLED', false))).toBe(false)
    expect(canCancelTrip(trip('OPEN', false))).toBe(false)
  })
})
