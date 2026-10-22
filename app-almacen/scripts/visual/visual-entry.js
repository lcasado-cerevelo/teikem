// Entrada de la app para la verificación visual en el navegador: inicia sql.js (SQLite en WebAssembly) y SOLO ENTONCES arranca la app, porque la app usa la base local de forma síncrona.
import initSqlJs from 'sql.js/dist/sql-wasm-inline.js'

initSqlJs().then((SQL) => {
  globalThis.__SQLJS__ = SQL
  require('expo-router/entry')
})
