import { buildReceiveBody, checkReceive, initialEntries, parseQty, totalReceived, type ReturnLine } from './returnReceiveLogic'

const line = (over: Partial<ReturnLine> = {}): ReturnLine => ({
  id: 11, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', trackingTypeCode: 'NONE', qtyRequested: 3, qtyReceived: 0, qtyDamaged: 0, pending: 3, ...over,
})

describe('cantidades', () => {
  it('vacío es 0; acepta coma; rechaza negativos, letras y más de 3 decimales', () => {
    expect(parseQty('')).toBe(0)
    expect(parseQty('2,5')).toBe(2.5)
    expect(parseQty('-1')).toBeNull()
    expect(parseQty('abc')).toBeNull()
    expect(parseQty('1.2345')).toBeNull()
  })
  it('por omisión llega todo bueno', () => {
    expect(initialEntries([line()])[11]).toEqual({ good: '3', damaged: '', note: '', discard: false, lot: '' })
  })
})

describe('revisar lo que se va a recibir', () => {
  const entries = (over = {}) => ({ 11: { good: '3', damaged: '', note: '', discard: false, lot: '', ...over } })
  it('todo bueno se puede enviar', () => {
    expect(checkReceive([line()], entries())).toBeNull()
  })
  it('sin nada anotado, con cantidad inválida o pasándose de lo pendiente se bloquea', () => {
    expect(checkReceive([line()], entries({ good: '' }))).toEqual({ kind: 'nothing' })
    expect(checkReceive([line()], entries({ good: 'x' }))).toMatchObject({ kind: 'qtyInvalid', sku: 'MED' })
    expect(checkReceive([line()], entries({ good: '2', damaged: '2' }))).toMatchObject({ kind: 'exceeds', pending: 3 })
  })
  it('lo dañado pide la descripción y el producto con lote pide el lote', () => {
    expect(checkReceive([line()], entries({ good: '2', damaged: '1' }))).toMatchObject({ kind: 'damageNote' })
    expect(checkReceive([line()], entries({ good: '2', damaged: '1', note: 'caja rota' }))).toBeNull()
    expect(checkReceive([line({ trackingTypeCode: 'LOT' })], entries())).toMatchObject({ kind: 'lot' })
    expect(checkReceive([line({ trackingTypeCode: 'LOT' })], entries({ lot: 'L1' }))).toBeNull()
  })
  it('una línea sin nada no cuenta aunque no lleve lote', () => {
    const two = [line(), line({ id: 12, sku: 'B', trackingTypeCode: 'LOT' })]
    expect(checkReceive(two, { 11: { good: '3', damaged: '', note: '', discard: false, lot: '' }, 12: { good: '', damaged: '', note: '', discard: false, lot: '' } })).toBeNull()
  })
})

describe('el cuerpo del envío', () => {
  it('solo las líneas con algo, con el daño, el desecho y el lote', () => {
    const lines = [line({ trackingTypeCode: 'LOT' }), line({ id: 12, sku: 'B' })]
    const body = buildReceiveBody(
      lines,
      { 11: { good: '2', damaged: '1', note: ' rota ', discard: true, lot: ' L1 ' }, 12: { good: '', damaged: '', note: '', discard: false, lot: '' } },
      true,
    )
    expect(body).toEqual({ lines: [{ lineId: 11, goodQty: 2, damagedQty: 1, damageNote: 'rota', damageDiscard: true, lot: { number: 'L1' } }], closeRest: true })
  })
  it('sin daño no manda descripción ni desecho, y sin cerrar lo pendiente no manda closeRest', () => {
    expect(buildReceiveBody([line()], { 11: { good: '3', damaged: '', note: 'x', discard: true, lot: '' } }, false)).toEqual({ lines: [{ lineId: 11, goodQty: 3, damagedQty: 0 }] })
  })
  it('suma lo bueno y lo dañado', () => {
    expect(totalReceived([line()], { 11: { good: '2', damaged: '1', note: '', discard: false, lot: '' } })).toEqual({ good: 2, damaged: 1 })
  })
})
