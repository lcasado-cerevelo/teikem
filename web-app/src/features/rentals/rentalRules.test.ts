// Lote F17 (Rentas F-R1) — lógica pura de las pantallas de rentas: filtros y URL, vencimiento calculado, validaciones con los
// mensajes exactos del servidor (manual 11 §9), equipos elegidos y los cuerpos de alta, edición y extensión.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import {
  activeLines,
  addDays,
  addEquipment,
  createRentalBody,
  datesIssue,
  dueLabel,
  dueState,
  EMPTY_RATE,
  EMPTY_RENTAL_FILTERS,
  equipmentLines,
  extendBody,
  extensionIssues,
  hasRentalFilters,
  headerValuesOf,
  lineState,
  parseDueDays,
  rateChanged,
  rateInput,
  rateIssue,
  rentalFiltersFromUrl,
  rentalListQuery,
  rentalPatchBody,
  scannedSerialIssue,
  serialKey,
  type PickedEquipment,
  type RentalDto,
} from './rentalRules'

const es = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
beforeAll(() => setLang('es'))

const P1 = 'aaaaaaaa-0000-0000-0000-000000000001'
const P2 = 'aaaaaaaa-0000-0000-0000-000000000002'
const eq = (serial: string, productPublicId = P1, rate: PickedEquipment['rate'] = null): PickedEquipment => ({
  productPublicId,
  sku: productPublicId === P1 ? 'EQ-1' : 'EQ-2',
  productName: 'Cama',
  serialNumber: serial,
  binCode: 'A-01',
  rate,
})

const RENTAL: RentalDto = {
  rental: {
    id: 7,
    publicId: 'r1',
    number: 'REN-00007',
    clientPublicId: 'c1',
    locationPublicId: 'l1',
    warehousePublicId: 'w1',
    startDate: '2026-10-05',
    pickupDate: '2026-11-04',
    originalPickupDate: '2026-11-04',
    contractNumber: 'CT-77',
    statusCode: 'DRAFT',
  },
  clientContactId: 12,
  contractSignedOn: '2026-10-04',
  estimatedDeliveryCost: 45,
  transportCurrencyCode: 'USD',
  notes: 'Llevar cargador',
  rowVersion: 'AAAA',
  lines: [
    { id: 31, serialNumber: 'SN-1', isActive: true, rate: { frequencyCode: 'MONTHLY', amount: 150, currencyCode: 'USD' } },
    { id: 32, serialNumber: 'SN-2', isActive: true, rate: { frequencyCode: 'MONTHLY', amount: 165, currencyCode: 'USD' } },
    { id: 33, serialNumber: 'SN-3', isActive: false },
  ],
}

describe('filtros de la lista', () => {
  it('desde la URL (una vez): "Ver todos" del aviso trae dueWithinDays=7 y overdue=true; status repetible o con comas', () => {
    expect(rentalFiltersFromUrl(new URLSearchParams('dueWithinDays=7&overdue=true'))).toEqual({ ...EMPTY_RENTAL_FILTERS, dueWithinDays: '7', overdue: true })
    expect(rentalFiltersFromUrl(new URLSearchParams('status=draft,SCHEDULED&status=ON_RENT&status=ON_RENT&clientPublicId=c1&search=SN-1')).status).toEqual([
      'DRAFT',
      'SCHEDULED',
      'ON_RENT',
    ])
    const junk = rentalFiltersFromUrl(new URLSearchParams('dueWithinDays=-3&overdue=1&status=<x>'))
    expect(junk).toEqual(EMPTY_RENTAL_FILTERS)
  })

  it('consulta del API: solo lo que tiene valor; días enteros de 0 a 3650', () => {
    expect(rentalListQuery(EMPTY_RENTAL_FILTERS)).toEqual({})
    expect(rentalListQuery({ status: ['ON_RENT'], clientPublicId: 'c1', dueWithinDays: '0', overdue: true, search: '  REN-1 ' })).toEqual({
      status: ['ON_RENT'],
      clientPublicId: 'c1',
      dueWithinDays: 0,
      overdue: true,
      search: 'REN-1',
    })
    expect(parseDueDays('7')).toBe(7)
    expect(parseDueDays('7.5')).toBeNull()
    expect(parseDueDays('4000')).toBeNull()
    expect(parseDueDays('')).toBeNull()
    expect(hasRentalFilters(EMPTY_RENTAL_FILTERS)).toBe(false)
    expect(hasRentalFilters({ ...EMPTY_RENTAL_FILTERS, overdue: true })).toBe(true)
  })
})

describe('vencimiento (dato calculado, no estatus)', () => {
  it('solo rentas abiertas (Programada o En renta); vencida, hoy, en ≤ 7 días o más', () => {
    expect(dueState({ statusCode: 'DRAFT', daysToPickup: -3, isOverdue: false })).toBeNull()
    expect(dueState({ statusCode: 'RETURNED', daysToPickup: -3, isOverdue: false })).toBeNull()
    expect(dueState({ statusCode: 'SCHEDULED', daysToPickup: -2, isOverdue: true })).toBe('overdue')
    expect(dueState({ statusCode: 'ON_RENT', daysToPickup: 0, isOverdue: false })).toBe('today')
    expect(dueState({ statusCode: 'ON_RENT', daysToPickup: 7 })).toBe('soon')
    expect(dueState({ statusCode: 'ON_RENT', daysToPickup: 8 })).toBe('later')
  })

  it('textos', () => {
    expect(dueLabel(es, 'overdue', -3)).toBe('Vencida hace 3 días')
    expect(dueLabel(es, 'overdue', -1)).toBe('Vencida hace 1 día')
    expect(dueLabel(es, 'today', 0)).toBe('Se recoge hoy')
    expect(dueLabel(es, 'soon', 5)).toBe('Vence en 5 días')
    expect(dueLabel(es, 'soon', 1)).toBe('Vence en 1 día')
    expect(dueLabel(es, 'later', 30)).toBe('En 30 días')
  })
})

describe('validaciones (mensajes exactos del servidor)', () => {
  it('fechas: recogido antes del inicio', () => {
    expect(datesIssue('2026-10-05', '2026-10-04')).toEqual({ code: 'pickupBeforeStart' })
    expect(datesIssue('2026-10-05', '2026-10-05')).toBeNull()
    expect(datesIssue('', '2026-10-04')).toBeNull()
    expect(es('rentals.errors.pickupBeforeStart')).toBe('La fecha de recogido no puede ser anterior a la de inicio.')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
  })

  it('extensión: el 400 de fecha (igual o anterior a la vigente) y el de motivo', () => {
    const same = extensionIssues({ currentPickupDate: '2026-11-04', newPickupDate: '2026-11-04', reason: ' ' })
    expect(es(`rentals.errors.${same.newPickupDate!.code}`, same.newPickupDate!.params)).toBe(
      'La nueva fecha de recogido debe ser posterior a la actual (2026-11-04).',
    )
    expect(es(`rentals.errors.${same.reason!.code}`)).toBe('Indique el motivo de la extensión.')
    expect(extensionIssues({ currentPickupDate: '2026-11-04', newPickupDate: '', reason: 'x' }).newPickupDate).toEqual({ code: 'newPickupRequired' })
    expect(es('rentals.errors.newPickupRequired')).toBe('Indique la nueva fecha de recogido.')
    expect(extensionIssues({ currentPickupDate: '2026-11-04', newPickupDate: '2026-11-05', reason: 'x'.repeat(301) }).reason).toEqual({ code: 'reasonTooLong' })
    expect(extensionIssues({ currentPickupDate: '2026-11-04', newPickupDate: '2026-11-05', reason: 'Dos semanas más' })).toEqual({})
  })

  it('tarifa: vacía = sin tarifa; frecuencia y monto ≥ 0', () => {
    expect(rateIssue(EMPTY_RATE)).toBeNull()
    expect(rateIssue({ frequency: '', amount: 10, currency: '' })).toEqual({ field: 'frequency', issue: { code: 'frequencyRequired' } })
    expect(rateIssue({ frequency: 'DAILY', amount: null, currency: '' })).toEqual({ field: 'amount', issue: { code: 'rateAmountRequired' } })
    expect(rateIssue({ frequency: 'DAILY', amount: -1, currency: '' })).toEqual({ field: 'amount', issue: { code: 'negativeRate' } })
    expect(es('rentals.errors.negativeRate')).toBe('La tarifa no puede ser negativa.')
    expect(es('rentals.errors.frequencyRequired')).toBe('Indique la frecuencia de cobro: DAILY, WEEKLY, MONTHLY o ONE_TIME.')
    expect(rateInput({ frequency: 'ONE_TIME', amount: 0, currency: '' })).toEqual({ frequency: 'ONE_TIME', amount: 0, currency: null })
    expect(rateInput(EMPTY_RATE)).toBeNull()
  })

  it('mensajes de equipos del manual', () => {
    expect(es('rentals.errors.notSerial', { sku: 'CAMA-1' })).toBe('El producto CAMA-1 no se controla por serie; solo se rentan equipos con número de serie.')
    expect(es('rentals.errors.notOwn', { sku: 'CAMA-1' })).toBe('Solo se rentan equipos propios; CAMA-1 pertenece a un cliente.')
    expect(es('rentals.errors.locationRequired')).toBe('Indique la localidad del cliente donde estará el equipo.')
  })
})

describe('equipos elegidos (series)', () => {
  it('no repite series (sin distinguir mayúsculas ni espacios) y avisa las repetidas', () => {
    const { list, duplicates } = addEquipment([eq('SN-1')], [eq(' sn-1 '), eq('SN-2'), eq('SN-2')])
    expect(list.map((e) => e.serialNumber)).toEqual(['SN-1', 'SN-2'])
    expect(duplicates).toEqual([' sn-1 ', 'SN-2'])
    expect(serialKey(' sn-1 ')).toBe('SN-1')
  })

  it('renglones: uno por producto y tarifa, en el orden en que se eligieron', () => {
    const rate = { frequency: 'MONTHLY', amount: 150, currency: null }
    expect(equipmentLines([eq('S1', P1, rate), eq('S2', P2), eq('S3', P1, rate), eq('S4', P1)])).toEqual([
      { productPublicId: P1, serialNumbers: ['S1', 'S3'], rate },
      { productPublicId: P2, serialNumbers: ['S2'] },
      { productPublicId: P1, serialNumbers: ['S4'] },
    ])
  })

  it('serie escaneada: ya elegida o no disponible en el almacén (mensaje del 409 del servidor)', () => {
    const ctx = { picked: new Set(['SN-1']), available: new Map([['SN-2', {}]]), warehouseCode: 'ALM-01' }
    expect(scannedSerialIssue('sn-1', ctx)).toEqual({ code: 'serialAlreadyPicked', params: { serial: 'sn-1' } })
    const missing = scannedSerialIssue('SN-9', ctx)!
    expect(es(`rentals.errors.${missing.code}`, missing.params)).toBe('La serie SN-9 no está disponible en ALM-01.')
    expect(scannedSerialIssue('SN-2', ctx)).toBeNull()
  })

  it('estado de cada equipo y equipos activos', () => {
    expect(lineState({ isActive: false, dispatchedAtUtc: '2026-10-01T00:00:00' })).toBe('inactive')
    expect(lineState({ isActive: true, dispatchedAtUtc: 'x', returnedAtUtc: 'y' })).toBe('returned')
    expect(lineState({ isActive: true, dispatchedAtUtc: 'x' })).toBe('dispatched')
    expect(lineState({ isActive: true })).toBe('pending')
    expect(activeLines(RENTAL).map((l) => l.id)).toEqual([31, 32])
  })
})

describe('cuerpos del API', () => {
  it('alta: vacíos → null, moneda solo con costo, renglones agrupados', () => {
    const body = createRentalBody(
      {
        clientPublicId: 'c1',
        locationPublicId: 'l1',
        clientContactId: '',
        warehousePublicId: 'w1',
        startDate: '2026-10-05',
        pickupDate: '2026-11-04',
        contractNumber: '  ',
        contractSignedOn: '',
        estimatedDeliveryCost: null,
        transportCurrency: 'USD',
        notes: ' Llevar cargador ',
      },
      [eq('S1'), eq('S2')],
    )
    expect(body).toEqual({
      clientPublicId: 'c1',
      locationPublicId: 'l1',
      clientContactId: null,
      warehousePublicId: 'w1',
      startDate: '2026-10-05',
      pickupDate: '2026-11-04',
      contractNumber: null,
      contractSignedOn: null,
      estimatedDeliveryCost: null,
      transportCurrency: null,
      notes: 'Llevar cargador',
      lines: [{ productPublicId: P1, serialNumbers: ['S1', 'S2'] }],
    })
    expect(createRentalBody({ ...headerValuesOf(RENTAL), clientContactId: '12' }, []).lines).toBeUndefined()
  })

  it('edición: solo lo que cambió, con clear… al vaciar y el rowVersion; sin cambios = null', () => {
    const same = headerValuesOf(RENTAL)
    expect(rentalPatchBody(RENTAL, same)).toBeNull()
    expect(
      rentalPatchBody(RENTAL, { ...same, clientContactId: '', contractSignedOn: '', estimatedDeliveryCost: null, contractNumber: '', notes: '', pickupDate: '2026-11-10' }),
    ).toEqual({
      clearClientContact: true,
      clearContractSignedOn: true,
      clearEstimatedDeliveryCost: true,
      contractNumber: '',
      notes: '',
      pickupDate: '2026-11-10',
      rowVersion: 'AAAA',
    })
    expect(rentalPatchBody(RENTAL, { ...same, locationPublicId: 'l2', estimatedDeliveryCost: 50, transportCurrency: 'EUR' })).toEqual({
      locationPublicId: 'l2',
      estimatedDeliveryCost: 50,
      transportCurrency: 'EUR',
      rowVersion: 'AAAA',
    })
  })

  it('extensión: tarifa nueva solo a los equipos elegidos cuya tarifa cambia', () => {
    const rate = { frequency: 'MONTHLY', amount: 165, currency: '' }
    expect(rateChanged(rate, RENTAL.lines![0].rate)).toBe(true)
    expect(rateChanged(rate, RENTAL.lines![1].rate)).toBe(false)
    expect(extendBody(RENTAL, { newPickupDate: '2026-11-19', reason: ' más tiempo ', rate, lineIds: [31, 32, 33] })).toEqual({
      newPickupDate: '2026-11-19',
      reason: 'más tiempo',
      rates: [{ lineId: 31, frequency: 'MONTHLY', amount: 165, currency: null }],
      rowVersion: 'AAAA',
    })
    expect(extendBody(RENTAL, { newPickupDate: '2026-11-19', reason: 'x', rate: EMPTY_RATE, lineIds: [31] })).toEqual({
      newPickupDate: '2026-11-19',
      reason: 'x',
      rowVersion: 'AAAA',
    })
  })
})
