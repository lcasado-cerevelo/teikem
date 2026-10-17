// Lote 89 (P12) — datos del Radar de órdenes abiertas del Pulso: GET /api/v1/pulse/radar (grupos) y el listado de Órdenes con `search` y los estatus abiertos (la búsqueda de una orden).
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type PulseRadarDto = components['schemas']['PulseRadarDto']
export type PulseRadarGroupDto = components['schemas']['PulseRadarGroupDto']
export type RadarGroupBy = 'TOWN' | 'CLIENT'
export const RADAR_MIN_SEARCH = 2
export const RADAR_HITS = 6

export function useRadar(groupBy: RadarGroupBy) {
  return useQuery({
    queryKey: ['/api/v1/pulse/radar', groupBy],
    queryFn: () => unwrap(api.GET('/api/v1/pulse/radar', { params: { query: { groupBy, take: 6 } } })),
    staleTime: 30 * 1000,
    refetchInterval: 60 * 1000,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

/** Busca entre las órdenes abiertas por factura, número, empaque o consignatario (el buscador libre del listado de Órdenes). */
export function useRadarSearch(text: string, openStatuses: readonly string[]) {
  const q = text.trim()
  return useQuery({
    queryKey: ['/api/v1/orders', 'radar', q, openStatuses],
    queryFn: () => unwrap(api.GET('/api/v1/orders', { params: { query: { search: q, statuses: [...openStatuses], take: RADAR_HITS } } })),
    enabled: q.length >= RADAR_MIN_SEARCH && openStatuses.length > 0,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}
