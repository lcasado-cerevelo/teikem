// Lote 246 — reglas puras de QuickBooks en Órdenes de compra.
import { describe, expect, it } from 'vitest'
import { canPass, filterPending, hasNewData, newDataOf, originOf, qbStateOf, selectionBody, touchedQuickBooks, batchesOf, type PurchaseQbOrder } from './purchaseQuickBooksRules'

const ORDER = (n: string, extra: Partial<PurchaseQbOrder> = {}): PurchaseQbOrder => ({ publicId: `id-${n}`, number: n, supplierId: 5, supplierName: 'Acme Medical', quickBooksVendor: 'ACME MEDICAL INC', ...extra })

describe('purchaseQuickBooksRules', () => {
  it('un estado o un origen desconocido cae en el valor seguro', () => {
    expect(qbStateOf('EXPORTED')).toBe('EXPORTED')
    expect(qbStateOf('???')).toBe('NOT_EXPORTABLE')
    expect(qbStateOf(null)).toBe('NOT_EXPORTABLE')
    expect(originOf('EXCEL')).toBe('EXCEL')
    expect(originOf(undefined)).toBe('TEIKEM')
  })

  it('solo se ofrecen las por pasar y las exportadas por confirmar; avisa al editar las que ya salieron', () => {
    expect(canPass('PENDING')).toBe(true)
    expect(canPass('EXPORTED')).toBe(true)
    expect(canPass('IN_QUICKBOOKS')).toBe(false)
    expect(canPass('FROM_QUICKBOOKS')).toBe(false)
    expect(touchedQuickBooks('EXPORTED')).toBe(true)
    expect(touchedQuickBooks('IN_QUICKBOOKS')).toBe(true)
    expect(touchedQuickBooks('PENDING')).toBe(false)
  })

  it('el buscador mira número, proveedor y nombre en QuickBooks', () => {
    const rows = [ORDER('PO-1'), ORDER('PO-2', { supplierName: 'Zeta', quickBooksVendor: 'ZETA CORP' })]
    expect(filterPending(rows, '')).toHaveLength(2)
    expect(filterPending(rows, 'zeta corp').map((r) => r.number)).toEqual(['PO-2'])
    expect(filterPending(rows, 'po-1').map((r) => r.number)).toEqual(['PO-1'])
  })

  it('«ver datos» junta el proveedor nuevo y los productos nuevos por SKU', () => {
    const pending = { newSuppliers: [{ supplierId: 5, name: 'Acme Medical' }], newProducts: [{ publicId: 'p1', sku: 'ab-1' }, { publicId: 'p2', sku: 'zz-9' }] }
    const o = ORDER('PO-1', { newSupplier: true, newProductSkus: ['AB-1'] })
    const d = newDataOf(o, pending)
    expect(d.supplier?.name).toBe('Acme Medical')
    expect(d.products.map((p) => p.publicId)).toEqual(['p1'])
    expect(hasNewData(o)).toBe(true)
    expect(hasNewData(ORDER('PO-2'))).toBe(false)
  })

  it('elegir todas las por pasar manda all; si no, la lista de ids sin repetir', () => {
    expect(selectionBody(['a', 'b'], 2)).toEqual({ all: true })
    expect(selectionBody(['a', 'a', 'b'], 5)).toEqual({ purchaseOrderIds: ['a', 'b'] })
    expect(selectionBody(['a'], -1)).toEqual({ purchaseOrderIds: ['a'] })
    expect(selectionBody([], 0)).toEqual({ purchaseOrderIds: [] })
  })

  it('marcar o confirmar se parte en tandas de 1000', () => {
    const ids = Array.from({ length: 2500 }, (_, i) => `x${i}`)
    expect(batchesOf(ids).map((b) => b.length)).toEqual([1000, 1000, 500])
  })
})
