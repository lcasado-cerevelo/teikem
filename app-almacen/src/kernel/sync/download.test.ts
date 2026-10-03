import { __resetAllForTests } from 'expo-sqlite'

import { api, ApiError } from '../api/client'
import { __resetDbForTests, getDb } from '../db/database'
import { __resetSecureStoreForTests } from 'expo-secure-store'

import { __resetSessionForTests, saveDeviceIdentity } from '../auth/session'
import { downloadAsns, downloadBins, downloadForReceiving, downloadProducts, downloadPurchaseOrders, downloadPurchaseOrdersIfAllowed } from './download'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSecureStoreForTests()
  __resetSessionForTests()
  getMock.mockReset()
})

function page<T>(items: T[], nextCursor: string | null, serverTimeUtc: string) {
  return { data: { items, nextCursor, serverTimeUtc }, response: new Response(null, { status: 200 }) }
}

describe('downloadProducts', () => {
  it('sigue nextCursor hasta agotar las páginas y guarda cada producto', async () => {
    getMock
      .mockResolvedValueOnce(page([{ id: 1, publicId: 'p1', sku: 'SKU-1', name: 'Uno', isActive: true }], 'cur-2', '2026-01-01T00:00:00.000Z'))
      .mockResolvedValueOnce(page([{ id: 2, publicId: 'p2', sku: 'SKU-2', name: 'Dos', isActive: true }], null, '2026-01-01T00:00:01.000Z'))

    const result = await downloadProducts()

    expect(result).toEqual({ resource: 'products', pages: 2, items: 2 })
    expect(getMock).toHaveBeenCalledTimes(2)
    const rows = getDb().getAllSync<{ sku: string }>('SELECT sku FROM product ORDER BY id')
    expect(rows.map((r) => r.sku)).toEqual(['SKU-1', 'SKU-2'])
  })

  it('guarda la marca de agua con el serverTimeUtc de la PRIMERA página menos 5 minutos, y la manda en la próxima pasada', async () => {
    getMock
      .mockResolvedValueOnce(page([{ id: 1, publicId: 'p1', sku: 'A', name: 'A', isActive: true }], 'cur-2', '2026-01-01T00:10:00.000Z'))
      .mockResolvedValueOnce(page([], null, '2026-01-01T00:10:05.000Z'))

    await downloadProducts()
    const watermark = getDb().getFirstSync<{ since_utc: string }>("SELECT since_utc FROM sync_watermark WHERE resource = 'products'")
    expect(watermark?.since_utc).toBe('2026-01-01T00:05:00.000Z')

    getMock.mockReset()
    getMock.mockResolvedValueOnce(page([], null, '2026-01-01T00:20:00.000Z'))
    await downloadProducts()
    const secondCallQuery = getMock.mock.calls[0][1].params.query
    expect(secondCallQuery.since).toBe('2026-01-01T00:05:00.000Z')
  })

  it('isActive=false borra la fila local en vez de guardarla', async () => {
    getMock.mockResolvedValueOnce(page([{ id: 1, publicId: 'p1', sku: 'A', name: 'A', isActive: true }], null, '2026-01-01T00:00:00.000Z'))
    await downloadProducts()
    getMock.mockResolvedValueOnce(page([{ id: 1, isActive: false }], null, '2026-01-01T00:00:01.000Z'))
    await downloadProducts()
    const rows = getDb().getAllSync('SELECT * FROM product')
    expect(rows).toHaveLength(0)
  })
})

describe('downloadPurchaseOrders', () => {
  it('reescribe las líneas de la orden en cada pasada (sin duplicar)', async () => {
    getMock.mockResolvedValueOnce(
      page(
        [
          {
            id: 10,
            publicId: 'po-1',
            number: 'PO-100',
            warehousePublicId: 'wh-1',
            isActive: true,
            lines: [{ id: 1, productPublicId: 'p1', sku: 'A', qtyOrdered: 5, qtyReceived: 0, qtyPending: 5 }],
          },
        ],
        null,
        '2026-01-01T00:00:00.000Z',
      ),
    )
    await downloadPurchaseOrders()
    expect(getDb().getAllSync('SELECT * FROM purchase_order_line WHERE purchase_order_id = 10')).toHaveLength(1)

    getMock.mockReset()
    getMock.mockResolvedValueOnce(
      page(
        [
          {
            id: 10,
            publicId: 'po-1',
            number: 'PO-100',
            warehousePublicId: 'wh-1',
            isActive: true,
            lines: [
              { id: 1, productPublicId: 'p1', sku: 'A', qtyOrdered: 5, qtyReceived: 2, qtyPending: 3 },
              { id: 2, productPublicId: 'p2', sku: 'B', qtyOrdered: 1, qtyReceived: 0, qtyPending: 1 },
            ],
          },
        ],
        null,
        '2026-01-01T00:05:00.000Z',
      ),
    )
    await downloadPurchaseOrders()
    const lines = getDb().getAllSync<{ qty_received: number }>('SELECT qty_received FROM purchase_order_line WHERE purchase_order_id = 10 ORDER BY id')
    expect(lines).toHaveLength(2)
    expect(lines[0].qty_received).toBe(2)
  })

  it('isActive=false borra la orden y sus líneas', async () => {
    getMock.mockResolvedValueOnce(
      page([{ id: 10, publicId: 'po-1', number: 'PO-100', warehousePublicId: 'wh-1', isActive: true, lines: [{ id: 1, productPublicId: 'p1', qtyOrdered: 1, qtyReceived: 0, qtyPending: 1 }] }], null, '2026-01-01T00:00:00.000Z'),
    )
    await downloadPurchaseOrders()
    getMock.mockReset()
    getMock.mockResolvedValueOnce(page([{ id: 10, isActive: false }], null, '2026-01-01T00:05:00.000Z'))
    await downloadPurchaseOrders()
    expect(getDb().getAllSync('SELECT * FROM purchase_order WHERE id = 10')).toHaveLength(0)
    expect(getDb().getAllSync('SELECT * FROM purchase_order_line WHERE purchase_order_id = 10')).toHaveLength(0)
  })
})

describe('downloadAsns', () => {
  it('guarda el aviso y sus líneas', async () => {
    getMock.mockResolvedValueOnce(
      page(
        [
          {
            id: 5,
            warehousePublicId: 'wh-1',
            reference: 'ASN-1',
            statusCode: 'EXPECTED',
            isActive: true,
            lines: [{ id: 1, productPublicId: 'p1', sku: 'A', expectedQty: 4 }],
          },
        ],
        null,
        '2026-01-01T00:00:00.000Z',
      ),
    )
    await downloadAsns()
    const rows = getDb().getAllSync<{ reference: string }>('SELECT reference FROM asn')
    expect(rows).toEqual([{ reference: 'ASN-1' }])
    expect(getDb().getAllSync('SELECT * FROM asn_line WHERE asn_id = 5')).toHaveLength(1)
  })
})

describe('downloadBins (Lote 16)', () => {
  it('pide las posiciones del almacén indicado, guarda el tipo de zona y conserva las inactivas con is_active = 0', async () => {
    getMock.mockResolvedValueOnce(
      page(
        [
          { id: 1, code: 'RSV-A-01', warehousePublicId: 'wh-1', zoneId: 5, zoneCode: 'RSV', zoneName: 'Reserva', zoneTypeCode: 'RESERVE', isActive: true },
          { id: 2, code: 'STG-01', warehousePublicId: 'wh-1', zoneId: 6, zoneCode: 'STG', zoneName: 'Recepción', zoneTypeCode: 'STAGING', isActive: true },
        ],
        null,
        '2026-01-01T00:00:00.000Z',
      ),
    )
    const result = await downloadBins('wh-1')
    expect(result).toEqual({ resource: 'bins', pages: 1, items: 2 })
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/sync/bins')
    expect(getMock.mock.calls[0][1].params.query).toMatchObject({ warehousePublicId: 'wh-1', take: 500 })
    expect(getDb().getAllSync('SELECT code, zone_type_code, is_active FROM bin ORDER BY id')).toEqual([
      { code: 'RSV-A-01', zone_type_code: 'RESERVE', is_active: 1 },
      { code: 'STG-01', zone_type_code: 'STAGING', is_active: 1 },
    ])

    getMock.mockResolvedValueOnce(page([{ id: 1, code: 'RSV-A-01', warehousePublicId: 'wh-1', zoneTypeCode: 'RESERVE', isActive: false }], null, '2026-01-01T00:10:00.000Z'))
    await downloadBins('wh-1')
    expect(getDb().getFirstSync('SELECT is_active FROM bin WHERE id = 1')).toEqual({ is_active: 0 })
  })

  it('copia isProvisional de la posición (y lo apaga si el supervisor la confirma)', async () => {
    getMock.mockResolvedValueOnce(
      page(
        [
          { id: 1, code: 'Z-09', warehousePublicId: 'wh-1', zoneId: 5, isActive: true, isProvisional: true },
          { id: 2, code: 'A-01', warehousePublicId: 'wh-1', zoneId: 5, isActive: true },
        ],
        null,
        '2026-01-01T00:00:00.000Z',
      ),
    )
    await downloadBins('wh-1')
    expect(getDb().getAllSync('SELECT code, is_provisional FROM bin ORDER BY id')).toEqual([
      { code: 'Z-09', is_provisional: 1 },
      { code: 'A-01', is_provisional: 0 },
    ])
    getMock.mockResolvedValueOnce(page([{ id: 1, code: 'Z-09', warehousePublicId: 'wh-1', zoneId: 5, isActive: true, isProvisional: false }], null, '2026-01-01T00:10:00.000Z'))
    await downloadBins('wh-1')
    expect(getDb().getFirstSync('SELECT is_provisional FROM bin WHERE id = 1')).toEqual({ is_provisional: 0 })
  })

  it('la marca de agua es por almacén: otro almacén baja completo (sin since)', async () => {
    getMock.mockResolvedValueOnce(page([], null, '2026-01-01T00:10:00.000Z'))
    await downloadBins('wh-1')
    getMock.mockResolvedValueOnce(page([], null, '2026-01-01T00:20:00.000Z'))
    await downloadBins('wh-2')
    expect(getMock.mock.calls[1][1].params.query.since).toBeUndefined()
    getMock.mockResolvedValueOnce(page([], null, '2026-01-01T00:30:00.000Z'))
    await downloadBins('wh-1')
    expect(getMock.mock.calls[2][1].params.query.since).toBe('2026-01-01T00:05:00.000Z')
  })
})

describe('órdenes de compra sin permiso (403)', () => {
  const forbidden = () => Promise.reject(new ApiError(403, null))

  it('un 403 en órdenes de compra no aborta la pasada: salta ese recurso y baja avisos y posiciones', async () => {
    await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'T', defaultWarehousePublicId: 'wh-1', theme: null })
    getMock.mockImplementation((path: string) =>
      path === '/api/v1/sync/purchase-orders' ? forbidden() : Promise.resolve(page([], null, '2026-01-01T00:00:00.000Z')),
    )
    const results = await downloadForReceiving()
    expect(results.map((d) => d.resource)).toEqual(['products', 'purchaseOrders', 'asns', 'bins'])
    expect(results[1]).toEqual({ resource: 'purchaseOrders', pages: 0, items: 0 })
  })

  it('borra las órdenes que hubiera de antes y su marca, para volver a bajarlas completas si recupera el permiso', async () => {
    getMock.mockResolvedValueOnce(page([{ id: 7, publicId: 'po7', number: 'PO-7', isActive: true, lines: [] }], null, '2026-01-01T00:00:00.000Z'))
    await downloadPurchaseOrders()
    expect(getDb().getAllSync('SELECT id FROM purchase_order')).toHaveLength(1)
    getMock.mockImplementation(() => forbidden())
    await downloadPurchaseOrdersIfAllowed()
    expect(getDb().getAllSync('SELECT id FROM purchase_order')).toHaveLength(0)
    expect(getDb().getAllSync("SELECT 1 FROM sync_watermark WHERE resource = 'purchaseOrders'")).toHaveLength(0)
  })

  it('otros errores (red, 500) sí se propagan: la pasada falla como siempre', async () => {
    getMock.mockImplementation(() => Promise.reject(new ApiError(500, null)))
    await expect(downloadPurchaseOrdersIfAllowed()).rejects.toMatchObject({ status: 500 })
  })
})

describe('downloadForReceiving', () => {
  it('sin almacén por defecto no baja posiciones; con él, las baja al final', async () => {
    getMock.mockImplementation(() => Promise.resolve(page([], null, '2026-01-01T00:00:00.000Z')))
    expect((await downloadForReceiving()).map((d) => d.resource)).toEqual(['products', 'purchaseOrders', 'asns'])

    await saveDeviceIdentity({ devicePublicId: 'dev-1', deviceSecret: 's', tenantName: 'T', defaultWarehousePublicId: 'wh-1', theme: null })
    expect((await downloadForReceiving()).map((d) => d.resource)).toEqual(['products', 'purchaseOrders', 'asns', 'bins'])
  })
})
