import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { newLineDraft, validateTargetBin } from './receiveLogic'
import {
  addLocalReceiptLine,
  countLocalBins,
  discardLocalReceipt,
  findDocByCode,
  findLocalBinsByCode,
  findProductByCode,
  getDocLines,
  getDocProgress,
  getOpenReceipt,
  listPendingDocs,
  removeLocalReceiptLine,
  startLocalReceipt,
} from './localLookup'
import { buildLine } from './receiveLogic'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  const db = getDb()
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'SKU-1', 'Uno', '7501234', 'NONE', 1)")
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (2, 'p2', 'SKU-2', 'Dos', NULL, 'LOT', 1)")
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (3, 'p3', 'SKU-3', 'Inactivo', '999', 'NONE', 0)")
  db.runSync("INSERT INTO purchase_order (id, public_id, number, warehouse_public_id, is_active) VALUES (10, 'po-1', 'PO-100', 'wh-1', 1)")
  db.runSync("INSERT INTO asn (id, warehouse_public_id, reference, is_active) VALUES (20, 'wh-1', 'ASN-9', 1)")
})

describe('findProductByCode', () => {
  it('encuentra por código de barras o por SKU', () => {
    expect(findProductByCode('7501234')).toMatchObject({ publicId: 'p1', sku: 'SKU-1', trackingTypeCode: 'NONE' })
    expect(findProductByCode('SKU-2')).toMatchObject({ publicId: 'p2', trackingTypeCode: 'LOT' })
  })

  it('no encuentra un producto inactivo ni uno inexistente', () => {
    expect(findProductByCode('999')).toBeNull()
    expect(findProductByCode('no-existe')).toBeNull()
  })
})

describe('findDocByCode', () => {
  it('encuentra la orden de compra por número', () => {
    expect(findDocByCode('wh-1', 'PO-100')).toEqual({ kind: 'po', purchaseOrderPublicId: 'po-1', asnId: null, label: 'PO-100' })
  })

  it('encuentra el aviso por referencia', () => {
    expect(findDocByCode('wh-1', 'ASN-9')).toEqual({ kind: 'asn', purchaseOrderPublicId: null, asnId: 20, label: 'ASN-9' })
  })

  it('no encuentra nada de otro almacén ni un código inventado', () => {
    expect(findDocByCode('wh-2', 'PO-100')).toBeNull()
    expect(findDocByCode('wh-1', 'X')).toBeNull()
  })
})

describe('recibo local', () => {
  it('empieza vacío (sin recibo abierto)', () => {
    expect(getOpenReceipt()).toBeNull()
  })

  it('arranca un recibo ciego, agrega líneas y las relee tal cual', () => {
    const id = startLocalReceipt('wh-1', null)
    const line = buildLine({ ...newLineDraft({ publicId: 'p1', sku: 'SKU-1', name: 'Uno', trackingTypeCode: 'NONE' }), qtyText: '3' })
    addLocalReceiptLine(id, line)

    const open = getOpenReceipt()
    expect(open?.doc).toBeNull()
    expect(open?.lineRows).toHaveLength(1)
    expect(open?.lineRows[0]).toMatchObject({ productPublicId: 'p1', receivedQty: 3, serialNumbers: null })
  })

  it('arranca un recibo contra un aviso y lo guarda con su id', () => {
    const doc = findDocByCode('wh-1', 'ASN-9')!
    startLocalReceipt('wh-1', doc)
    expect(getOpenReceipt()?.doc).toEqual({ kind: 'asn', purchaseOrderPublicId: null, asnId: 20, label: 'ASN-9' })
  })

  it('guarda los números de serie como lista', () => {
    const id = startLocalReceipt('wh-1', null)
    const line = buildLine({
      ...newLineDraft({ publicId: 'p3', sku: 'SKU-3', name: 'Serial', trackingTypeCode: 'SERIAL' }),
      serials: ['SN-1', 'SN-2'],
    })
    addLocalReceiptLine(id, line)
    expect(getOpenReceipt()?.lineRows[0].serialNumbers).toEqual(['SN-1', 'SN-2'])
  })

  it('quita una línea por id', () => {
    const id = startLocalReceipt('wh-1', null)
    const line = buildLine({ ...newLineDraft({ publicId: 'p1', sku: 'SKU-1', name: 'Uno', trackingTypeCode: 'NONE' }), qtyText: '1' })
    addLocalReceiptLine(id, line)
    const lineId = getOpenReceipt()!.lineRows[0].id
    removeLocalReceiptLine(lineId)
    expect(getOpenReceipt()?.lineRows).toHaveLength(0)
  })

  it('un recibo a la vez: empezar otro mientras uno sigue abierto lanza y no toca el que ya está', () => {
    const first = startLocalReceipt('wh-1', null)
    addLocalReceiptLine(first, buildLine({ ...newLineDraft({ publicId: 'p1', sku: 'SKU-1', name: 'Uno', trackingTypeCode: 'NONE' }), qtyText: '1' }))
    expect(() => startLocalReceipt('wh-1', null)).toThrow(/ya hay un recibo en curso/i)
    expect(getOpenReceipt()?.id).toBe(first)
    expect(getOpenReceipt()?.lineRows).toHaveLength(1)
  })

  it('cancelar (discardLocalReceipt) libera el aparato para empezar uno nuevo', () => {
    startLocalReceipt('wh-1', null)
    discardLocalReceipt()
    expect(() => startLocalReceipt('wh-1', null)).not.toThrow()
    expect(getOpenReceipt()).not.toBeNull()
  })

  it('discardLocalReceipt borra el recibo y sus líneas', () => {
    const id = startLocalReceipt('wh-1', null)
    addLocalReceiptLine(id, buildLine({ ...newLineDraft({ publicId: 'p1', sku: 'SKU-1', name: 'Uno', trackingTypeCode: 'NONE' }), qtyText: '1' }))
    discardLocalReceipt()
    expect(getOpenReceipt()).toBeNull()
  })
})

// ------------------------------------------------------------------ Lote 16: recibo directo a posición

describe('recibo local con modo y posición destino', () => {
  it('guarda el modo con que se abrió y la posición destino de cada línea, y los relee tal cual', () => {
    const id = startLocalReceipt('wh-1', null, 'DIRECT')
    addLocalReceiptLine(id, buildLine({ ...newLineDraft({ publicId: 'p1', sku: 'SKU-1', name: 'Uno', trackingTypeCode: 'NONE' }), qtyText: '2', targetBinCode: 'R-01' }))
    const open = getOpenReceipt()
    expect(open?.receivingMode).toBe('DIRECT')
    expect(open?.lines[0].targetBinCode).toBe('R-01')
  })

  it('sin modo (como un recibo abierto antes de actualizar la app) queda en null y la línea sin destino', () => {
    const id = startLocalReceipt('wh-1', null)
    addLocalReceiptLine(id, buildLine({ ...newLineDraft({ publicId: 'p1', sku: 'SKU-1', name: 'Uno', trackingTypeCode: 'NONE' }), qtyText: '1' }))
    const open = getOpenReceipt()
    expect(open?.receivingMode).toBeNull()
    expect(open?.lines[0].targetBinCode).toBeNull()
  })
})

describe('posiciones locales', () => {
  beforeEach(() => {
    const db = getDb()
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (1, 'RSV-A-01', 'wh-1', 'RESERVE', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (2, 'STG-01', 'wh-1', 'STAGING', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (3, 'OLD-01', 'wh-1', 'RESERVE', 0)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (4, 'RSV-A-01', 'wh-2', 'RESERVE', 1)")
  })

  it('busca por código sin distinguir mayúsculas y solo en el almacén indicado', () => {
    expect(findLocalBinsByCode('wh-1', ' rsv-a-01 ')).toEqual([{ code: 'RSV-A-01', zoneTypeCode: 'RESERVE', isActive: true }])
    expect(findLocalBinsByCode('wh-3', 'RSV-A-01')).toEqual([])
    expect(findLocalBinsByCode('wh-1', '')).toEqual([])
  })

  it('devuelve también las desactivadas y las de recepción (la decisión es de validateTargetBin)', () => {
    expect(findLocalBinsByCode('wh-1', 'OLD-01')).toEqual([{ code: 'OLD-01', zoneTypeCode: 'RESERVE', isActive: false }])
    expect(validateTargetBin('OLD-01', findLocalBinsByCode('wh-1', 'OLD-01'))).toEqual({ ok: false, reason: 'inactive' })
    expect(validateTargetBin('STG-01', findLocalBinsByCode('wh-1', 'STG-01'))).toEqual({ ok: false, reason: 'notStorage' })
    expect(validateTargetBin('rsv-a-01', findLocalBinsByCode('wh-1', 'rsv-a-01'))).toEqual({ ok: true, code: 'RSV-A-01' })
  })

  it('cuenta las posiciones del almacén (0 = todavía no se bajaron)', () => {
    expect(countLocalBins('wh-1')).toBe(3)
    expect(countLocalBins('wh-9')).toBe(0)
  })
})

describe('getDocLines', () => {
  it('aviso: sus líneas con lote, en orden', () => {
    const db = getDb()
    db.runSync("INSERT INTO asn_line (id, asn_id, product_public_id, expected_qty, lot_number) VALUES (1, 20, 'p2', 5, 'L-1')")
    db.runSync("INSERT INTO asn_line (id, asn_id, product_public_id, expected_qty, lot_number) VALUES (2, 20, 'p1', 3, NULL)")
    expect(getDocLines(findDocByCode('wh-1', 'ASN-9'))).toEqual([
      { productPublicId: 'p2', lotNumber: 'L-1' },
      { productPublicId: 'p1', lotNumber: null },
    ])
  })

  it('orden de compra: solo las líneas con algo pendiente, sin lote; recibo ciego: ninguna', () => {
    const db = getDb()
    db.runSync("INSERT INTO purchase_order_line (id, purchase_order_id, product_public_id, qty_ordered, qty_received, qty_pending) VALUES (1, 10, 'p1', 5, 0, 5)")
    db.runSync("INSERT INTO purchase_order_line (id, purchase_order_id, product_public_id, qty_ordered, qty_received, qty_pending) VALUES (2, 10, 'p2', 5, 5, 0)")
    expect(getDocLines(findDocByCode('wh-1', 'PO-100'))).toEqual([{ productPublicId: 'p1', lotNumber: null }])
    expect(getDocLines(null)).toEqual([])
  })
})

describe('getDocProgress (Lote 186)', () => {
  it('aviso con algo recibido por partes: lo recibido de cada línea; sin nada recibido, null', () => {
    const db = getDb()
    db.runSync("INSERT INTO asn_line (id, asn_id, product_public_id, sku, expected_qty, posted_qty) VALUES (1, 20, 'p1', 'SKU-1', 10, 0)")
    expect(getDocProgress(findDocByCode('wh-1', 'ASN-9'))).toBeNull()
    db.runSync('UPDATE asn_line SET posted_qty = 4 WHERE id = 1')
    expect(getDocProgress(findDocByCode('wh-1', 'ASN-9'))).toEqual([{ sku: 'SKU-1', expectedQty: 10, postedQty: 4 }])
    expect(getDocProgress(null)).toBeNull()
  })
})

describe('listPendingDocs (Lote 161)', () => {
  beforeEach(() => {
    const db = getDb()
    db.runSync("INSERT INTO asn (id, warehouse_public_id, client_name, reference, expected_date, number, is_active) VALUES (21, 'wh-1', 'AxisCare', 'AX-4012', '2026-10-21', 'LLE-00021', 1)")
    db.runSync("INSERT INTO asn (id, warehouse_public_id, client_name, reference, expected_date, number, is_active) VALUES (22, 'wh-1', 'Otro', 'OT-1', '2026-10-22', 'LLE-00022', 1)")
    // un aviso nacido de una orden de compra se ve como orden, no como aviso
    db.runSync("INSERT INTO asn (id, warehouse_public_id, purchase_order_public_id, reference, is_active) VALUES (23, 'wh-1', 'po-1', 'PO-100', 1)")
    db.runSync("INSERT INTO purchase_order (id, public_id, number, warehouse_public_id, supplier_name, is_active) VALUES (11, 'po-2', 'PO-200', 'wh-1', 'Proveedor Sur', 1)")
    db.runSync("INSERT INTO purchase_order (id, public_id, number, warehouse_public_id, is_active) VALUES (12, 'po-3', 'PO-300', 'wh-2', 1)")
  })

  it('lista los avisos de cliente del almacén sin los nacidos de una orden', () => {
    const rows = listPendingDocs('wh-1', 'asn', '', 30)
    expect(rows.map((r) => r.doc.asnId)).toEqual([21, 22, 20])
    expect(rows[0]).toMatchObject({ title: 'LLE-00021 · AX-4012', subtitle: 'AxisCare · 2026-10-21', doc: { kind: 'asn', label: 'LLE-00021' } })
  })

  it('busca por un pedazo, sin distinguir donde cae (referencia, cliente o fecha)', () => {
    expect(listPendingDocs('wh-1', 'asn', '401', 30).map((r) => r.doc.asnId)).toEqual([21])
    expect(listPendingDocs('wh-1', 'asn', 'axis', 30).map((r) => r.doc.asnId)).toEqual([21])
    expect(listPendingDocs('wh-1', 'asn', '10-22', 30).map((r) => r.doc.asnId)).toEqual([22])
    expect(listPendingDocs('wh-1', 'asn', 'zzz', 30)).toEqual([])
  })

  it('Lote 170: busca por el número del aviso (LLE-#####) y lo encuentra al escanearlo', () => {
    expect(listPendingDocs('wh-1', 'asn', 'lle-00022', 30).map((r) => r.doc.asnId)).toEqual([22])
    expect(findDocByCode('wh-1', 'LLE-00021')).toMatchObject({ kind: 'asn', asnId: 21, label: 'LLE-00021' })
    expect(findDocByCode('wh-1', 'AX-4012')).toMatchObject({ asnId: 21, label: 'LLE-00021' })
  })

  it('lista las órdenes del almacén y busca por número o proveedor', () => {
    expect(listPendingDocs('wh-1', 'po', '', 30).map((r) => r.title)).toEqual(['PO-100', 'PO-200'])
    expect(listPendingDocs('wh-1', 'po', 'sur', 30).map((r) => r.title)).toEqual(['PO-200'])
  })

  it('respeta el límite pedido (la pantalla sube de a poco, sin tope total)', () => {
    expect(listPendingDocs('wh-1', 'asn', '', 2)).toHaveLength(2)
  })
})
