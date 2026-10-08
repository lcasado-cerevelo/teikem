import { buildDamageRequest, damageBlock, damageStep, defaultCause, EMPTY_DAMAGE, parseDamageQty, type DamageDraft } from './damageLogic'

const PRODUCT = { publicId: 'p-1', sku: 'SKU-1', name: 'Tornillo', trackingTypeCode: 'NONE' }

describe('damageLogic', () => {
  it('pide los datos en orden: origen → posición o recibo → producto → detalle', () => {
    expect(damageStep(EMPTY_DAMAGE)).toBe('origin')
    expect(damageStep({ ...EMPTY_DAMAGE, origin: 'WAREHOUSE' })).toBe('source')
    expect(damageStep({ ...EMPTY_DAMAGE, origin: 'RECEIPT' })).toBe('source')
    expect(damageStep({ ...EMPTY_DAMAGE, origin: 'WAREHOUSE', bin: { id: 3, code: 'A-01' } })).toBe('product')
    expect(damageStep({ ...EMPTY_DAMAGE, origin: 'RECEIPT', receipt: { publicId: 'r', number: 'REC-1' }, product: PRODUCT })).toBe('details')
  })

  it('la cantidad acepta coma decimal y debe ser mayor que 0', () => {
    expect(parseDamageQty('2,5')).toBe(2.5)
    expect(parseDamageQty('0')).toBeNull()
    expect(parseDamageQty('')).toBeNull()
    expect(parseDamageQty('abc')).toBeNull()
  })

  it('propone la causa según el origen', () => {
    expect(defaultCause('RECEIPT')).toBe('ARRIVED_DAMAGED')
    expect(defaultCause('WAREHOUSE')).toBe('WAREHOUSE_ACCIDENT')
  })

  it('un producto por lote pide el lote', () => {
    const d: DamageDraft = { ...EMPTY_DAMAGE, origin: 'WAREHOUSE', product: { ...PRODUCT, trackingTypeCode: 'LOT' }, qtyText: '3' }
    expect(damageBlock(d)).toBe('lot')
    expect(damageBlock({ ...d, lot: ' L-1 ' })).toBeNull()
    expect(damageBlock({ ...d, qtyText: '' })).toBe('qty')
  })

  it('el cuerpo lleva la posición en un daño del almacén y el recibo en uno de recibo', () => {
    const warehouse: DamageDraft = { ...EMPTY_DAMAGE, origin: 'WAREHOUSE', bin: { id: 7, code: 'A-01' }, product: PRODUCT, qtyText: '4', cause: 'OTHER' }
    expect(buildDamageRequest('wh', warehouse, 'QUARANTINE')).toEqual({
      origin: 'WAREHOUSE',
      warehousePublicId: 'wh',
      productPublicId: 'p-1',
      receiptPublicId: null,
      fromBinId: 7,
      quantity: 4,
      cause: 'OTHER',
      disposition: 'QUARANTINE',
      lot: undefined,
    })
    const receipt: DamageDraft = { ...EMPTY_DAMAGE, origin: 'RECEIPT', receipt: { publicId: 'r-9', number: 'REC-9' }, product: { ...PRODUCT, trackingTypeCode: 'LOT' }, qtyText: '2', lot: 'L-3' }
    expect(buildDamageRequest('wh', receipt, 'DISCARD')).toMatchObject({ origin: 'RECEIPT', receiptPublicId: 'r-9', fromBinId: null, cause: 'ARRIVED_DAMAGED', disposition: 'DISCARD', lot: { number: 'L-3' } })
  })
})
