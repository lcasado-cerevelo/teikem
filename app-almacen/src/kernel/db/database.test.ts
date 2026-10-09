import { deleteDatabaseSync, openDatabaseSync, __resetAllForTests } from 'expo-sqlite'

import { getDb, __resetDbForTests } from './database'
import { getKv, setKv } from './kv'
import { MIGRATIONS, SCHEMA_VERSION } from './schema'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

describe('base local', () => {
  it('migra el esquema y queda en la versión esperada', () => {
    const db = getDb()
    const row = db.getFirstSync<{ user_version: number }>('PRAGMA user_version')
    expect(row?.user_version).toBeGreaterThan(0)
    const tables = db
      .getAllSync<{ name: string }>("SELECT name FROM sqlite_master WHERE type = 'table'")
      .map((t) => t.name)
    expect(tables).toEqual(
      expect.arrayContaining([
        'product',
        'bin',
        'outbox',
        'sync_watermark',
        'local_receipt',
        'local_receipt_line',
        'local_pick',
        'local_pick_line',
        'local_count',
        'local_count_line',
        'balance_cache',
      ]),
    )
  })

  it('vuelve a abrir sin duplicar migraciones (idempotente)', () => {
    getDb()
    expect(() => getDb()).not.toThrow()
  })

  it('deleteDatabaseSync no rompe una apertura posterior (aislamiento entre pruebas)', () => {
    getDb()
    deleteDatabaseSync('teikem_almacen.db')
    __resetDbForTests()
    expect(() => getDb()).not.toThrow()
  })
})

describe('kv', () => {
  it('guarda, lee y borra un valor', () => {
    expect(getKv('x')).toBeNull()
    setKv('x', 'uno')
    expect(getKv('x')).toBe('uno')
    setKv('x', 'dos')
    expect(getKv('x')).toBe('dos')
    setKv('x', null)
    expect(getKv('x')).toBeNull()
  })
})

describe('migración v7 (daño en el recibo)', () => {
  it('la línea del recibo local guarda lo dañado, su causa, comentario, posición y desecho', () => {
    const db = getDb()
    const cols = db.getAllSync<{ name: string }>('PRAGMA table_info(local_receipt_line)').map((c) => c.name)
    expect(cols).toEqual(expect.arrayContaining(['damaged_qty', 'damage_cause', 'damage_note', 'damage_bin_code', 'damage_discard']))
    expect(SCHEMA_VERSION).toBeGreaterThanOrEqual(7)
  })
})

describe('migración v3 (Lote 16, recibo directo a posición)', () => {
  it('la base nueva queda en la versión 3 con las columnas nuevas y el índice de posiciones', () => {
    const db = getDb()
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(SCHEMA_VERSION)
    expect(SCHEMA_VERSION).toBeGreaterThanOrEqual(3)
    const cols = (table: string) => db.getAllSync<{ name: string }>(`PRAGMA table_info(${table})`).map((c) => c.name)
    expect(cols('local_receipt')).toContain('receiving_mode')
    expect(cols('local_receipt_line')).toContain('target_bin_code')
    expect(cols('bin')).toEqual(expect.arrayContaining(['code', 'warehouse_public_id', 'zone_type_code', 'is_active']))
    const indexes = db.getAllSync<{ name: string }>("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'bin'").map((i) => i.name)
    expect(indexes).toContain('ix_bin_warehouse_code')
  })

  it('un aparato en v2 con un recibo en curso migra sin perderlo: el recibo queda sin modo (entra con acomodo) y sus líneas sin destino', () => {
    // Base tal como la dejó la app anterior: migraciones v1 y v2 aplicadas, user_version = 2, un recibo abierto.
    const raw = openDatabaseSync('teikem_almacen.db')
    raw.execSync(MIGRATIONS[0])
    raw.execSync(MIGRATIONS[1])
    raw.execSync('PRAGMA user_version = 2;')
    raw.runSync(
      "INSERT INTO local_receipt (id, warehouse_public_id, doc_label, created_at_utc) VALUES (1, 'wh-1', NULL, '2026-09-29T10:00:00Z')",
    )
    raw.runSync(
      "INSERT INTO local_receipt_line (receipt_id, product_public_id, sku, received_qty) VALUES (1, 'p1', 'SKU-1', 4)",
    )

    const db = getDb()
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(SCHEMA_VERSION)
    expect(db.getFirstSync('SELECT warehouse_public_id, receiving_mode FROM local_receipt WHERE id = 1')).toEqual({
      warehouse_public_id: 'wh-1',
      receiving_mode: null,
    })
    expect(db.getFirstSync('SELECT received_qty, target_bin_code FROM local_receipt_line WHERE receipt_id = 1')).toEqual({
      received_qty: 4,
      target_bin_code: null,
    })
  })
})

describe('migración v4 (Lote A4, contar por producto)', () => {
  const cols = (table: string) =>
    getDb()
      .getAllSync<{ name: string; notnull: number }>(`PRAGMA table_info(${table})`)
      .reduce<Record<string, number>>((acc, c) => ({ ...acc, [c.name]: c.notnull }), {})

  it('la base nueva incluye lo de la versión 4: la posición del conteo es opcional, cada línea guarda su posición y su lote, y la cantidad admite blanco', () => {
    const db = getDb()
    expect(SCHEMA_VERSION).toBeGreaterThanOrEqual(4)
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(SCHEMA_VERSION)
    const count = cols('local_count')
    expect(count).toMatchObject({ mode: 1, bin_id: 0, product_public_id: 0, tracking_type_code: 0 })
    const line = cols('local_count_line')
    expect(line).toMatchObject({ counted_qty: 0, bin_id: 0, bin_code: 0, lot_id: 0, lot_number: 0, lot_expiry_date: 0, is_provisional_bin: 1 })
    const indexes = db.getAllSync<{ name: string }>("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'local_count_line'").map((i) => i.name)
    expect(indexes).toContain('ix_local_count_line_count')
    expect(db.getAllSync("SELECT name FROM sqlite_master WHERE name LIKE '%_v4'")).toEqual([])
  })

  it('un aparato en v3 con un conteo por posición en curso migra sin perderlo: el conteo queda "por posición" y sus líneas heredan la posición', () => {
    const raw = openDatabaseSync('teikem_almacen.db')
    raw.execSync(MIGRATIONS[0])
    raw.execSync(MIGRATIONS[1])
    raw.execSync(MIGRATIONS[2])
    raw.execSync('PRAGMA user_version = 3;')
    raw.runSync(
      "INSERT INTO local_count (id, count_id, warehouse_public_id, bin_id, bin_code, is_blind, created_at_utc) VALUES (1, 42, 'wh-1', 5, 'A-01', 0, '2026-10-01T10:00:00Z')",
    )
    raw.runSync(
      "INSERT INTO local_count_line (id, local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra) VALUES (7, 1, 70, 'p1', 'SKU-1', 'Uno', 3, 2, 0)",
    )
    raw.runSync(
      "INSERT INTO local_count_line (id, local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra) VALUES (8, 1, NULL, 'p9', 'SKU-9', 'Extra', NULL, 1, 1)",
    )

    const db = getDb()
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(SCHEMA_VERSION)
    expect(db.getFirstSync('SELECT id, count_id, mode, bin_id, bin_code, is_blind, product_public_id FROM local_count')).toEqual({
      id: 1,
      count_id: 42,
      mode: 'BIN',
      bin_id: 5,
      bin_code: 'A-01',
      is_blind: 0,
      product_public_id: null,
    })
    expect(db.getAllSync('SELECT id, line_id, counted_qty, is_extra, bin_id, bin_code, lot_number, is_provisional_bin FROM local_count_line ORDER BY id')).toEqual([
      { id: 7, line_id: 70, counted_qty: 2, is_extra: 0, bin_id: 5, bin_code: 'A-01', lot_number: null, is_provisional_bin: 0 },
      { id: 8, line_id: null, counted_qty: 1, is_extra: 1, bin_id: 5, bin_code: 'A-01', lot_number: null, is_provisional_bin: 0 },
    ])
    // la llave foránea sigue apuntando al conteo: borrarlo borra sus líneas
    db.runSync('DELETE FROM local_count WHERE id = 1')
    expect(db.getAllSync('SELECT id FROM local_count_line')).toEqual([])
    // una línea nueva sigue numerándose después de las copiadas (AUTOINCREMENT)
    db.runSync("INSERT INTO local_count (id, count_id, warehouse_public_id, is_blind, created_at_utc) VALUES (2, 43, 'wh-1', 1, 'x')")
    const info = db.runSync("INSERT INTO local_count_line (local_count_id, product_public_id) VALUES (2, 'p1')")
    expect(info.lastInsertRowId).toBeGreaterThan(8)
  })
})

describe('migración v5 (Lote A4, adenda: posición provisional sincronizada)', () => {
  it('la base nueva queda en la versión 6 con bin.is_provisional (por defecto 0) y la tabla del orden de salida', () => {
    const db = getDb()
    expect(SCHEMA_VERSION).toBeGreaterThanOrEqual(6)
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(SCHEMA_VERSION)
    expect(db.getAllSync<{ name: string }>('PRAGMA table_info(stock_exit)').map((c) => c.name)).toEqual(
      expect.arrayContaining(['warehouse_public_id', 'product_public_id', 'rank', 'bin_code', 'lot_number', 'expiry_date', 'available']),
    )
    const col = db.getAllSync<{ name: string; notnull: number; dflt_value: string | null }>('PRAGMA table_info(bin)').find((c) => c.name === 'is_provisional')
    expect(col).toMatchObject({ notnull: 1, dflt_value: '0' })
  })

  it('un aparato en v4 conserva sus posiciones, conteo en curso y sincronización; las posiciones quedan sin marca y bajan completas otra vez', () => {
    const raw = openDatabaseSync('teikem_almacen.db')
    for (let i = 0; i < 4; i++) raw.execSync(MIGRATIONS[i])
    raw.execSync('PRAGMA user_version = 4;')
    raw.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, zone_code, is_active) VALUES (1, 'A-01', 'wh-1', 5, 'PCK', 1)")
    raw.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, zone_code, is_active) VALUES (2, 'OLD-1', 'wh-1', 5, 'PCK', 0)")
    raw.runSync("INSERT INTO sync_watermark (resource, since_utc, last_run_utc) VALUES ('bins:wh-1', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z')")
    raw.runSync("INSERT INTO sync_watermark (resource, since_utc, last_run_utc) VALUES ('products', '2026-10-01T00:00:00Z', '2026-10-01T00:00:00Z')")
    raw.runSync(
      "INSERT INTO local_count (id, count_id, warehouse_public_id, mode, product_public_id, sku, product_name, tracking_type_code, is_blind, created_at_utc) VALUES (1, 42, 'wh-1', 'PRODUCT', 'p1', 'SKU-1', 'Uno', 'NONE', 1, '2026-10-02T10:00:00Z')",
    )
    raw.runSync("INSERT INTO local_count_line (id, local_count_id, product_public_id, bin_id, bin_code, counted_qty, is_provisional_bin) VALUES (5, 1, 'p1', 9, 'Z-09', 3, 1)")

    const db = getDb()
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(SCHEMA_VERSION)
    expect(db.getAllSync('SELECT id, code, zone_code, is_active, is_provisional FROM bin ORDER BY id')).toEqual([
      { id: 1, code: 'A-01', zone_code: 'PCK', is_active: 1, is_provisional: 0 },
      { id: 2, code: 'OLD-1', zone_code: 'PCK', is_active: 0, is_provisional: 0 },
    ])
    // se reinicia la marca de agua de las posiciones (v5) y la de productos (v9, empaque): ambos bajan completos otra vez
    expect(db.getAllSync('SELECT resource FROM sync_watermark')).toEqual([])
    expect(db.getAllSync<{ name: string }>('PRAGMA table_info(product)').map((c) => c.name)).toEqual(expect.arrayContaining(['pack_uom_code', 'pack_uom_name', 'pack_qty']))
    // lo que había en un conteo por producto en curso sigue igual
    expect(db.getFirstSync('SELECT count_id, mode, sku FROM local_count')).toEqual({ count_id: 42, mode: 'PRODUCT', sku: 'SKU-1' })
    expect(db.getFirstSync('SELECT bin_code, counted_qty, is_provisional_bin FROM local_count_line')).toEqual({ bin_code: 'Z-09', counted_qty: 3, is_provisional_bin: 1 })
  })
})
