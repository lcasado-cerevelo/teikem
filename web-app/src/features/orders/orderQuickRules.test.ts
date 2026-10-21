// Lote F-C3 — reglas puras de la entrada rápida: validación de la fila, request (borrador de 1 paquete y 1 pieza), errores del servidor
// por campo, preferencia Rápida/Detallada y paridad es/en de `orders.quick.*` y `orders.import.*`.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import en from '../../kernel/i18n/en.json'
import es from '../../kernel/i18n/es.json'
import {
  buildQuickRequest,
  emptyQuickRow,
  ENTRY_MODE_KEY,
  isOffDate,
  nextQuickRow,
  quickCod,
  quickFieldOf,
  quickProblemOf,
  readEntryMode,
  saveEntryMode,
  validateQuickRow,
  type QuickRowValues,
} from './orderQuickRules'

const TODAY = '2026-10-21'
const M = {
  dateRequired: 'Indique la fecha del envío.',
  pastDate: 'La fecha del envío no puede ser anterior a hoy.',
  clientRequired: 'El cliente es obligatorio.',
  consigneeRequired: 'El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.',
  packageTypeRequired: 'El tipo de paquete es obligatorio.',
  serviceRequired: 'El tipo de servicio es obligatorio.',
  codInvalid: 'El monto COD debe ser un número.',
  codNegative: 'El monto COD no puede ser negativo.',
  numberMax: 'El número no puede exceder 40 caracteres.',
}
const OK: QuickRowValues = { date: TODAY, invoiceNumber: '', orderNumber: '', consigneeId: 'l-1', packageType: 'BOX', cod: '', serviceType: 'STANDARD', notes: '' }

describe('validateQuickRow', () => {
  it('una fila completa no tiene errores', () => {
    expect(validateQuickRow('c-1', OK, { orderNumberEditable: false, today: TODAY }, M)).toEqual({})
  })
  it('pide cliente, consignatario, paquete y servicio con los mensajes del servidor', () => {
    expect(validateQuickRow(null, { ...OK, consigneeId: '', packageType: '', serviceType: '' }, { orderNumberEditable: false, today: TODAY }, M)).toEqual({
      clientPublicId: M.clientRequired,
      consigneeId: M.consigneeRequired,
      packageType: M.packageTypeRequired,
      serviceType: M.serviceRequired,
    })
  })
  it('la fecha es obligatoria y no puede ser anterior a hoy (hoy y mañana valen; ayer se bloquea)', () => {
    const ctx = { orderNumberEditable: false, today: TODAY }
    expect(validateQuickRow('c-1', { ...OK, date: '' }, ctx, M).date).toBe(M.dateRequired)
    expect(validateQuickRow('c-1', { ...OK, date: '2026-10-20' }, ctx, M).date).toBe(M.pastDate)
    expect(validateQuickRow('c-1', { ...OK, date: TODAY }, ctx, M).date).toBeUndefined()
    expect(validateQuickRow('c-1', { ...OK, date: '2026-10-22' }, ctx, M).date).toBeUndefined()
  })
  it('el número de factura solo se valida si el cliente lo asigna (máx. 40)', () => {
    const long = 'x'.repeat(41)
    expect(validateQuickRow('c-1', { ...OK, invoiceNumber: long }, { orderNumberEditable: false, invoiceNumberEditable: true, today: TODAY }, M).invoiceNumber).toBe(M.numberMax)
    expect(validateQuickRow('c-1', { ...OK, invoiceNumber: long }, { orderNumberEditable: false, today: TODAY }, M).invoiceNumber).toBeUndefined()
  })
  it('COD: vacío y 0 valen; texto o negativo no', () => {
    expect(validateQuickRow('c-1', { ...OK, cod: '' }, { orderNumberEditable: false, today: TODAY }, M)).toEqual({})
    expect(validateQuickRow('c-1', { ...OK, cod: '0' }, { orderNumberEditable: false, today: TODAY }, M)).toEqual({})
    expect(validateQuickRow('c-1', { ...OK, cod: '12abc' }, { orderNumberEditable: false, today: TODAY }, M).cod).toBe(M.codInvalid)
    expect(validateQuickRow('c-1', { ...OK, cod: '-1' }, { orderNumberEditable: false, today: TODAY }, M).cod).toBe(M.codNegative)
  })
  it('el número de orden solo se valida si el cliente lo asigna (máx. 40)', () => {
    const long = 'x'.repeat(41)
    expect(validateQuickRow('c-1', { ...OK, orderNumber: long }, { orderNumberEditable: true, today: TODAY }, M).orderNumber).toBe(M.numberMax)
    expect(validateQuickRow('c-1', { ...OK, orderNumber: long }, { orderNumberEditable: false, today: TODAY }, M).orderNumber).toBeUndefined()
  })
})

describe('quickCod', () => {
  it('lee con los separadores de la compañía (Puerto Rico: coma de miles, punto decimal)', () => {
    expect(quickCod('')).toBeNull()
    expect(quickCod('  ')).toBeNull()
    expect(quickCod('1,234.50')).toBe(1234.5)
    expect(quickCod('50')).toBe(50)
    expect(quickCod('abc')).toBeNaN()
  })
})

describe('buildQuickRequest', () => {
  const opts = { orderNumberEditable: false }
  it('una línea de 1 pieza, Sometida (sin confirmNow) y la fecha elegida en la fila', () => {
    expect(buildQuickRequest('c-1', { ...OK, cod: '50', notes: ' hola ' }, opts)).toEqual({
      clientPublicId: 'c-1',
      consigneeLocationPublicId: 'l-1',
      serviceType: 'STANDARD',
      packages: [{ packageType: 'BOX', pieces: 1 }],
      codAmount: 50,
      requestedDate: '2026-10-21T00:00:00',
      promisedDate: '2026-10-21T00:00:00',
      notes: 'hola',
    })
  })
  it('sin COD ni comentario manda null; el número de orden solo si el cliente lo asigna y se tecleó', () => {
    const r = buildQuickRequest('c-1', { ...OK, cod: '0', orderNumber: ' PO-1 ' }, opts)
    expect(r.codAmount).toBeNull()
    expect(r.notes).toBeNull()
    expect(r).not.toHaveProperty('orderNumber')
    expect(buildQuickRequest('c-1', { ...OK, orderNumber: ' PO-1 ' }, { ...opts, orderNumberEditable: true }).orderNumber).toBe('PO-1')
    expect(buildQuickRequest('c-1', OK, { ...opts, orderNumberEditable: true })).not.toHaveProperty('orderNumber')
  })
  it('la fecha de la fila (no la de hoy) es la del envío; la factura solo si el cliente la asigna y se tecleó', () => {
    const r = buildQuickRequest('c-1', { ...OK, date: '2026-10-23', invoiceNumber: ' FAC-9 ' }, { ...opts, invoiceNumberEditable: true })
    expect(r.requestedDate).toBe('2026-10-23T00:00:00')
    expect(r.promisedDate).toBe('2026-10-23T00:00:00')
    expect(r.clientInvoiceNumber).toBe('FAC-9')
    expect(buildQuickRequest('c-1', { ...OK, invoiceNumber: 'FAC-9' }, opts)).not.toHaveProperty('clientInvoiceNumber')
    expect(buildQuickRequest('c-1', OK, { ...opts, invoiceNumberEditable: true })).not.toHaveProperty('clientInvoiceNumber')
  })
})

describe('nextQuickRow', () => {
  it('la fecha es la de la última orden guardada; servicio y paquete, los del cliente (o los de la fila anterior); lo demás se limpia', () => {
    const next = nextQuickRow({ ...OK, date: '2026-10-22', serviceType: 'EXPRESS', cod: '9', notes: 'x', orderNumber: 'A', invoiceNumber: 'F', packageType: 'ENV' }, { serviceType: 'SAMEDAY', packageType: 'BOX' })
    expect(next).toEqual({ date: '2026-10-22', invoiceNumber: '', orderNumber: '', consigneeId: '', packageType: 'BOX', cod: '', serviceType: 'SAMEDAY', notes: '' })
    // sin predeterminados se conservan el servicio y el paquete de la fila anterior
    const kept = nextQuickRow({ ...OK, serviceType: 'EXPRESS', packageType: 'ENV' }, {})
    expect(kept.serviceType).toBe('EXPRESS')
    expect(kept.packageType).toBe('ENV')
    expect(emptyQuickRow()).toEqual({ date: '', invoiceNumber: '', orderNumber: '', consigneeId: '', packageType: '', cod: '', serviceType: '', notes: '' })
  })
  it('una fecha distinta de hoy se marca (esas filas se sombrean)', () => {
    expect(isOffDate('2026-10-22', TODAY)).toBe(true)
    expect(isOffDate(TODAY, TODAY)).toBe(false)
    expect(isOffDate('', TODAY)).toBe(false)
  })
})

describe('errores del servidor', () => {
  it('los nombres de campo del servidor pasan a campos de la fila', () => {
    expect(quickFieldOf('consignee')).toBe('consigneeId')
    expect(quickFieldOf('consigneeLocationPublicId')).toBe('consigneeId')
    expect(quickFieldOf('$.packages[0].packageType')).toBe('packageType')
    expect(quickFieldOf('packages')).toBe('packageType')
    expect(quickFieldOf('serviceType')).toBe('serviceType')
    expect(quickFieldOf('codAmount')).toBe('cod')
    expect(quickFieldOf('orderNumber')).toBe('orderNumber')
    expect(quickFieldOf('clientInvoiceNumber')).toBe('invoiceNumber')
    expect(quickFieldOf('requestedDate')).toBe('date')
  })
  it('separa campos de la fila, el resto y los datos de factura repetida', () => {
    const err = new ApiError(409, {
      title: 'Factura repetida.',
      code: 'duplicate_invoice',
      errors: { clientInvoiceNumber: ['Factura repetida.'], existingOrderNumber: ['ORD-1'], existingOrderPublicId: ['o-1'], 'packages[0].pieces': ['La cantidad de piezas debe ser al menos 1.'] },
    })
    const p = quickProblemOf(err)
    expect(p.title).toBe('Factura repetida.')
    expect(p.fields).toEqual({ invoiceNumber: 'Factura repetida.' })   // se ve bajo el campo de la factura
    expect(p.others).toEqual(['La cantidad de piezas debe ser al menos 1.'])
    expect(p.retryable).toBe(false)
  })
  it('una falla de red o un 5xx se puede reintentar con la misma clave; un 4xx no', () => {
    expect(quickProblemOf(new TypeError('x')).retryable).toBe(true)
    expect(quickProblemOf(new ApiError(503, { title: 'No disponible', code: 'internal', errors: {} })).retryable).toBe(true)
    expect(quickProblemOf(new ApiError(422, { title: 'No', code: 'status_rule', errors: {} })).retryable).toBe(false)
  })
})

describe('preferencia Rápida / Detallada', () => {
  afterEach(() => {
    window.localStorage.clear()
    vi.restoreAllMocks()
  })
  it('por omisión Rápida; guarda y recuerda la elección; un valor raro vuelve a Rápida', () => {
    expect(readEntryMode()).toBe('quick')
    saveEntryMode('detail')
    expect(window.localStorage.getItem(ENTRY_MODE_KEY)).toBe('detail')
    expect(readEntryMode()).toBe('detail')
    window.localStorage.setItem(ENTRY_MODE_KEY, 'otra-cosa')
    expect(readEntryMode()).toBe('quick')
  })
  it('un almacenamiento bloqueado no lanza', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('bloqueado')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('bloqueado')
    })
    expect(readEntryMode()).toBe('quick')
    expect(() => saveEntryMode('detail')).not.toThrow()
  })
})

describe('textos', () => {
  const keys = (o: unknown, prefix = ''): string[] =>
    Object.entries(o as Record<string, unknown>).flatMap(([k, v]) => (typeof v === 'object' && v !== null ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]))
  it('orders.quick, orders.import y los textos nuevos de orders.entry/list tienen las mismas claves en es y en', () => {
    for (const section of ['quick', 'import'] as const) expect(keys(en.orders[section]).sort()).toEqual(keys(es.orders[section]).sort())
    for (const k of ['mode', 'quick', 'detailed'] as const) expect(en.orders.entry[k]).toBeTruthy()
    expect(en.orders.list.import).toBeTruthy()
  })
})
