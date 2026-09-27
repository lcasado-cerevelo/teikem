// Datos de "Pulso del día" (P3): el GET de Pulso y la preferencia de rango de fecha de cada usuario por tarjeta.
// Lote F6: tarjetas de almacén calculadas en cliente (saldo actual, sin rango de fecha) sobre los endpoints del módulo.
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type DateRangeRequest = components['schemas']['DateRangeRequest']
/** Tipo de tarjeta de Pulso: indicador o gráfico. */
export type PulseItemKind = 'indicator' | 'chart'

export const PULSE_QUERY_KEY = ['/api/v1/analytics/pulse'] as const

/** `GET /api/v1/analytics/pulse`: indicadores y gráficos marcados para Pulso, ya calculados por el servidor.
 *  `enabled` en false evita la llamada cuando el usuario no tiene `analytics.view` o el módulo ANALYTICS está apagado
 *  (bienvenida sin datos). Es la pantalla de inicio: un 403 no redirige (evita el ciclo '/' ↔ '/module-off'),
 *  Pulso muestra el error en su lugar. */
export function usePulse(enabled: boolean) {
  return useQuery({
    queryKey: PULSE_QUERY_KEY,
    queryFn: () => unwrap(api.GET('/api/v1/analytics/pulse')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/**
 * Mi rango de fecha para un indicador o gráfico (`PUT /api/v1/analytics/{indicators|charts}/{id}/my-date-range`):
 * preferencia por usuario (no edita la definición; basta `analytics.view`). Al guardar se recalcula Pulso.
 */
export function useSetMyDateRange() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ kind, id, body }: { kind: PulseItemKind; id: number; body: DateRangeRequest }) =>
      kind === 'indicator'
        ? unwrap(api.PUT('/api/v1/analytics/indicators/{id}/my-date-range', { params: { path: { id } }, body }))
        : unwrap(api.PUT('/api/v1/analytics/charts/{id}/my-date-range', { params: { path: { id } }, body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: PULSE_QUERY_KEY }),
  })
}

// ---------------------------------------------------------------------------------------------------------------------
// Tarjetas de almacén (Lote F6). No son indicadores del motor de analítica: se calculan en cliente con los totales que
// ya devuelven los endpoints de almacén (`take=1` para no paginar todo). Son saldo actual, no actividad de un período:
// no llevan rango de fecha. Las claves son las mismas `[ruta, params]` que usa `features/warehouse/api.ts`, así que las
// mutaciones del almacén (que invalidan por prefijo de ruta) las refrescan solas. Pulso es la pantalla de inicio: un 403
// no redirige (`handleAccessDenied: false`); la tarjeta muestra '—'.
// ---------------------------------------------------------------------------------------------------------------------

/** Tipos de tarea de almacén con tarjeta propia en Pulso (códigos de WarehouseTaskType). */
export const PULSE_TASK_TYPES = ['PUTAWAY', 'REPLENISH', 'COUNT', 'CROSSDOCK'] as const
export type PulseTaskType = (typeof PULSE_TASK_TYPES)[number]

const NO_REDIRECT = { handleAccessDenied: false } as const

/**
 * Datos del panel 'Almacén' de Pulso (`inventory.view` + WMS_LOTSERIAL; `enabled` en false no consulta nada):
 * - `balances`: `GET /api/v1/inventory/balances?includeZero=false&take=1` → `totalOnHand` / `totalAvailable`.
 * - `openReceipts`: `GET /api/v1/receipts?status=OPEN&take=1` → `total`.
 * - `pendingTasks`: `GET /api/v1/warehouse-tasks?includeClosed=false&take=1` → `total`; `tasksByType`: lo mismo con
 *   `types=<tipo>` por cada tipo de `PULSE_TASK_TYPES` (en ese orden).
 * - `openCounts`: `GET /api/v1/cycle-counts?status=OPEN` → largo del arreglo.
 */
export function useWarehousePulse(enabled: boolean) {
  const balancesQuery = { includeZero: false, take: 1 }
  const balances = useQuery({
    queryKey: ['/api/v1/inventory/balances', balancesQuery],
    queryFn: () => unwrap(api.GET('/api/v1/inventory/balances', { params: { query: balancesQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  const receiptsQuery = { status: ['OPEN'], take: 1 }
  const openReceipts = useQuery({
    queryKey: ['/api/v1/receipts', receiptsQuery],
    queryFn: () => unwrap(api.GET('/api/v1/receipts', { params: { query: receiptsQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  const tasksQuery = { includeClosed: false, take: 1 }
  const pendingTasks = useQuery({
    queryKey: ['/api/v1/warehouse-tasks', tasksQuery],
    queryFn: () => unwrap(api.GET('/api/v1/warehouse-tasks', { params: { query: tasksQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  const tasksByType = useQueries({
    queries: PULSE_TASK_TYPES.map((type) => {
      const query = { includeClosed: false, take: 1, types: [type] }
      return {
        queryKey: ['/api/v1/warehouse-tasks', query],
        queryFn: () => unwrap(api.GET('/api/v1/warehouse-tasks', { params: { query } })),
        enabled,
        meta: NO_REDIRECT,
      }
    }),
  })

  const countsQuery = { status: ['OPEN'] }
  const openCounts = useQuery({
    queryKey: ['/api/v1/cycle-counts', countsQuery],
    queryFn: () => unwrap(api.GET('/api/v1/cycle-counts', { params: { query: countsQuery } })),
    enabled,
    meta: NO_REDIRECT,
  })

  return { balances, openReceipts, pendingTasks, tasksByType, openCounts }
}
