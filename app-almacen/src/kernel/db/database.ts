// Lote 8A-app — apertura de la base local y migraciones (ver schema.ts). Un solo archivo por instalación; nunca se borra
// al actualizar la app (borrarlo es reinstalar). El PRAGMA user_version guarda hasta dónde se migró.
import * as SQLite from 'expo-sqlite'

import { getSessionState } from '../auth/session'
import { MIGRATIONS, SCHEMA_VERSION } from './schema'

export type SQLiteDatabase = ReturnType<typeof SQLite.openDatabaseSync>

const DB_NAME = 'teikem_almacen.db'

let instance: SQLiteDatabase | null = null
let instanceName: string | null = null

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

/** Base local de la compañía activa (2026-09-30: un archivo por registro; el registro de antes usa `teikem_almacen.db`).
 *  Perezosa: un solo `expo-sqlite` abierto a la vez; al cambiar de compañía se cierra la anterior y se abre la suya. */
export function getDb(): SQLiteDatabase {
  const name = getSessionState().device?.dbName ?? DB_NAME
  if (instance && instanceName !== name) {
    instance.closeSync()
    instance = null
  }
  if (!instance) {
    instance = SQLite.openDatabaseSync(name)
    instanceName = name
    migrate(instance)
  }
  return instance
}

let shared: SQLiteDatabase | null = null

/** Base común del teléfono (`teikem_almacen.db`): configuración que no es de una compañía (servidor, idioma, tema). Si la
 *  compañía activa es el registro de antes (sin dbName), es la misma base. */
export function getSharedDb(): SQLiteDatabase {
  if (instance && instanceName === DB_NAME) return instance
  if (!shared) {
    shared = SQLite.openDatabaseSync(DB_NAME)
    migrate(shared)
  }
  return shared
}

/** Solo para pruebas: fuerza una base nueva en el siguiente getDb() (cada archivo de prueba llama primero
 *  `__resetAllForTests` del mock de expo-sqlite y luego esto). */
export function __resetDbForTests(): void {
  instance = null
  instanceName = null
  shared = null
}
