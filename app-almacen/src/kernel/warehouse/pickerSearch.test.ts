import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests, getDb } from '../db/database'
import { likeTerm, searchBins, searchPicker, searchProducts } from './pickerSearch'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  const db = getDb()
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (1, 'p1', 'TOR-10', 'Tornillo 10mm', '7501', 'NONE', 1)")
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (2, 'p2', 'TUE-05', 'Tuerca 5mm', '7502', 'NONE', 1)")
  db.runSync("INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, is_active) VALUES (3, 'p3', 'OLD-01', 'Tornillo viejo', '7503', 'NONE', 0)")
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (1, 'A-01', 'wh-1', 'RSV', 'Reserva', 'RESERVE', 1)")
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (2, 'B-02', 'wh-1', 'PCK', 'Picking', 'PICKING', 1)")
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (3, 'A-09', 'wh-2', 'RSV', 'Reserva', 'RESERVE', 1)")
  db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_code, zone_name, zone_type_code, is_active) VALUES (4, 'A-77', 'wh-1', 'RSV', 'Reserva', 'RESERVE', 0)")
})

describe('búsqueda del selector', () => {
  it('likeTerm escapa % _ y \\', () => {
    expect(likeTerm(' a%b_c\\ ')).toBe('%a\\%b\\_c\\\\%')
  })

  it('productos: por SKU, nombre o código de barras, solo activos, vacío = todos', () => {
    expect(searchProducts('tornillo').map((p) => p.code)).toEqual(['TOR-10'])
    expect(searchProducts('TUE').map((p) => p.code)).toEqual(['TUE-05'])
    expect(searchProducts('7502').map((p) => p.code)).toEqual(['TUE-05'])
    expect(searchProducts('').map((p) => p.code)).toEqual(['TOR-10', 'TUE-05'])
    expect(searchProducts('zzz')).toEqual([])
  })

  it('posiciones: del almacén, activas, por código o zona', () => {
    expect(searchBins('wh-1', 'a-').map((b) => b.code)).toEqual(['A-01'])
    expect(searchBins('wh-1', 'picking').map((b) => b.code)).toEqual(['B-02'])
    expect(searchBins('wh-1', '').map((b) => b.code)).toEqual(['A-01', 'B-02'])
  })

  it('"any" junta productos y posiciones; sin almacén no trae posiciones', () => {
    expect(searchPicker('any', 'wh-1', '0').map((i) => `${i.kind}:${i.code}`)).toEqual(['product:TOR-10', 'product:TUE-05', 'bin:A-01', 'bin:B-02'])
    expect(searchPicker('bin', null, '').length).toBe(0)
  })
})
