// Lote F-F2 — datos de «Vistas e informes»: lista de vistas (`GET /analytics/reports`), correr una vista con rango de fecha,
// vista previa del constructor y alta/edición/baja. La lista y la corrida de Rentas (`rentals/api.ts`) tienen otras claves
// (`[ruta, 'rentals']`, `[ruta, id]`): aquí cuelgan del mismo prefijo, así que guardar o eliminar las refresca también.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { ReportRunRequest, ReportUpsertRequest } from './viewRules'

export const REPORTS_KEY = '/api/v1/analytics/reports'
export const REPORT_RUN_KEY = '/api/v1/analytics/reports/{id}/run'

/** Todas las vistas que el usuario puede ver (el servidor ya filtra por visibilidad y por fuente legible). */
export function useReports() {
  return useQuery({
    queryKey: [REPORTS_KEY, 'all'],
    queryFn: () => unwrap(api.GET('/api/v1/analytics/reports')),
    meta: { handleAccessDenied: false },
  })
}

/** Corre una vista guardada; `body` `null` = configuración incompleta (rango personalizado sin fechas): no consulta. */
export function useRunReport(id: number | null | undefined, body: ReportRunRequest | null) {
  return useQuery({
    queryKey: [REPORT_RUN_KEY, id, 'view', body],
    queryFn: () => unwrap(api.POST('/api/v1/analytics/reports/{id}/run', { params: { path: { id: id as number } }, body: body as ReportRunRequest })),
    enabled: id != null && body != null,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

/** Vista previa del constructor (`POST /reports/{fuente}/preview`, `analytics.view`): no guarda nada. */
export function usePreviewReport(source: string, body: ReportUpsertRequest | null, take: number) {
  return useQuery({
    queryKey: ['/api/v1/analytics/reports/{baseEntityType}/preview', source, body, take],
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/analytics/reports/{baseEntityType}/preview', {
          params: { path: { baseEntityType: source }, query: { dateRangeMode: 'ALL', take } },
          body: body as ReportUpsertRequest,
        }),
      ),
    enabled: body != null && source !== '',
    placeholderData: keepPreviousData,
    staleTime: 15 * 1000,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

function useInvalidateReports() {
  const qc = useQueryClient()
  return () => {
    void qc.invalidateQueries({ queryKey: [REPORTS_KEY] })
    void qc.invalidateQueries({ queryKey: [REPORT_RUN_KEY] })
  }
}

/** Alta (`POST /reports/{fuente}`) o edición (`PUT /reports/{id}`, la fuente no cambia). */
export function useSaveReport() {
  const invalidate = useInvalidateReports()
  return useMutation({
    mutationFn: ({ id, source, body }: { id: number | null; source: string; body: ReportUpsertRequest }) =>
      id != null
        ? unwrap(api.PUT('/api/v1/analytics/reports/{id}', { params: { path: { id } }, body }))
        : unwrap(api.POST('/api/v1/analytics/reports/{baseEntityType}', { params: { path: { baseEntityType: source } }, body })),
    onSuccess: invalidate,
  })
}

/** Baja lógica (`DELETE /reports/{id}`, 204). */
export function useDeleteReport() {
  const invalidate = useInvalidateReports()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/analytics/reports/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  })
}
