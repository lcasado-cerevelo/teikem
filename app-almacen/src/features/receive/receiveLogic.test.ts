import {
  addSerial,
  attachDamage,
  buildLine,
  buildReceiptBody,
  canAddLine,
  DAMAGE_CAUSE_OTHER,
  damageBlock,
  draftDamagedQty,
  draftExpiry,
  draftQuantity,
  type DocLine,
  type DraftLine,
  findTargetConflict,
  linesMissingTarget,
  newLineDraft,
  parseReceivingMode,
  removeSerial,
  requiresLot,
  requiresSerials,
  validateDamageBin,
  validateTargetBin,
} from './receiveLogic'
import { PR_FORMAT } from '../../kernel/format/settings'

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
      targetBinCode: null,
      damagedQty: 0,
      damageCause: null,
      damageNote: null,
      damageBinCode: null,
      damageDiscard: false,
    })
  })

  it('LOT: incluye el lote y el vencimiento si se capturó', () => {
    const line = buildLine({ ...newLineDraft(LOT_PRODUCT), qtyText: '10', lot: ' L-9 ', expiry: '2027-01-01' })
    expect(line.receivedQty).toBe(10)
    expect(line.lotNumber).toBe('L-9')
    expect(line.expiryDate).toBe('2027-01-01')
  })

  it('LOT: el vencimiento se escribe en el orden de fecha de la compañía (Puerto Rico: MM/DD/AAAA) y sale en ISO', () => {
    const line = buildLine({ ...newLineDraft(LOT_PRODUCT), qtyText: '10', lot: 'L-9', expiry: '01/31/2027' })
    expect(line.expiryDate).toBe('2027-01-31')
    expect(buildLine({ ...newLineDraft(LOT_PRODUCT), qtyText: '10', lot: 'L-9', expiry: '' }).expiryDate).toBeNull()
  })

  it('LOT: un vencimiento que no es una fecha real no deja agregar la línea (el servidor rechazaría el recibo)', () => {
    const draft = { ...newLineDraft(LOT_PRODUCT), qtyText: '10', lot: 'L-9', expiry: '31/01/2027' }
    expect(draftExpiry(draft, PR_FORMAT)).toBeNull()
    expect(canAddLine(draft)).toBe(false)
    expect(draftExpiry({ expiry: '31/01/2027' }, { ...PR_FORMAT, dateOrder: 'DMY' })).toBe('2027-01-31')
    expect(canAddLine({ ...draft, expiry: '' })).toBe(true)
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
    expect(body).toMatchObject({ warehousePublicId: 'wh-1', purchaseOrderPublicId: null, asnId: null, confirm: true, receivingMode: null })
    expect(body.lines).toEqual([
      {
        productPublicId: 'p1',
        receivedQty: 2,
        lot: null,
        serialNumbers: null,
        targetBinCode: null,
        damagedQty: null,
        damageCause: null,
        damageNote: null,
        damageBinCode: null,
        damageDiscard: null,
      },
    ])
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

// ------------------------------------------------------------------ Lote 16: recibo directo a posición

describe('parseReceivingMode', () => {
  it('solo reconoce PUTAWAY y DIRECT (sin distinguir mayúsculas); lo demás es sin modo', () => {
    expect(parseReceivingMode('DIRECT')).toBe('DIRECT')
    expect(parseReceivingMode(' putaway ')).toBe('PUTAWAY')
    expect(parseReceivingMode('HALF')).toBeNull()
    expect(parseReceivingMode(null)).toBeNull()
    expect(parseReceivingMode(undefined)).toBeNull()
  })
})

describe('canAddLine en recibo directo', () => {
  it('sin posición destino no se puede agregar; con ella sí; con acomodo no la pide', () => {
    const draft = { ...newLineDraft(NONE_PRODUCT), qtyText: '3' }
    expect(canAddLine(draft, true)).toBe(false)
    expect(canAddLine({ ...draft, targetBinCode: 'RSV-A-01' }, true)).toBe(true)
    expect(canAddLine(draft, false)).toBe(true)
    expect(canAddLine(draft)).toBe(true)
  })

  it('la posición destino no suple una cantidad inválida ni el lote que falta', () => {
    expect(canAddLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '0', targetBinCode: 'R-01' }, true)).toBe(false)
    expect(canAddLine({ ...newLineDraft(LOT_PRODUCT), targetBinCode: 'R-01' }, true)).toBe(false)
  })
})

describe('draftQuantity', () => {
  it('series = cuántas; si no, la cantidad escrita (0 si no es válida)', () => {
    expect(draftQuantity({ ...newLineDraft(NONE_PRODUCT), qtyText: '2,5' })).toBe(2.5)
    expect(draftQuantity({ ...newLineDraft(NONE_PRODUCT), qtyText: 'x' })).toBe(0)
    expect(draftQuantity(addSerial(addSerial(newLineDraft(SERIAL_PRODUCT), 'A'), 'B'))).toBe(2)
  })
})

describe('buildLine con posición destino', () => {
  it('copia la posición destino (recortada) en la línea, también con series', () => {
    expect(buildLine({ ...newLineDraft(NONE_PRODUCT), targetBinCode: ' R-01 ' }).targetBinCode).toBe('R-01')
    expect(buildLine({ ...addSerial(newLineDraft(SERIAL_PRODUCT), 'A'), targetBinCode: 'Q-01' }).targetBinCode).toBe('Q-01')
  })
})

describe('validateTargetBin', () => {
  const BINS = [
    { code: 'RSV-A-01', zoneTypeCode: 'RESERVE', isActive: true },
    { code: 'QUA-01', zoneTypeCode: 'QUARANTINE', isActive: true },
    { code: 'STG-01', zoneTypeCode: 'STAGING', isActive: true },
    { code: 'XD-01', zoneTypeCode: 'CROSSDOCK', isActive: true },
    { code: 'OLD-01', zoneTypeCode: 'RESERVE', isActive: false },
    { code: 'SIN-ZONA', zoneTypeCode: null, isActive: true },
  ]

  it('acepta una posición de guardado activa y devuelve su código como está en el catálogo', () => {
    expect(validateTargetBin('rsv-a-01 ', BINS)).toEqual({ ok: true, code: 'RSV-A-01' })
    expect(validateTargetBin('SIN-ZONA', BINS)).toEqual({ ok: true, code: 'SIN-ZONA' })
  })

  it('la cuarentena sí puede ser destino (D5-A)', () => {
    expect(validateTargetBin('QUA-01', BINS)).toEqual({ ok: true, code: 'QUA-01' })
  })

  it('rechaza recepción (STAGING) y cruce de muelle (CROSSDOCK)', () => {
    expect(validateTargetBin('STG-01', BINS)).toEqual({ ok: false, reason: 'notStorage' })
    expect(validateTargetBin('XD-01', BINS)).toEqual({ ok: false, reason: 'notStorage' })
  })

  it('una posición desactivada no sirve; una que no está, tampoco', () => {
    expect(validateTargetBin('OLD-01', BINS)).toEqual({ ok: false, reason: 'inactive' })
    expect(validateTargetBin('ZZ', BINS)).toEqual({ ok: false, reason: 'notFound' })
    expect(validateTargetBin('   ', BINS)).toEqual({ ok: false, reason: 'notFound' })
    expect(validateTargetBin('RSV-A-01', [])).toEqual({ ok: false, reason: 'notFound' })
  })
})

describe('linesMissingTarget', () => {
  it('cuenta las líneas sin posición destino', () => {
    const a = buildLine({ ...newLineDraft(NONE_PRODUCT), targetBinCode: 'R-01' })
    const b = buildLine(newLineDraft(NONE_PRODUCT))
    expect(linesMissingTarget([a, b, b])).toBe(2)
    expect(linesMissingTarget([a])).toBe(0)
  })
})

describe('findTargetConflict (H11)', () => {
  const line = (productPublicId: string, target: string | null, lot: string | null = null): DraftLine => ({
    productPublicId,
    sku: productPublicId.toUpperCase(),
    productName: productPublicId,
    trackingTypeCode: lot ? 'LOT' : 'NONE',
    receivedQty: 1,
    lotNumber: lot,
    expiryDate: null,
    serialNumbers: null,
    targetBinCode: target,
    damagedQty: 0,
    damageCause: null,
    damageNote: null,
    damageBinCode: null,
    damageDiscard: false,
  })

  it('el mismo producto dos veces con destinos distintos en un documento de una sola línea choca en la segunda', () => {
    const doc: DocLine[] = [{ productPublicId: 'p1', lotNumber: null }]
    expect(findTargetConflict(doc, [line('p1', 'R-01'), line('p1', 'R-02')])).toEqual({ index: 1, sku: 'P1', bin: 'R-01' })
  })

  it('con el mismo destino no choca (el servidor las suma); sin distinguir mayúsculas', () => {
    const doc: DocLine[] = [{ productPublicId: 'p1', lotNumber: null }]
    expect(findTargetConflict(doc, [line('p1', 'R-01'), line('p1', 'r-01')])).toBeNull()
  })

  it('si el documento trae dos líneas del producto, cada una puede ir a su posición', () => {
    const doc: DocLine[] = [
      { productPublicId: 'p1', lotNumber: null },
      { productPublicId: 'p1', lotNumber: null },
    ]
    expect(findTargetConflict(doc, [line('p1', 'R-01'), line('p1', 'R-02')])).toBeNull()
    expect(findTargetConflict(doc, [line('p1', 'R-01'), line('p1', 'R-02'), line('p1', 'R-03')])).toEqual({ index: 2, sku: 'P1', bin: 'R-01' })
  })

  it('un producto que no está en el documento entra como línea extra y nunca choca', () => {
    const doc: DocLine[] = [{ productPublicId: 'p1', lotNumber: null }]
    expect(findTargetConflict(doc, [line('p9', 'R-01'), line('p9', 'R-02')])).toBeNull()
  })

  it('con lote: solo se suma (y choca) con la línea aplicada del mismo lote', () => {
    const doc: DocLine[] = [{ productPublicId: 'p2', lotNumber: 'L-1' }]
    // L-2 no encuentra línea libre ni aplicada de su lote → extra, sin choque.
    expect(findTargetConflict(doc, [line('p2', 'R-01', 'L-1'), line('p2', 'R-02', 'L-2')])).toBeNull()
    expect(findTargetConflict(doc, [line('p2', 'R-01', 'L-1'), line('p2', 'R-02', 'l-1')])).toEqual({ index: 1, sku: 'P2', bin: 'R-01' })
  })

  it('sin líneas del documento (recibo ciego) no hay choque', () => {
    expect(findTargetConflict([], [line('p1', 'R-01'), line('p1', 'R-02')])).toBeNull()
  })
})

describe('buildReceiptBody con modo de recepción', () => {
  const direct = buildLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '8', targetBinCode: 'R-01' })

  it('directo: manda receivingMode DIRECT y la posición destino de cada línea', () => {
    const body = buildReceiptBody('wh-1', { asnId: 7 }, [direct], 'DIRECT')
    expect(body.receivingMode).toBe('DIRECT')
    expect(body.lines[0]).toMatchObject({ productPublicId: 'p1', receivedQty: 8, targetBinCode: 'R-01' })
  })

  it('con acomodo: manda PUTAWAY y ninguna posición destino', () => {
    const body = buildReceiptBody('wh-1', null, [direct], 'PUTAWAY')
    expect(body.receivingMode).toBe('PUTAWAY')
    expect(body.lines[0].targetBinCode).toBeNull()
  })

  it('recibo abierto antes de actualizar la app (sin modo): sin modo ni posiciones, el servidor lo trata con acomodo (D9-A)', () => {
    const body = buildReceiptBody('wh-1', null, [direct])
    expect(body.receivingMode).toBeNull()
    expect(body.lines[0].targetBinCode).toBeNull()
  })
})

describe('daño declarado en la captura', () => {
  const base = { ...newLineDraft(NONE_PRODUCT), qtyText: '10', damaged: true }

  it('sin marcar, no hay daño ni bloquea', () => {
    expect(damageBlock(newLineDraft(NONE_PRODUCT))).toBeNull()
    expect(draftDamagedQty(newLineDraft(NONE_PRODUCT))).toBe(0)
  })

  it('pide una cantidad mayor que 0 y no mayor que lo recibido, la razón y, con Otra, escribirla', () => {
    expect(damageBlock({ ...base, damagedQtyText: '0', damageCause: 'ARRIVED_DAMAGED' })).toBe('qty')
    expect(damageBlock({ ...base, damagedQtyText: '11', damageCause: 'ARRIVED_DAMAGED' })).toBe('qty')
    expect(damageBlock({ ...base, damagedQtyText: '3', damageCause: '' })).toBe('cause')
    expect(damageBlock({ ...base, damagedQtyText: '3', damageCause: DAMAGE_CAUSE_OTHER, damageNote: '  ' })).toBe('note')
    expect(damageBlock({ ...base, damagedQtyText: '3', damageCause: DAMAGE_CAUSE_OTHER, damageNote: 'se mojó' })).toBeNull()
    expect(damageBlock({ ...base, damagedQtyText: '3,5', damageCause: 'TRANSIT_ACCIDENT' })).toBeNull()
  })

  it('un daño incompleto no deja agregar la línea; completo sí', () => {
    expect(canAddLine({ ...base, damagedQtyText: '3', damageCause: '' })).toBe(false)
    expect(canAddLine({ ...base, damagedQtyText: '3', damageCause: 'TRANSIT_ACCIDENT' })).toBe(true)
  })

  it('los productos con serie no declaran daño aquí', () => {
    const serial = { ...addSerial(newLineDraft(SERIAL_PRODUCT), 'A'), damaged: true, damagedQtyText: '9' }
    expect(draftDamagedQty(serial)).toBe(0)
    expect(damageBlock(serial)).toBeNull()
  })

  it('attachDamage reparte lo dañado en orden sin pasar de lo que trae cada línea y devuelve lo que sobra', () => {
    const draft = { ...base, qtyText: '10', damagedQtyText: '7', damageCause: DAMAGE_CAUSE_OTHER, damageNote: ' se mojó ' }
    const lines = [buildLine({ ...draft, qtyText: '4', targetBinCode: 'R-01' }), buildLine({ ...draft, qtyText: '4', targetBinCode: 'R-02' })]
    const placed = attachDamage(draft, lines, { binCode: 'Q-01', discard: false })
    expect(placed.lines.map((l) => l.damagedQty)).toEqual([4, 3])
    expect(placed.leftover).toBe(0)
    expect(placed.lines[0]).toMatchObject({ damageCause: DAMAGE_CAUSE_OTHER, damageNote: 'se mojó', damageBinCode: 'Q-01', damageDiscard: false })
    expect(attachDamage(draft, [lines[0]], { binCode: null, discard: true })).toMatchObject({ leftover: 3, lines: [{ damagedQty: 4, damageBinCode: null, damageDiscard: true }] })
  })

  it('la nota solo viaja con la causa Otra', () => {
    const draft = { ...base, damagedQtyText: '2', damageCause: 'ARRIVED_DAMAGED', damageNote: 'ruido' }
    expect(attachDamage(draft, [buildLine(draft)], { binCode: 'Q-01', discard: false }).lines[0].damageNote).toBeNull()
  })

  it('el cuerpo del envío lleva el daño de la línea (y nada si no hay)', () => {
    const draft = { ...base, damagedQtyText: '2', damageCause: 'TRANSIT_ACCIDENT' }
    const [damaged] = attachDamage(draft, [buildLine(draft)], { binCode: 'Q-01', discard: false }).lines
    const [discarded] = attachDamage(draft, [buildLine(draft)], { binCode: null, discard: true }).lines
    const body = buildReceiptBody('wh-1', null, [damaged, discarded, buildLine({ ...newLineDraft(NONE_PRODUCT), qtyText: '1' })])
    expect(body.lines[0]).toMatchObject({ damagedQty: 2, damageCause: 'TRANSIT_ACCIDENT', damageBinCode: 'Q-01', damageDiscard: null })
    expect(body.lines[1]).toMatchObject({ damagedQty: 2, damageBinCode: null, damageDiscard: true })
    expect(body.lines[2]).toMatchObject({ damagedQty: null, damageCause: null, damageBinCode: null, damageDiscard: null })
  })

  it('la posición de lo dañado puede ser de cualquier zona pero activa y existente', () => {
    const bins = [
      { code: 'Q-01', zoneTypeCode: 'QUARANTINE', isActive: true },
      { code: 'STG-01', zoneTypeCode: 'STAGING', isActive: true },
      { code: 'OLD', zoneTypeCode: 'STORAGE', isActive: false },
    ]
    expect(validateDamageBin('stg-01', bins)).toEqual({ ok: true, code: 'STG-01' })
    expect(validateDamageBin('OLD', bins)).toEqual({ ok: false, reason: 'inactive' })
    expect(validateDamageBin('NOPE', bins)).toEqual({ ok: false, reason: 'notFound' })
  })
})
