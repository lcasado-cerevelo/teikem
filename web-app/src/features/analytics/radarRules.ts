// Lote 89 (P12) — reglas puras del Radar: a dónde lleva cada grupo (Órdenes ya filtrado con lo abierto) y el enlace de «Ver todas». Sin React.
import type { RadarGroupBy } from './radarApi'

/** Órdenes filtrada por un grupo del Radar: el pueblo (`city`) o el cliente (`client` = publicId), siempre solo con los estatus abiertos. */
export function radarGroupHref(groupBy: RadarGroupBy, key: string, openStatuses: readonly string[]): string {
  const params = new URLSearchParams()
  params.set(groupBy === 'TOWN' ? 'city' : 'client', key)
  if (openStatuses.length > 0) params.set('statuses', openStatuses.join(','))
  return `/orders?${params.toString()}`
}

/** «Ver todas en Órdenes»: la lista de Órdenes con solo los estatus abiertos. */
export function radarOpenHref(openStatuses: readonly string[]): string {
  return openStatuses.length > 0 ? `/orders?${new URLSearchParams({ statuses: openStatuses.join(',') }).toString()}` : '/orders'
}
