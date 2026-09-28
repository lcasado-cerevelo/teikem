// Lote 8A-app — apertura de la base local y migraciones (ver schema.ts). Un solo archivo por instalación; nunca se borra
// al actualizar la app (borrarlo es reinstalar). El PRAGMA user_version guarda hasta dónde se migró.
import * as SQLite from 'expo-sqlite'

import { MIGRATIONS, SCHEMA_VERSION } from './schema'

export type SQLiteDatabase = ReturnType<typeof SQLite.openDatabaseSync>

const DB_NAME = 'teikem_almacen.db'

let instance: SQLiteDatabase | null = null

function migrate(db: SQLiteDatabase): void {
  const row = db.getFirstSync<{ user_version: number }>('PRAGMA user_version')
  let version = row?.user_version ?? 0
  db.execSync('PRAGMA foreign_keys = ON;')
  while (version < SCHEMA_VERSION) {
    const sql = MIGRATIONS[version]
    if (!sql) throw new Error(`Falta la migración de la base local para la versión ${version + 1}.`)
    db.execSync(sql)
    version += 1
    db.execSync(`PRAGMA user_version = ${version};`)
  }
}

/** Base local (perezosa, un solo `expo-sqlite` abierto por proceso). */
export function getDb(): SQLiteDatabase {
  if (!instance) {
    instance = SQLite.openDatabaseSync(DB_NAME)
    migrate(instance)
  }
  return instance
}

/** Solo para pruebas: fuerza una base nueva en el siguiente getDb() (cada archivo de prueba llama primero
 *  `__resetAllForTests` del mock de expo-sqlite y luego esto). */
export function __resetDbForTests(): void {
  instance = null
}
