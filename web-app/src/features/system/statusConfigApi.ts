// Sistema → Estatus: las etapas de cada dominio de estatus (cliente, envío, ruta…) con su orden, su tipo y si la compañía las tiene encendidas (2026-10-21, pedido de Luis).
// Lee `GET /api/v1/status/{entity}?includeDisabled=true` y valida con `GET …/validate`; escribe `PUT /api/v1/status/{entity}/{code}/override` (admin.statusconfig): habilitar o apagar, orden propio y nombre.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type StatusStage = components['schemas']['StatusDto']

export const statusConfigKeys = {
  list: (domain: string) => ['status-config', domain] as const,
  validate: (domain: string) => ['status-config', domain, 'validate'] as const,
}

export function useStatusStages(domain: string | null, enabled = true) {
  return useQuery({
    queryKey: statusConfigKeys.list(domain ?? ''),
    queryFn: async () => {
      const rows = await unwrap(api.GET('/api/v1/status/{entity}', { params: { path: { entity: domain ?? '' }, query: { includeDisabled: true } } }))
      return [...rows].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    },
    enabled: !!domain && enabled,
  })
}

export function useStatusValidation(domain: string | null, enabled = true) {
  return useQuery({
    queryKey: statusConfigKeys.validate(domain ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/status/{entity}/validate', { params: { path: { entity: domain ?? '' } } })),
    enabled: !!domain && enabled,
  })
}

export type StatusPatch = { isEnabled?: boolean; sortOverride?: number; labels?: Record<string, string> }

/** Cambia una etapa para esta compañía. El servidor valida el pipeline completo al guardar (422 con el motivo si quedaría inválido). */
export function useStatusOverride(domain: string | null) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { code: string; patch: StatusPatch }) =>
      unwrap(api.PUT('/api/v1/status/{entity}/{code}/override', { params: { path: { entity: domain ?? '', code: v.code } }, body: { labels: v.patch.labels ?? null, colorHex: null, isEnabled: v.patch.isEnabled ?? null, sortOverride: v.patch.sortOverride ?? null } })),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ['status-config'] })
      void qc.invalidateQueries({ queryKey: ['/api/v1/status/{entity}'] })   // las pantallas que leen el catálogo de estatus lo piden de nuevo
    },
  })
}

/**
 * Intercambia el orden de dos etapas vecinas: cada una toma el orden de la otra. Si tienen el mismo orden se separan (la que sube queda un paso antes). Devuelve los dos cambios [código, orden nuevo].
 */
export function swapOrder(rows: readonly { code?: string | null; sortOrder?: number }[], index: number, direction: -1 | 1): [string, number][] | null {
  const other = index + direction
  if (index < 0 || other < 0 || index >= rows.length || other >= rows.length) return null
  const a = rows[index]
  const b = rows[other]
  if (!a.code || !b.code) return null
  const sa = a.sortOrder ?? 0
  const sb = b.sortOrder ?? 0
  if (sa === sb) return direction === -1 ? [[a.code, sb - 1], [b.code, sb]] : [[a.code, sb + 1], [b.code, sb]]
  return [[a.code, sb], [b.code, sa]]
}
