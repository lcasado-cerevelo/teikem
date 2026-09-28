import {
  addSerial,
  buildLine,
  buildReceiptBody,
  canAddLine,
  newLineDraft,
  removeSerial,
  requiresLot,
  requiresSerials,
} from './receiveLogic'

const NONE_PRODUCT = { publicId: 'p1', sku: 'SKU-1', name: 'Producto 1', trackingTypeCode: 'NONE' as const }
const LOT_PRODUCT = { publicId: 'p2', sku: 'SKU-2', name: 'Producto 2', trackingTypeCode: 'LOT' as const }
const SERIAL_PRODUCT = { publicId: 'p3', sku: 'SKU-3', name: 'Producto 3', trackingTypeCode: 'SERIAL' as const }

describe('newLineDraft', () => {
  it('arranca con cantidad 1 y sin lote ni series', () => {
    const draft = newLineDraft(NONE_PRODUCT)
    expect(draft).toMatchObject({ qtyText: '1', lot: '', expiry: '', serials: [] })
  })
})

describe('requiresLot / requiresSerials', () => {
  it('solo LOT pide lote y solo SERIAL pide series', () => {
    expect(requiresLot(newLineDraft(LOT_PRODUCT))).toBe(true)
    expect(requiresSerials(newLineDraft(LOT_PRODUCT))).toBe(false)
    expect(requiresLot(newLineDraft(SERIAL_PRODUCT))).toBe(false)
    expect(requiresSerials(newLineDraft(SERIAL_PRODUCT))).toBe(true)
    expect(requiresLot(newLineDraft(NONE_PRODUCT))).toBe(false)
  })
})

describe('canAddLine', () => {
  it('NONE: necesita una cantidad mayor que 0', () => {
    expect(canAddLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '0' })).toBe(false)
    expect(canAddLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '' })).toBe(false)
    expect(canAddLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '3' })).toBe(true)
    expect(canAddLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '2,5' })).toBe(true)
  })

  it('LOT: necesita cantidad y el número de lote', () => {
    const draft = newLineDraft(LOT_PRODUCT)
    expect(canAddLine(draft)).toBe(false)
    expect(canAddLine({ ...draft, lot: 'L-001' })).toBe(true)
    expect(canAddLine({ ...draft, lot: '   ' })).toBe(false)
  })

  it('SERIAL: necesita al menos un número de serie (la cantidad no importa)', () => {
    const draft = newLineDraft(SERIAL_PRODUCT)
    expect(canAddLine(draft)).toBe(false)
    expect(canAddLine(addSerial(draft, 'SN-1'))).toBe(true)
  })
})

describe('addSerial / removeSerial', () => {
  it('no agrega vacíos ni repetidos; quita por valor', () => {
    let draft = newLineDraft(SERIAL_PRODUCT)
    draft = addSerial(draft, ' SN-1 ')
    draft = addSerial(draft, 'SN-1')
    draft = addSerial(draft, '   ')
    draft = addSerial(draft, 'SN-2')
    expect(draft.serials).toEqual(['SN-1', 'SN-2'])
    draft = removeSerial(draft, 'SN-1')
    expect(draft.serials).toEqual(['SN-2'])
  })
})

describe('buildLine', () => {
  it('NONE: cantidad numérica, sin lote ni series', () => {
    const line = buildLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '4' })
    expect(line).toEqual({
      productPublicId: 'p1',
      sku: 'SKU-1',
      productName: 'Producto 1',
      trackingTypeCode: 'NONE',
      receivedQty: 4,
      lotNumber: null,
      expiryDate: null,
      serialNumbers: null,
    })
  })

  it('LOT: incluye el lote y el vencimiento si se capturó', () => {
    const line = buildLine({ ...newLineDraft(LOT_PRODUCT), qtyText: '10', lot: ' L-9 ', expiry: '2027-01-01' })
    expect(line.receivedQty).toBe(10)
    expect(line.lotNumber).toBe('L-9')
    expect(line.expiryDate).toBe('2027-01-01')
  })

  it('SERIAL: la cantidad es el número de series capturadas', () => {
    let draft = newLineDraft(SERIAL_PRODUCT)
    draft = addSerial(draft, 'A')
    draft = addSerial(draft, 'B')
    draft = addSerial(draft, 'C')
    const line = buildLine(draft)
    expect(line.receivedQty).toBe(3)
    expect(line.serialNumbers).toEqual(['A', 'B', 'C'])
    expect(line.lotNumber).toBeNull()
  })
})

describe('buildReceiptBody', () => {
  const LINE = buildLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '2' })

  it('recibo ciego: sin orden ni aviso', () => {
    const body = buildReceiptBody('wh-1', null, [LINE])
    expect(body).toMatchObject({ warehousePublicId: 'wh-1', purchaseOrderPublicId: null, asnId: null, confirm: true })
    expect(body.lines).toEqual([{ productPublicId: 'p1', receivedQty: 2, lot: null, serialNumbers: null }])
  })

  it('contra una orden de compra: manda purchaseOrderPublicId', () => {
    const body = buildReceiptBody('wh-1', { purchaseOrderPublicId: 'po-1' }, [LINE])
    expect(body.purchaseOrderPublicId).toBe('po-1')
    expect(body.asnId).toBeNull()
  })

  it('contra un aviso: manda asnId', () => {
    const body = buildReceiptBody('wh-1', { asnId: 42 }, [LINE])
    expect(body.asnId).toBe(42)
    expect(body.purchaseOrderPublicId).toBeNull()
  })

  it('una línea con lote arma el LotInput anidado', () => {
    const lotLine = buildLine({ ...newLineDraft(LOT_PRODUCT), qtyText: '5', lot: 'L-1' })
    const body = buildReceiptBody('wh-1', null, [lotLine])
    expect(body.lines[0].lot).toEqual({ number: 'L-1', expiryDate: null })
  })
})
