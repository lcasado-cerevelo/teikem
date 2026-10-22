// Sustituto de expo-sqlite SOLO para la verificación visual en el navegador (npm run export:visual): la versión web de expo-sqlite no sirve para la API síncrona que usa la app.
// Mismo subconjunto que el mock de Jest (__mocks__/expo-sqlite.ts) pero respaldado por sql.js (SQLite en WebAssembly), ya iniciado por visual-entry.js. No se usa en el aparato.
const bindOf = (params) => (params == null ? [] : params)
const dbs = new Map()

class WebDb {
  constructor(raw) { this.raw = raw }
  execSync(sql) { this.raw.exec(sql) }
  runSync(sql, params) {
    this.raw.run(sql, bindOf(params))
    const changes = this.raw.getRowsModified()
    const id = this.raw.exec('SELECT last_insert_rowid() AS id')[0]?.values?.[0]?.[0] ?? 0
    return { changes, lastInsertRowId: Number(id) }
  }
  getAllSync(sql, params) {
    const stmt = this.raw.prepare(sql)
    try {
      stmt.bind(bindOf(params))
      const rows = []
      while (stmt.step()) rows.push(stmt.getAsObject())
      return rows
    } finally {
      stmt.free()
    }
  }
  getFirstSync(sql, params) { return this.getAllSync(sql, params)[0] ?? null }
  withTransactionSync(task) {
    this.raw.exec('BEGIN')
    try { task(); this.raw.exec('COMMIT') } catch (e) { this.raw.exec('ROLLBACK'); throw e }
  }
  closeSync() {}
}

export function openDatabaseSync(name) {
  let raw = dbs.get(name)
  if (!raw) { raw = new globalThis.__SQLJS__.Database(); dbs.set(name, raw) }
  return new WebDb(raw)
}
export function deleteDatabaseSync(name) { dbs.delete(name) }
