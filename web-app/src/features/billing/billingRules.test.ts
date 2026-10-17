// Lote 73 — reglas puras de la pantalla de Facturación.
import { describe, expect, it } from 'vitest'
import {
  adjustError, invoiceCanPay, invoiceCanVoid, monthStartIso, parseAmount, paymentCanVoid, paymentFormError, runFormError, runIsOpen, statementFrom, todayIso, toPaymentRequest, toRunRequest, voidPaymentError,
  availableFields, moveColumn, runCanExport, templateFormError, toTemplateRequest,
  type RunForm,
} from './billingRules'

const form = (patch: Partial<RunForm> = {}): RunForm => ({ clientPublicId: null, periodStart: '2026-10-01', periodEnd: '2026-10-15', onlyInPeriod: false, includeProducts: true, includeFreight: true, note: '', ...patch })

describe('formulario de la corrida', () => {
  it('pide las dos fechas, en orden, y una nota corta', () => {
    expect(runFormError(form())).toBeNull()
    expect(runFormError(form({ periodStart: '' }))).toBe('period')
    expect(runFormError(form({ periodEnd: '' }))).toBe('period')
    expect(runFormError(form({ periodStart: '2026-10-20' }))).toBe('order')
    expect(runFormError(form({ includeProducts: false, includeFreight: false }))).toBe('include')
    expect(runFormError(form({ includeProducts: false }))).toBeNull()
    expect(runFormError(form({ note: 'x'.repeat(501) }))).toBe('note')
  })

  it('el cuerpo omite lo vacío y solo manda onlyInPeriod si se marcó', () => {
    expect(toRunRequest(form())).toEqual({ clientPublicId: undefined, periodStart: '2026-10-01', periodEnd: '2026-10-15', onlyInPeriod: undefined, includeProducts: undefined, includeFreight: undefined, note: undefined })
    expect(toRunRequest(form({ includeFreight: false }))).toMatchObject({ includeProducts: undefined, includeFreight: false })
    expect(toRunRequest(form({ clientPublicId: 'c1', onlyInPeriod: true, note: '  Octubre  ' }))).toMatchObject({ clientPublicId: 'c1', onlyInPeriod: true, note: 'Octubre' })
  })

  it('fechas por omisión: el primer día del mes y hoy', () => {
    const d = new Date(2026, 9, 16)
    expect(todayIso(d)).toBe('2026-10-16')
    expect(monthStartIso(d)).toBe('2026-10-01')
  })
})

describe('ajuste de una línea', () => {
  it('monto numérico de hasta 4 decimales y motivo obligatorio', () => {
    expect(parseAmount('8,5')).toBe(8.5)
    expect(parseAmount('')).toBeNull()
    expect(parseAmount('-1')).toBeNull()
    expect(adjustError('8', 'Descuento')).toBeNull()
    expect(adjustError('x', 'Descuento')).toBe('amount')
    expect(adjustError('8.12345', 'Descuento')).toBe('decimals')
    expect(adjustError('8', '  ')).toBe('note')
    expect(adjustError('8', 'x'.repeat(251))).toBe('noteLong')
  })
})

describe('estatus', () => {
  it('solo una corrida generada o revisada está abierta; solo emitida o vencida se anula', () => {
    expect(['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'].map(runIsOpen)).toEqual([true, true, false, false, false])
    expect(['DRAFT', 'ISSUED', 'PAID', 'OVERDUE', 'VOID'].map(invoiceCanVoid)).toEqual([false, true, false, true, false])
  })
})

describe('pagos (Lote 76)', () => {
  const form = (patch = {}) => ({ amount: '30', method: 'CHECK', paidDate: '2026-10-16', reference: '', ...patch })
  const check = (patch = {}, balance = 100) => paymentFormError(form(patch), balance, '2026-10-16', '2026-10-01')

  it('valida lo mismo que el servidor antes de enviar', () => {
    expect(check()).toBeNull()
    expect(check({ amount: '' })).toBe('amount')
    expect(check({ amount: '0' })).toBe('amount')
    expect(check({ amount: '10.005' })).toBe('decimals')
    expect(check({ amount: '150' })).toBe('exceeds')
    expect(check({ amount: '100' })).toBeNull()   // justo el saldo
    expect(check({ method: '' })).toBe('method')
    expect(check({ paidDate: '2026-10-17' })).toBe('future')
    expect(check({ paidDate: '2026-09-30' })).toBe('beforeIssue')
    expect(check({ reference: 'x'.repeat(81) })).toBe('reference')
  })

  it('el cuerpo omite la fecha si es hoy y la referencia si está vacía', () => {
    expect(toPaymentRequest(form({ amount: '30,5' }), '2026-10-16')).toEqual({ amount: 30.5, method: 'CHECK', paidDate: undefined, reference: undefined })
    expect(toPaymentRequest(form({ paidDate: '2026-10-05', reference: ' CH-1 ' }), '2026-10-16')).toMatchObject({ paidDate: '2026-10-05', reference: 'CH-1' })
  })

  it('solo una factura emitida o vencida se paga y un pago se anula mientras la factura no esté pagada', () => {
    expect(['DRAFT', 'ISSUED', 'PAID', 'OVERDUE', 'VOID'].map(invoiceCanPay)).toEqual([false, true, false, true, false])
    expect(paymentCanVoid('ISSUED', null)).toBe(true)
    expect(paymentCanVoid('OVERDUE', null)).toBe(true)
    expect(paymentCanVoid('PAID', null)).toBe(false)
    expect(paymentCanVoid('ISSUED', '2026-10-16T10:00:00Z')).toBe(false)   // ya anulado
  })

  it('el motivo de anular es obligatorio y de hasta 250 caracteres', () => {
    expect(voidPaymentError(' ')).toBe('required')
    expect(voidPaymentError('Cheque devuelto')).toBeNull()
    expect(voidPaymentError('x'.repeat(251))).toBe('long')
  })

  it('el estado de cuenta empieza 90 días antes', () => {
    expect(statementFrom('2026-10-16')).toBe('2026-07-18')
    expect(statementFrom('2026-03-01')).toBe('2025-12-01')
  })
})

describe('plantillas de exportación (Lote 78)', () => {
  const cols = [{ field: 'A', header: '' }, { field: 'B', header: ' Total ' }, { field: 'C', header: '' }]
  const base = { name: 'Contable', format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true, columns: cols }

  it('valida lo mismo que el servidor antes de enviar', () => {
    expect(templateFormError(base)).toBeNull()
    expect(templateFormError({ ...base, name: '  ' })).toBe('name')
    expect(templateFormError({ ...base, name: 'x'.repeat(81) })).toBe('nameLong')
    expect(templateFormError({ ...base, columns: [] })).toBe('columns')
    expect(templateFormError({ ...base, columns: [{ field: 'A', header: 'h'.repeat(61) }] })).toBe('header')
  })

  it('el cuerpo recorta el nombre y los encabezados y omite los vacíos', () => {
    expect(toTemplateRequest({ ...base, name: ' Contable ' }).columns).toEqual([{ field: 'A', header: undefined }, { field: 'B', header: 'Total' }, { field: 'C', header: undefined }])
    expect(toTemplateRequest(base)).toMatchObject({ name: 'Contable', format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true })
  })

  it('mueve una columna y en los extremos no hace nada', () => {
    expect(moveColumn(['a', 'b', 'c'], 1, -1)).toEqual(['b', 'a', 'c'])
    expect(moveColumn(['a', 'b', 'c'], 1, 1)).toEqual(['a', 'c', 'b'])
    expect(moveColumn(['a', 'b', 'c'], 0, -1)).toEqual(['a', 'b', 'c'])
    expect(moveColumn(['a', 'b', 'c'], 2, 1)).toEqual(['a', 'b', 'c'])
  })

  it('ofrece los campos del catálogo que todavía no están en la plantilla', () => {
    const catalog = [{ code: 'A' }, { code: 'B' }, { code: 'D' }]
    expect(availableFields(catalog, cols).map((f) => f.code)).toEqual(['D'])
    expect(availableFields(catalog, []).map((f) => f.code)).toEqual(['A', 'B', 'D'])
  })

  it('solo una corrida aprobada o exportada se exporta', () => {
    expect(['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'].map(runCanExport)).toEqual([false, false, true, true, false])
  })
})
