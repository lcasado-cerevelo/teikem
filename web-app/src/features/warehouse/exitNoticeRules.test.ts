// Lote 144 — reglas puras del aviso de salida en pantalla.
import { describe, expect, it } from 'vitest'
import type { ExitNoticeLineDto } from './exitNoticeApi'
import { canDispatch, canHandlePending, dispatchLines, dispatchQuantityIssue, canModifyLine, exitQuantityIssue, pendingLines, toShip } from './exitNoticeRules'

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

describe('despachar', () => {
  it('solo con aviso abierto y algo reservado', () => {
    expect(canDispatch({ statusCode: 'REQUESTED', lines: [line({ qtyReserved: 1 })] })).toBe(true)
    expect(canDispatch({ statusCode: 'REQUESTED', lines: [line({ qtyReserved: 0, qtyBackordered: 3 })] })).toBe(false)
    expect(canDispatch({ statusCode: 'SHIPPED', lines: [line({ qtyReserved: 1 })] })).toBe(false)
  })
  it('cantidad: 0 o vacío no despacha; no puede pasar de lo reservado', () => {
    expect(dispatchQuantityIssue(null, 4)).toBeNull()
    expect(dispatchQuantityIssue(0, 4)).toBeNull()
    expect(dispatchQuantityIssue(5, 4)).toBe('warehouse.exitNotices.errors.overReserved')
    expect(dispatchQuantityIssue(-1, 4)).toBe('warehouse.exitNotices.errors.quantityRequired')
    expect(dispatchQuantityIssue(1.0005, 4)).toBe('warehouse.exitNotices.errors.quantityDecimals')
    expect(dispatchQuantityIssue(4, 4)).toBeNull()
  })
  it('el cuerpo lleva solo las líneas con cantidad', () => {
    expect(dispatchLines({ 1: 2, 2: 0, 3: null })).toEqual([{ lineId: 1, quantity: 2 }])
  })
})
