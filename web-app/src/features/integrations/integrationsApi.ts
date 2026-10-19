// Datos de «Integraciones» (Lote 132, P11; módulo INTEGRATIONS): lista, alta y edición, apagar/prender, correr ahora, subir un archivo, historial y la bandeja de lo que no se pudo procesar.
// Permisos: integrations.view / .create / .manage / .inbox. Mientras una integración corre la lista se vuelve a pedir cada 5 s.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type IntegrationDto = Schemas['IntegrationDto']
export type IntegrationCreateRequest = Schemas['IntegrationCreateRequest']
export type IntegrationPatchRequest = Schemas['IntegrationPatchRequest']
export type IntegrationOptionsDto = Schemas['IntegrationOptionsDto']
export type IntegrationRunDto = Schemas['IntegrationRunDto']
export type InboxItemDto = Schemas['InboxItemDto']
export type InboxPageDto = Schemas['InboxPageDto']
export type InboxRowDto = Schemas['InboxRowDto']

export const integrationKeys = {
  all: ['integrations'] as const,
  list: (includeInactive: boolean) => ['integrations', '/api/v1/integrations', { includeInactive }] as const,
  options: ['integrations', '/api/v1/integrations/options'] as const,
  runs: (id: string) => ['integrations', '/api/v1/integrations/{publicId}/runs', id] as const,
  inbox: (integration: string, status: string, skip: number, take: number) => ['integrations', 'inbox', { integration, status, skip, take }] as const,
  item: (id: number) => ['integrations', 'inbox', 'item', id] as const,
}

export function useIntegrations(includeInactive: boolean) {
  return useQuery({
    queryKey: integrationKeys.list(includeInactive),
    queryFn: () => unwrap(api.GET('/api/v1/integrations', { params: { query: { includeInactive } } })),
    refetchInterval: (q) => (q.state.data?.some((i) => i.lastStatus === 'RUNNING') ? 5000 : false),
  })
}

export function useIntegrationOptions(enabled: boolean) {
  return useQuery({
    queryKey: integrationKeys.options,
    queryFn: () => unwrap(api.GET('/api/v1/integrations/options')),
    enabled,
    staleTime: 60_000,
    meta: { handleAccessDenied: false },
  })
}

export function useIntegrationRuns(publicId: string | null) {
  return useQuery({
    queryKey: integrationKeys.runs(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/{publicId}/runs', { params: { path: { publicId: publicId! }, query: { take: 30 } } })),
    enabled: publicId !== null,
  })
}

export function useIntegrationMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: integrationKeys.all })
  return {
    create: useMutation({
      mutationFn: (body: IntegrationCreateRequest) => unwrap(api.POST('/api/v1/integrations', { body })),
      onSettled: invalidate,
    }),
    update: useMutation({
      mutationFn: (v: { publicId: string; body: IntegrationPatchRequest }) => unwrap(api.PATCH('/api/v1/integrations/{publicId}', { params: { path: { publicId: v.publicId } }, body: v.body })),
      onSettled: invalidate,
    }),
    setActive: useMutation({
      mutationFn: (v: { publicId: string; active: boolean }) =>
        unwrap(
          v.active
            ? api.POST('/api/v1/integrations/{publicId}/reactivate', { params: { path: { publicId: v.publicId } } })
            : api.POST('/api/v1/integrations/{publicId}/deactivate', { params: { path: { publicId: v.publicId } } }),
        ),
      onSettled: invalidate,
    }),
    run: useMutation({
      mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/run', { params: { path: { publicId } } })),
      onSettled: invalidate,
    }),
    upload: useMutation({
      mutationFn: (v: { publicId: string; file: File }) => {
        const body = new FormData()
        body.append('file', v.file)
        return unwrap(api.POST('/api/v1/integrations/{publicId}/upload', { params: { path: { publicId: v.publicId } }, body: body as never }))
      },
      onSettled: invalidate,
    }),
  }
}

// ---------------------------------------------------------------- bandeja

export function useInbox(integration: string, status: string, skip: number, take: number) {
  return useQuery({
    queryKey: integrationKeys.inbox(integration, status, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/inbox', { params: { query: { integration: integration || undefined, status: status || undefined, skip, take } } })),
  })
}

export function useInboxItem(id: number | null) {
  return useQuery({
    queryKey: integrationKeys.item(id ?? 0),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/inbox/{id}', { params: { path: { id: id! } } })),
    enabled: id !== null,
  })
}

export function useInboxMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: integrationKeys.all })
  return {
    fix: useMutation({
      mutationFn: (v: { id: number; rows: InboxRowDto[] }) => unwrap(api.PATCH('/api/v1/integrations/inbox/{id}', { params: { path: { id: v.id } }, body: { rows: v.rows } })),
      onSettled: invalidate,
    }),
    reprocess: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/integrations/inbox/{id}/reprocess', { params: { path: { id } } })),
      onSettled: invalidate,
    }),
    reprocessAll: useMutation({
      mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/inbox/reprocess-all', { params: { path: { publicId } } })),
      onSettled: invalidate,
    }),
    discard: useMutation({
      mutationFn: (v: { id: number; note: string }) => unwrap(api.POST('/api/v1/integrations/inbox/{id}/discard', { params: { path: { id: v.id } }, body: { note: v.note || null } })),
      onSettled: invalidate,
    }),
  }
}

/** Descarga el CSV de los pendientes (lo pide al API con la sesión y lo baja con un enlace temporal). */
export async function downloadInboxCsv(integration: string): Promise<void> {
  const res = await api.GET('/api/v1/integrations/inbox/export', { params: { query: { integration: integration || undefined } }, parseAs: 'blob' })
  if (res.error !== undefined || !res.response.ok) throw new Error('export')
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'bandeja-integraciones.csv'
  a.click()
  URL.revokeObjectURL(url)
}
