import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { applySalesReturns } from '../../kernel/sync/download'
import { listOutbox } from '../../kernel/sync/outbox'
import { findSalesReturnByNumber, getSalesReturnLines, listSalesReturns, queueReturnReceive, reapplyPendingReturnReceipts } from './localReturns'
import { initialEntries } from './returnReceiveLogic'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

const WH = 'wh-1'
const RET = {
  id: 9, publicId: 'dv-9', number: 'DV-00009', warehousePublicId: WH, clientName: 'Cliente 1', orderNumber: 'OV-00005', statusCode: 'AUTHORIZED', note: 'Cajas rotas', requestedAtUtc: '2026-10-16T10:00:00Z', isActive: true,
  lines: [{ id: 90, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', trackingTypeCode: 'NONE', qtyRequested: 3, qtyReceived: 0, qtyDamaged: 0 }],
}

describe('devoluciones por recibir en el aparato', () => {
  it('se guardan con sus líneas y se buscan por número (completo o solo el final)', () => {
    applySalesReturns(getDb(), WH, [RET])
    expect(listSalesReturns(WH).map((r) => [r.number, r.pending])).toEqual([['DV-00009', 3]])
    expect(findSalesReturnByNumber(WH, 'dv-00009')?.id).toBe(9)
    expect(findSalesReturnByNumber(WH, '00009')?.id).toBe(9)
    expect(findSalesReturnByNumber(WH, 'DV-1')).toBeNull()
    expect(getSalesReturnLines(9).map((l) => [l.sku, l.pending])).toEqual([['MED', 3]])
  })

  it('una devolución inactiva que llega se borra de la copia y una sin pendiente no se lista', () => {
    applySalesReturns(getDb(), WH, [RET])
    applySalesReturns(getDb(), WH, [{ ...RET, isActive: false }])
    expect(listSalesReturns(WH)).toEqual([])
    expect(getDb().getAllSync('SELECT * FROM sales_return_line')).toEqual([])
  })

  it('recibir en parte encola con la ruta de la devolución y baja lo pendiente al instante', () => {
    applySalesReturns(getDb(), WH, [RET])
    const ret = findSalesReturnByNumber(WH, 'DV-00009')!
    const lines = getSalesReturnLines(9)
    const id = queueReturnReceive(ret, lines, { 90: { good: '1', damaged: '1', note: 'rota', discard: false, lot: '' } }, false)
    const row = listOutbox().find((r) => r.id === id)!
    expect(row.kind).toBe('salesReturnReceive')
    expect(row.path).toBe('/api/v1/sales-returns/dv-9/receive')
    expect(JSON.parse(row.body)).toEqual({ lines: [{ lineId: 90, goodQty: 1, damagedQty: 1, damageNote: 'rota' }] })
    expect(listSalesReturns(WH).map((r) => r.pending)).toEqual([1])
  })

  it('recibir todo (o cerrar lo pendiente) saca la devolución de la lista', () => {
    applySalesReturns(getDb(), WH, [RET])
    const ret = findSalesReturnByNumber(WH, 'DV-00009')!
    queueReturnReceive(ret, getSalesReturnLines(9), initialEntries(getSalesReturnLines(9)), false)
    expect(listSalesReturns(WH)).toEqual([])
    expect(getDb().getAllSync('SELECT * FROM sales_return')).toEqual([])

    applySalesReturns(getDb(), WH, [RET])
    queueReturnReceive(findSalesReturnByNumber(WH, 'DV-00009')!, getSalesReturnLines(9), { 90: { good: '1', damaged: '', note: '', discard: false, lot: '' } }, true)
    expect(listSalesReturns(WH)).toEqual([])
  })

  it('al bajar de nuevo, las recepciones que siguen en la cola se vuelven a reflejar', () => {
    applySalesReturns(getDb(), WH, [RET])
    queueReturnReceive(findSalesReturnByNumber(WH, 'DV-00009')!, getSalesReturnLines(9), { 90: { good: '2', damaged: '', note: '', discard: false, lot: '' } }, false)
    // llega la copia del servidor, que todavía no conoce lo recibido
    getDb().withTransactionSync(() => {
      getDb().runSync('DELETE FROM sales_return_line')
      getDb().runSync('DELETE FROM sales_return')
      applySalesReturns(getDb(), WH, [RET])
      reapplyPendingReturnReceipts(getDb())
    })
    expect(listSalesReturns(WH).map((r) => r.pending)).toEqual([1])
  })
})
