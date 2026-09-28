// Mock de Jest para expo-sqlite (subconjunto síncrono que usa src/kernel/db/database.ts), respaldado por better-sqlite3
// (un motor SQL real, no un doble de prueba a mano) para que las pruebas del esquema y del motor de sincronización
// corran contra SQL de verdad. Jest lo toma automáticamente por vivir en __mocks__ junto a node_modules (docs de Jest).
import Database from 'better-sqlite3'

type BindParams = unknown[] | Record<string, unknown> | undefined

const dbs = new Map<string, Database.Database>()

function bind(params: BindParams): unknown[] | Record<string, unknown> {
  return params ?? []
}

class FakeSQLiteDatabase {
  constructor(private readonly raw: Database.Database) {}

  execSync(sql: string): void {
    this.raw.exec(sql)
  }

  runSync(sql: string, params?: BindParams): { changes: number; lastInsertRowId: number } {
    const info = this.raw.prepare(sql).run(bind(params) as never)
    return { changes: info.changes, lastInsertRowId: Number(info.lastInsertRowid) }
  }

  getFirstSync<T>(sql: string, params?: BindParams): T | null {
    return (this.raw.prepare(sql).get(bind(params) as never) as T | undefined) ?? null
  }

  getAllSync<T>(sql: string, params?: BindParams): T[] {
    return this.raw.prepare(sql).all(bind(params) as never) as T[]
  }

  withTransactionSync(task: () => void): void {
    this.raw.transaction(task)()
  }

  closeSync(): void {
    this.raw.close()
  }
}

export function openDatabaseSync(name: string): FakeSQLiteDatabase {
  let raw = dbs.get(name)
  if (!raw) {
    raw = new Database(':memory:')
    dbs.set(name, raw)
  }
  return new FakeSQLiteDatabase(raw)
}

export function deleteDatabaseSync(name: string): void {
  const raw = dbs.get(name)
  if (raw) {
    raw.close()
    dbs.delete(name)
  }
}

/** Solo para pruebas: aísla cada archivo de prueba (llamar en beforeEach/afterEach). */
export function __resetAllForTests(): void {
  dbs.forEach((raw) => raw.close())
  dbs.clear()
}
