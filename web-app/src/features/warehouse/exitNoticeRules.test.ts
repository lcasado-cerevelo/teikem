// Lote 144 — reglas puras del aviso de salida en pantalla.
import { describe, expect, it } from 'vitest'
import type { ExitNoticeLineDto } from './exitNoticeApi'
import { canHandlePending, canModifyLine, exitQuantityIssue, pendingLines, toShip } from './exitNoticeRules'

const line = (o: Partial<ExitNoticeLineDto>) => ({ id: 1, ...o }) as ExitNoticeLineDto

describe('exitQuantityIssue', () => {
  it.each([null, undefined, 0, -1, Number.NaN])('rechaza %s', (q) => expect(exitQuantityIssue(q)).toBe('warehouse.exitNotices.errors.quantityRequired'))
  it('rechaza muy grande y más de 3 decimales', () => {
    expect(exitQuantityIssue(100_000_000)).toBe('warehouse.exitNotices.errors.quantityTooLarge')
    expect(exitQuantityIssue(1.0005)).toBe('warehouse.exitNotices.errors.quantityDecimals')
  })
  it('acepta 3 decimales', () => expect(exitQuantityIssue(1.125)).toBeNull())
})

describe('líneas', () => {
  it('pendiente solo en líneas abiertas con cantidad en espera', () => {
    const ls = [line({ id: 1, qtyBackordered: 2 }), line({ id: 2, qtyBackordered: 2, isClosed: true }), line({ id: 3, qtyBackordered: 0 })]
    expect(pendingLines(ls).map((l) => l.id)).toEqual([1])
    expect(pendingLines(null)).toEqual([])
  })
  it('cambiar cantidad: aviso modificable, línea abierta y sin despacho', () => {
    expect(canModifyLine({ canModify: true }, line({}))).toBe(true)
    expect(canModifyLine({ canModify: false }, line({}))).toBe(false)
    expect(canModifyLine({ canModify: true }, line({ qtyShipped: 1 }))).toBe(false)
    expect(canModifyLine({ canModify: true }, line({ isClosed: true }))).toBe(false)
  })
  it('atender pendiente: solo solicitado o parcial', () => {
    expect(canHandlePending({ statusCode: 'PARTIAL' }, line({ qtyBackordered: 1 }))).toBe(true)
    expect(canHandlePending({ statusCode: 'SHIPPED' }, line({ qtyBackordered: 1 }))).toBe(false)
    expect(canHandlePending({ statusCode: 'REQUESTED' }, line({ qtyBackordered: 0 }))).toBe(false)
  })
  it('por despachar: reservado + pendiente, sin pendiente si está cerrada', () => {
    expect(toShip(line({ qtyReserved: 3, qtyBackordered: 2 }))).toBe(5)
    expect(toShip(line({ qtyReserved: 3, qtyBackordered: 2, isClosed: true }))).toBe(3)
  })
})
