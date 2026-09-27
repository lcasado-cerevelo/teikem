// Datos de "Pulso del día" (P3): un solo GET de solo lectura, sin mutaciones.
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'

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
