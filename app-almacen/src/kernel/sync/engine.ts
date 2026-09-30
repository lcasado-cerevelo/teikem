// Lote 8A-app — orquesta la cola de salida y la bajada por diferencia (docs/mobile/app-almacen-plan.md §1.3). Se
// dispara al abrir la app, cada 60 s (useAutoSync, mientras Inicio está montado) y con el botón "Sincronizar ahora".
// La reconexión inmediata (NetInfo) queda para la próxima entrega junto con la pantalla de Sincronización (A5); el
// respaldo de 60 s ya cubre "se recuperó la señal" con un minuto de margen como mucho.
import { useEffect, useRef, useSyncExternalStore } from 'react'

import { sendHeartbeat } from '../auth/deviceAuth'
import { downloadForReceiving, type DownloadResult } from './download'
import { countPending, runOutbox, subscribeOutbox, type RunOutboxResult } from './outbox'

export interface SyncSummary {
  outbox: RunOutboxResult
  download: DownloadResult[]
  ranAtUtc: string
  error: string | null
}

let lastSummary: SyncSummary | null = null
let running: Promise<SyncSummary> | null = null
const listeners = new Set<() => void>()
function emit(): void {
  listeners.forEach((l) => l())
}

export function subscribeSync(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function getLastSync(): SyncSummary | null {
  return lastSummary
}

/** Un solo ciclo en vuelo: si ya hay uno corriendo (temporizador y botón a la vez), se espera el mismo. */
export function runSync(): Promise<SyncSummary> {
  running ??= runOnce().finally(() => {
    running = null
  })
  return running
}

async function runOnce(): Promise<SyncSummary> {
  let outbox: RunOutboxResult = { sent: 0, rejected: 0, stoppedForNetwork: false, remaining: countPending() }
  let download: DownloadResult[] = []
  let error: string | null = null
  try {
    outbox = await runOutbox()
    if (!outbox.stoppedForNetwork) {
      // Lote 16: el heartbeat va antes de bajar: trae el almacén por defecto y su modo de recepción (directo o con
      // acomodo), y la bajada de posiciones usa ese almacén. Hasta este lote no lo llamaba nadie.
      await sendHeartbeat()
      download = await downloadForReceiving()
    }
  } catch (err) {
    error = err instanceof Error ? err.message : String(err)
  }
  const summary: SyncSummary = { outbox, download, ranAtUtc: new Date().toISOString(), error }
  lastSummary = summary
  emit()
  return summary
}

const AUTO_SYNC_INTERVAL_MS = 60_000

/** Corre en cuanto se monta y cada 60 s mientras la pantalla siga montada (docs/mobile/app-almacen-plan.md §1.3). */
export function useAutoSync(): void {
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    void runSync()
    const id = setInterval(() => {
      if (mounted.current) void runSync()
    }, AUTO_SYNC_INTERVAL_MS)
    return () => {
      mounted.current = false
      clearInterval(id)
    }
  }, [])
}

/** Pendientes de enviar en este momento (cuenta viva: cambia al encolar, reintentar, descartar o al vaciarse). */
export function usePendingCount(): number {
  return useSyncExternalStore(subscribeOutbox, countPending, countPending)
}

/** Resultado de la última pasada (para mostrar "última vez" y errores en Inicio/Sincronización). */
export function useLastSync(): SyncSummary | null {
  return useSyncExternalStore(subscribeSync, getLastSync, getLastSync)
}

export function __resetSyncEngineForTests(): void {
  lastSummary = null
  running = null
  listeners.clear()
}
