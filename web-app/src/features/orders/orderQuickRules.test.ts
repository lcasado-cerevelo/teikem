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
  nextQuickRow,
  quickCod,
  quickFieldOf,
  quickProblemOf,
  readEntryMode,
  saveEntryMode,
  validateQuickRow,
  type QuickRowValues,
} from './orderQuickRules'

const M = {
  clientRequired: 'El cliente es obligatorio.',
  consigneeRequired: 'El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.',
  packageTypeRequired: 'El tipo de paquete es obligatorio.',
  serviceRequired: 'El tipo de servicio es obligatorio.',
  codInvalid: 'El monto COD debe ser un número.',
  codNegative: 'El monto COD no puede ser negativo.',
  numberMax: 'El número no puede exceder 40 caracteres.',
}
const OK: QuickRowValues = { orderNumber: '', consigneeId: 'l-1', packageType: 'BOX', cod: '', serviceType: 'STANDARD', notes: '' }

describe('validateQuickRow', () => {
  it('una fila completa no tiene errores', () => {
    expect(validateQuickRow('c-1', OK, false, M)).toEqual({})
  })
  it('pide cliente, consignatario, paquete y servicio con los mensajes del servidor', () => {
    expect(validateQuickRow(null, { ...OK, consigneeId: '', packageType: '', serviceType: '' }, false, M)).toEqual({
      clientPublicId: M.clientRequired,
      consigneeId: M.consigneeRequired,
      packageType: M.packageTypeRequired,
      serviceType: M.serviceRequired,
    })
  })
  it('COD: vacío y 0 valen; texto o negativo no', () => {
    expect(validateQuickRow('c-1', { ...OK, cod: '' }, false, M)).toEqual({})
    expect(validateQuickRow('c-1', { ...OK, cod: '0' }, false, M)).toEqual({})
    expect(validateQuickRow('c-1', { ...OK, cod: '12abc' }, false, M).cod).toBe(M.codInvalid)
    expect(validateQuickRow('c-1', { ...OK, cod: '-1' }, false, M).cod).toBe(M.codNegative)
  })
  it('el número de orden solo se valida si el cliente lo asigna (máx. 40)', () => {
    const long = 'x'.repeat(41)
    expect(validateQuickRow('c-1', { ...OK, orderNumber: long }, true, M).orderNumber).toBe(M.numberMax)
    expect(validateQuickRow('c-1', { ...OK, orderNumber: long }, false, M).orderNumber).toBeUndefined()
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
  const opts = { orderNumberEditable: false, today: '2026-10-12' }
  it('una línea de 1 pieza, borrador y fechas de hoy', () => {
    expect(buildQuickRequest('c-1', { ...OK, cod: '50', notes: ' hola ' }, opts)).toEqual({
      clientPublicId: 'c-1',
      consigneeLocationPublicId: 'l-1',
      serviceType: 'STANDARD',
      packages: [{ packageType: 'BOX', pieces: 1 }],
      codAmount: 50,
      requestedDate: '2026-10-12T00:00:00',
      promisedDate: '2026-10-12T00:00:00',
      notes: 'hola',
      confirmNow: false,
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
})

describe('nextQuickRow', () => {
  it('conserva el servicio; el paquete vuelve al predeterminado (o se conserva si no hay) y lo demás se limpia', () => {
    const next = nextQuickRow({ ...OK, serviceType: 'EXPRESS', cod: '9', notes: 'x', orderNumber: 'A', packageType: 'ENV' }, { packageType: 'BOX' })
    expect(next).toEqual({ orderNumber: '', consigneeId: '', packageType: 'BOX', cod: '', serviceType: 'EXPRESS', notes: '' })
    // sin paquete predeterminado se conserva el de la fila anterior
    expect(nextQuickRow({ ...OK, packageType: 'ENV' }, {}).packageType).toBe('ENV')
    expect(emptyQuickRow()).toEqual({ orderNumber: '', consigneeId: '', packageType: '', cod: '', serviceType: '', notes: '' })
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
    expect(quickFieldOf('clientInvoiceNumber')).toBeNull()
  })
  it('separa campos de la fila, el resto y los datos de factura repetida', () => {
    const err = new ApiError(409, {
      title: 'Factura repetida.',
      code: 'duplicate_invoice',
      errors: { clientInvoiceNumber: ['Factura repetida.'], existingOrderNumber: ['ORD-1'], existingOrderPublicId: ['o-1'], 'packages[0].pieces': ['La cantidad de piezas debe ser al menos 1.'] },
    })
    const p = quickProblemOf(err)
    expect(p.title).toBe('Factura repetida.')
    expect(p.fields).toEqual({})
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
