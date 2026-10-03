// Lecturas y escrituras de "Seguridad y auditoría" (/system/audit, lote F10) sobre el cliente generado: la Actividad unificada
// (`GET /audit/activity`, paginada en el servidor), su exportación completa con los mismos filtros, las sesiones activas de
// TODA la compañía (`GET /audit/sessions`) y revocarlas. La política (MFA, reautenticación y duración de las sesiones) se lee
// y guarda con `useTenantSettings` / `useSaveTenantSettings` (Ajustes de la compañía).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import type { activityQuery } from './audit/auditView'

export type ActivityQuery = ReturnType<typeof activityQuery>

export const auditKeys = {
  activity: ['/api/v1/audit/activity'] as const,
  sessions: ['/api/v1/audit/sessions'] as const,
}

/** `GET /api/v1/audit/activity` (admin.audit): AuditLog y SecurityEvent en una lista, lo más reciente primero. */
export function useActivity(query: ActivityQuery) {
  return useQuery({
    queryKey: [...auditKeys.activity, query],
    queryFn: () => unwrap(api.GET('/api/v1/audit/activity', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

/** Toda la actividad de la consulta (sin la página): la usan "Exportar CSV" y el Exportar del pie de la tabla. */
export function fetchAllActivity(query: ActivityQuery) {
  return fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/audit/activity', { params: { query: { ...query, skip, take } } })))
}

/** `GET /api/v1/audit/sessions` (admin.audit): sesiones activas de todos los usuarios de la compañía, la propia marcada. */
export function useCompanySessions() {
  return useQuery({
    queryKey: auditKeys.sessions,
    queryFn: () => unwrap(api.GET('/api/v1/audit/sessions')),
  })
}

/** `DELETE /api/v1/audit/sessions/{id}` (admin.users): revoca una sesión; invalida la lista y la actividad (TOKEN_REVOKED). */
export function useRevokeCompanySession() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/audit/sessions/{id}', { params: { path: { id } } })),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: auditKeys.sessions })
      void qc.invalidateQueries({ queryKey: auditKeys.activity })
    },
  })
}

/**
 * `POST /api/v1/audit/sessions/revoke-others` (admin.users + AAL2: el cliente abre la reautenticación solo ante 403
 * aal2_required y reintenta): cierra todas las sesiones de la compañía salvo la propia → `{ revoked }`.
 */
export function useRevokeOtherSessions() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/audit/sessions/revoke-others')),
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: auditKeys.sessions })
      void qc.invalidateQueries({ queryKey: auditKeys.activity })
    },
  })
}
