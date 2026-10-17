// Lote 88 (P12) — datos de los ríos del Pulso: GET /api/v1/pulse/rivers/{orders|cod}. Son fotos de ahora: staleTime 30 s y se vuelven a pedir cada minuto. Un 403 (sin el permiso o el módulo) no redirige:
// el panel solo se pinta si el Pulso lo trae, y si falla muestra el error en su lugar.
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import type { RiverKind } from './riverRules'

export type PulseRiverDto = components['schemas']['PulseRiverDto']
export type PulseRiverNodeDto = components['schemas']['PulseRiverNodeDto']
export type PulseRiverValueDto = components['schemas']['PulseRiverValueDto']

export function useRiver(kind: RiverKind) {
  return useQuery({
    queryKey: ['/api/v1/pulse/rivers', kind],
    queryFn: () => unwrap(kind === 'orders' ? api.GET('/api/v1/pulse/rivers/orders') : api.GET('/api/v1/pulse/rivers/cod')),
    staleTime: 30 * 1000,
    refetchInterval: 60 * 1000,
    meta: { handleAccessDenied: false },
  })
}
