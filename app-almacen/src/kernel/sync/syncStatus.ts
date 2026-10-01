// Texto de estado de sincronización de Inicio (2026-10-01): antes, si la última pasada fallaba, decía "{pendientes} con error"
// aunque no hubiera nada con error (con 0 pendientes: "0 con error"). Ahora dice qué pasó y qué hacer.

/** Clave de i18n del texto: la última pasada falló (con o sin pendientes), hay pendientes, o todo está enviado. */
export function syncStatusKey(pending: number, lastFailed: boolean): 'home.syncStatusError' | 'home.syncStatusErrorPending' | 'home.syncStatusPending' | 'home.syncStatusOk' {
  if (lastFailed) return pending > 0 ? 'home.syncStatusErrorPending' : 'home.syncStatusError'
  return pending > 0 ? 'home.syncStatusPending' : 'home.syncStatusOk'
}
