import type { BalanceRow } from '../lookup/lookupLogic'
import { buildTransferRequest, isBlockedZone, lotChoices, movableOf, productsToMove, qtyIssue, sameBin } from './transferLogic'

const row = (id: number, sku: string, qtyAvailable: number, extra: Partial<BalanceRow> = {}): BalanceRow => ({
  id,
  binId: 5,
  lotId: null,
  zoneTypeCode: 'PICKING',
  binCode: 'A-01',
  productPublicId: `p-${sku}`,
  sku,
  productName: `Producto ${sku}`,
  lotNumber: null,
  qtyOnHand: qtyAvailable + 2,
  qtyAvailable,
  ...extra,
})

describe('Transferir — lógica', () => {
  it('solo ofrece lo que tiene disponible (lo reservado no se mueve), por SKU, y junta los lotes de un producto', () => {
    const list = productsToMove([row(1, 'B-2', 3), row(2, 'A-1', 0), row(3, 'A-10', 4, { lotId: 9, lotNumber: 'L1' }), row(4, 'A-10', 6, { lotId: 10, lotNumber: 'L2' })])
    expect(list.map((p) => [p.sku, p.available])).toEqual([['A-10', 10], ['B-2', 3]])
    const a10 = list[0]
    expect(lotChoices(a10).map((r) => r.lotNumber)).toEqual(['L1', 'L2'])
    expect(movableOf(a10, 9)).toBe(4)
    expect(movableOf(a10, 10)).toBe(6)
    expect(movableOf(list[1], null)).toBe(3)
  })

  it('valida la cantidad contra lo movible', () => {
    expect(qtyIssue('', 5)).toBe('invalid')
    expect(qtyIssue('0', 5)).toBe('invalid')
    expect(qtyIssue('abc', 5)).toBe('invalid')
    expect(qtyIssue('6', 5)).toBe('tooMany')
    expect(qtyIssue('5', 5)).toBeNull()
    expect(qtyIssue('2,5', 5)).toBeNull()
  })

  it('cuarentena, en renta y cross-dock no se transfieren desde el aparato', () => {
    expect(['QUARANTINE', 'rental', 'CROSSDOCK'].every(isBlockedZone)).toBe(true)
    expect(isBlockedZone('PICKING')).toBe(false)
    expect(isBlockedZone(null)).toBe(false)
  })

  it('arma el cuerpo y detecta la misma posición', () => {
    expect(
      buildTransferRequest({ warehousePublicId: 'wh-1', productPublicId: 'p1', from: { id: 5, code: 'A' }, to: { id: 6, code: 'B' }, quantity: 3, lotId: 9 }),
    ).toEqual({ productPublicId: 'p1', fromBinId: 5, toBinId: 6, quantity: 3, fromWarehousePublicId: 'wh-1', lotId: 9 })
    expect(sameBin({ id: 5, code: 'A' }, { id: 5, code: 'A' })).toBe(true)
    expect(sameBin({ id: 5, code: 'A' }, null)).toBe(false)
  })
})
