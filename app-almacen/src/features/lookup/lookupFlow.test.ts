import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../../kernel/api/client'
import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { applyOnline, lookupLocal, lookupOnline, type LookupResult } from './lookupFlow'

jest.mock('../../kernel/api/client', () => {
  const actual = jest.requireActual('../../kernel/api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock
const WH = 'wh-1'
const ok = (data: unknown) => Promise.resolve({ data, response: new Response(null, { status: 200 }) })

function seed() {
  const db = getDb()
  db.runSync(`INSERT INTO product (id, public_id, sku, name, barcode, is_active) VALUES (1, 'p1', 'SKU-1', 'Guantes', '7501', 1)`)
  db.runSync(`INSERT INTO bin (id, code, warehouse_public_id, zone_type_code, is_active) VALUES (10, 'A-01', '${WH}', 'PICKING', 1)`)
  db.runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, qty_on_hand, qty_reserved, updated_at_utc)
     VALUES (1, '${WH}', 10, 1, 'p1', 5, 1, '2026-10-10T10:00:00.000Z')`,
  )
  db.runSync(`INSERT INTO sync_watermark (resource, since_utc, last_run_utc) VALUES ('balances:${WH}', '2026-10-10T09:55:00.000Z', '2026-10-10T10:00:00.000Z')`)
}

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  getMock.mockReset()
})

describe('lookupLocal (al instante, sin red)', () => {
  it('un producto sale con sus saldos locales', () => {
    seed()
    const r = lookupLocal(WH, '7501')
    expect(r).toMatchObject({ kind: 'rows', titleKey: 'lookup.productResult', source: 'local', asOfUtc: '2026-10-10T10:00:00.000Z' })
    expect(r?.kind === 'rows' && r.rows[0]).toMatchObject({ binCode: 'A-01', qtyOnHand: 5, qtyAvailable: 4 })
  })

  it('una posición sale con lo que hay en ella (agrupado por producto), también vacía', () => {
    seed()
    const r = lookupLocal(WH, 'a-01')
    expect(r).toMatchObject({ kind: 'bin', bin: { id: 10, code: 'A-01' }, source: 'local' })
    expect(r?.kind === 'bin' && r.items).toEqual([expect.objectContaining({ sku: 'SKU-1', qtyOnHand: 5, qtyAvailable: 4 })])
    getDb().runSync('DELETE FROM stock_balance')
    expect((lookupLocal(WH, 'A-01') as Extract<LookupResult, { kind: 'bin' }>).items).toEqual([])
  })

  it('texto libre busca en lo local; sin coincidencias no hay resultado', () => {
    seed()
    expect(lookupLocal(WH, 'guan')).toMatchObject({ kind: 'rows', titleKey: 'lookup.searchResult' })
    expect(lookupLocal(WH, 'nada')).toBeNull()
  })

  it('si el aparato nunca bajó saldos de ese almacén, no inventa nada (null)', () => {
    seed()
    expect(lookupLocal('otro-almacen', '7501')).toBeNull()
  })
})

describe('lookupOnline + applyOnline (el servidor manda cuando llega)', () => {
  it('ok: reemplaza lo local por lo vivo y el indicador pasa a «al día»', async () => {
    seed()
    getMock.mockReturnValueOnce(ok({ items: [{ id: 1, binCode: 'A-01', productPublicId: 'p1', sku: 'SKU-1', productName: 'Guantes', qtyOnHand: 9, qtyAvailable: 9 }] }))
    const out = await lookupOnline(WH, '7501')
    const next = applyOnline(lookupLocal(WH, '7501'), out)
    expect(next.refresh).toBe('done')
    expect(next.result).toMatchObject({ source: 'live' })
    expect(next.result?.kind === 'rows' && next.result.rows[0].qtyOnHand).toBe(9)
  })

  it('sin red: se queda lo local y el indicador avisa «sin conexión»', async () => {
    seed()
    getMock.mockResolvedValueOnce({ response: Response.error() })
    const local = lookupLocal(WH, '7501')
    const next = applyOnline(local, await lookupOnline(WH, '7501'))
    expect(next).toEqual({ result: local, refresh: 'offline', error: null })
  })

  it('sin red y sin nada local ni guardado: mensaje de sin señal', async () => {
    getMock.mockResolvedValue({ response: Response.error() })
    const next = applyOnline(null, await lookupOnline(WH, 'XYZ'))
    expect(next).toEqual({ result: null, refresh: 'offline', error: { key: 'noNetworkNoCache' } })
  })

  it('el servidor dice que no existe: manda sobre lo local desactualizado', async () => {
    seed()
    getMock.mockReturnValueOnce(ok({ items: [] }))
    const next = applyOnline(lookupLocal(WH, '7501'), await lookupOnline(WH, '7501'))
    expect(next).toEqual({ result: null, refresh: 'done', error: { key: 'notFound' } })
  })

  it('un error del servidor con algo ya a la vista no lo borra', () => {
    const shown = { kind: 'rows', titleKey: 'lookup.productResult', titleParams: {}, rows: [], source: 'local', asOfUtc: 'x' } as LookupResult
    expect(applyOnline(shown, { status: 'error', message: 'boom' })).toEqual({ result: shown, refresh: 'offline', error: null })
    expect(applyOnline(null, { status: 'error', message: 'boom' }).error).toEqual({ key: 'generic', message: 'boom' })
  })
})
