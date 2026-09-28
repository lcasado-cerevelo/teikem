import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { newLineDraft } from './receiveLogic'
import {
  addLocalReceiptLine,
  discardLocalReceipt,
  findDocByCode,
  findProductByCode,
  getOpenReceipt,
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
