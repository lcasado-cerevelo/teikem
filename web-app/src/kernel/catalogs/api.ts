// Lecturas de catálogos y estatus (TanStack Query sobre el cliente generado).
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../api/client'
import { toLookupOption, toStatusOption, type LookupOption, type StatusOption } from './types'

/** Los catálogos cambian poco: se reusan 10 minutos (cambiar de idioma invalida y vuelve a pedir). */
export const CATALOG_STALE_MS = 10 * 60_000

export const catalogKeys = {
  lookups: (domain: string, includeDisabled: boolean) => ['/api/v1/catalogs/{entity}', { domain, includeDisabled }] as const,
  statuses: (domain: string, includeDisabled: boolean) => ['/api/v1/status/{entity}', { domain, includeDisabled }] as const,
  validate: (domain: string) => ['/api/v1/status/{entity}/validate', { domain }] as const,
  lateralEntries: (entityType: string) => ['/api/v1/status/lateral-entries/{entityType}', { entityType }] as const,
  history: (entityType: string, entityId: number) => ['/api/v1/status/history/{entityType}/{entityId}', { entityType, entityId }] as const,
}

export interface CatalogQueryOptions {
  /** Incluir valores deshabilitados (para mostrar valores históricos; no para capturar). */
  includeDisabled?: boolean
  /** Desactivar la consulta (p. ej. mientras no se sabe el dominio). */
  enabled?: boolean
}

/** Valores de un dominio de catálogo (`GET /api/v1/catalogs/{entity}`) como opciones `{ code, label }`. */
export function useLookups(domain: string | null | undefined, options: CatalogQueryOptions = {}) {
  const includeDisabled = options.includeDisabled ?? false
  return useQuery({
    queryKey: catalogKeys.lookups(domain ?? '', includeDisabled),
    queryFn: async (): Promise<LookupOption[]> => {
      const rows = await unwrap(
        api.GET('/api/v1/catalogs/{entity}', { params: { path: { entity: domain ?? '' }, query: { includeDisabled } } }),
      )
      return rows.map(toLookupOption).sort((a, b) => a.sortOrder - b.sortOrder)
    },
    enabled: !!domain && (options.enabled ?? true),
    staleTime: CATALOG_STALE_MS,
    meta: { handleAccessDenied: false },
  })
}

/** Etapas de un dominio de estatus (`GET /api/v1/status/{entity}`) ordenadas por SortOrder del tenant. */
export function useStatuses(domain: string | null | undefined, options: CatalogQueryOptions = {}) {
  const includeDisabled = options.includeDisabled ?? false
  return useQuery({
    queryKey: catalogKeys.statuses(domain ?? '', includeDisabled),
    queryFn: async (): Promise<StatusOption[]> => {
      const rows = await unwrap(
        api.GET('/api/v1/status/{entity}', { params: { path: { entity: domain ?? '' }, query: { includeDisabled } } }),
      )
      return rows.map(toStatusOption).sort((a, b) => a.sortOrder - b.sortOrder)
    },
    enabled: !!domain && (options.enabled ?? true),
    staleTime: CATALOG_STALE_MS,
    meta: { handleAccessDenied: false },
  })
}

/** Resultado del validador del pipeline del tenant (`GET /api/v1/status/{entity}/validate`). */
export function usePipelineValidation(domain: string | null | undefined, enabled = true) {
  return useQuery({
    queryKey: catalogKeys.validate(domain ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/status/{entity}/validate', { params: { path: { entity: domain ?? '' } } })),
    enabled: !!domain && enabled,
    staleTime: CATALOG_STALE_MS,
    meta: { handleAccessDenied: false },
  })
}

/** Reglas de entrada lateral efectivas del tipo de entidad (`GET /api/v1/status/lateral-entries/{entityType}`). */
export function useLateralEntries(entityType: string | null | undefined, enabled = true) {
  return useQuery({
    queryKey: catalogKeys.lateralEntries(entityType ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/status/lateral-entries/{entityType}', { params: { path: { entityType: entityType ?? '' } } })),
    enabled: !!entityType && enabled,
    staleTime: CATALOG_STALE_MS,
    meta: { handleAccessDenied: false },
  })
}

/** Historial de estatus de un registro (`GET /api/v1/status/history/{entityType}/{entityId}`), del más antiguo al último. */
export function useStatusHistory(entityType: string | null | undefined, entityId: number | null | undefined, enabled = true) {
  return useQuery({
    queryKey: catalogKeys.history(entityType ?? '', entityId ?? 0),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/status/history/{entityType}/{entityId}', {
          params: { path: { entityType: entityType ?? '', entityId: entityId ?? 0 } },
        }),
      ),
    enabled: !!entityType && typeof entityId === 'number' && entityId > 0 && enabled,
    meta: { handleAccessDenied: false },
  })
}
