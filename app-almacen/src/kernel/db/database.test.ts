import { deleteDatabaseSync, __resetAllForTests } from 'expo-sqlite'

import { getDb, __resetDbForTests } from './database'
import { getKv, setKv } from './kv'

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
