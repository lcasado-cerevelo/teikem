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

describe('migración v3 (Lote 16, recibo directo a posición)', () => {
  it('la base nueva queda en la versión 3 con las columnas nuevas y el índice de posiciones', () => {
    const db = getDb()
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(SCHEMA_VERSION)
    expect(SCHEMA_VERSION).toBe(3)
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
    expect(db.getFirstSync<{ user_version: number }>('PRAGMA user_version')?.user_version).toBe(3)
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
