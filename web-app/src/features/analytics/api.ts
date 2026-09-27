// Datos de "Pulso del día" (P3): el GET de Pulso y la preferencia de rango de fecha de cada usuario por tarjeta.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
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
