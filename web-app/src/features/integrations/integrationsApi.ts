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
export type ApiKeyDto = Schemas['ApiKeyDto']
export type ApiKeyCreatedDto = Schemas['ApiKeyCreatedDto']
export type ApiLogDto = Schemas['ApiLogDto']
export type FormatPreviewDto = Schemas['FormatPreviewDto']
export type FileFormatConfig = Schemas['FileFormatConfig']
export type OutputItemDto = Schemas['OutputItemDto']
export type OutputPreviewDto = Schemas['OutputPreviewDto']
export type WebhookDto = Schemas['WebhookDto']
export type WebhookCreatedDto = Schemas['WebhookCreatedDto']
export type WebhookDeliveryDto = Schemas['WebhookDeliveryDto']
export type ProposalDto = Schemas['ProposalDto']
export type ProposalPageDto = Schemas['ProposalPageDto']

export const integrationKeys = {
  all: ['integrations'] as const,
  list: (includeInactive: boolean) => ['integrations', '/api/v1/integrations', { includeInactive }] as const,
  options: ['integrations', '/api/v1/integrations/options'] as const,
  runs: (id: string) => ['integrations', '/api/v1/integrations/{publicId}/runs', id] as const,
  inbox: (integration: string, status: string, skip: number, take: number) => ['integrations', 'inbox', { integration, status, skip, take }] as const,
  apiKeys: (id: string) => ['integrations', 'api-keys', id] as const,
  apiLog: (id: string, key: string) => ['integrations', 'api-log', id, key] as const,
  webhooks: (id: string) => ['integrations', 'webhooks', id] as const,
  outbox: (integration: string, status: string, skip: number, take: number) => ['integrations', 'outbox', { integration, status, skip, take }] as const,
  outputs: (integration: string, status: string, skip: number, take: number) => ['integrations', 'outputs', { integration, status, skip, take }] as const,
  item: (id: number) => ['integrations', 'inbox', 'item', id] as const,
  proposals: (status: string, skip: number, take: number) => ['integrations', 'proposals', { status, skip, take }] as const,
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
    /** 2026-10-21: salida de un catálogo: anota TODOS los registros y corre la integración ahora. */
    sendAll: useMutation({
      mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/send-all', { params: { path: { publicId } } })),
      onSettled: invalidate,
    }),
    testConnection: useMutation({
      mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/test-connection', { params: { path: { publicId } } })),
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
    /** Descarta todos los pendientes (de una integración si se da, o de todas); nunca borra. */
    discardAll: useMutation({
      mutationFn: (v: { integration: string; note: string }) => unwrap(api.POST('/api/v1/integrations/inbox/discard-all', { body: { integration: v.integration || null, note: v.note || null } })),
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

// ---------------------------------------------------------------- llaves de API (Lote 135)

export function useApiKeys(publicId: string | null) {
  return useQuery({
    queryKey: integrationKeys.apiKeys(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/{publicId}/api-keys', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useApiKeyLog(publicId: string, keyId: string | null) {
  return useQuery({
    queryKey: integrationKeys.apiLog(publicId, keyId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/{publicId}/api-keys/{keyId}/log', { params: { path: { publicId, keyId: keyId! }, query: { take: 50 } } })),
    enabled: keyId !== null,
  })
}

export function useApiKeyMutations(publicId: string) {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: integrationKeys.all })
  return {
    create: useMutation({
      mutationFn: (body: { label: string; rateLimitPerMinute?: number | null }) => unwrap(api.POST('/api/v1/integrations/{publicId}/api-keys', { params: { path: { publicId } }, body })),
      onSettled: invalidate,
    }),
    update: useMutation({
      mutationFn: (v: { keyId: string; label?: string; rateLimitPerMinute?: number }) =>
        unwrap(api.PATCH('/api/v1/integrations/{publicId}/api-keys/{keyId}', { params: { path: { publicId, keyId: v.keyId } }, body: { label: v.label, rateLimitPerMinute: v.rateLimitPerMinute } })),
      onSettled: invalidate,
    }),
    revoke: useMutation({
      mutationFn: (keyId: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/api-keys/{keyId}/revoke', { params: { path: { publicId, keyId } } })),
      onSettled: invalidate,
    }),
  }
}

// ---------------------------------------------------------------- webhooks de salida y su bandeja (Lote 136)

export function useWebhooks(publicId: string | null) {
  return useQuery({
    queryKey: integrationKeys.webhooks(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/{publicId}/webhooks', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useWebhookMutations(publicId: string) {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: integrationKeys.all })
  const path = (id: string) => ({ params: { path: { publicId, webhookId: id } } })
  return {
    create: useMutation({
      mutationFn: (body: { url: string; events: string[] }) => unwrap(api.POST('/api/v1/integrations/{publicId}/webhooks', { params: { path: { publicId } }, body })),
      onSettled: invalidate,
    }),
    update: useMutation({
      mutationFn: (v: { id: string; url?: string; events?: string[]; isActive?: boolean }) =>
        unwrap(api.PATCH('/api/v1/integrations/{publicId}/webhooks/{webhookId}', { ...path(v.id), body: { url: v.url, events: v.events, isActive: v.isActive } })),
      onSettled: invalidate,
    }),
    remove: useMutation({
      mutationFn: (id: string) => unwrap(api.DELETE('/api/v1/integrations/{publicId}/webhooks/{webhookId}', path(id))),
      onSettled: invalidate,
    }),
    resume: useMutation({
      mutationFn: (id: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/webhooks/{webhookId}/resume', path(id))),
      onSettled: invalidate,
    }),
    rotate: useMutation({
      mutationFn: (id: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/webhooks/{webhookId}/rotate-secret', path(id))),
      onSettled: invalidate,
    }),
    test: useMutation({
      mutationFn: (id: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/webhooks/{webhookId}/test', path(id))),
    }),
  }
}

/** Propuestas de cierre de órdenes que dejaron los lectores por documento (2026-10-23). */
export function useProposals(status: string, skip: number, take: number) {
  return useQuery({
    queryKey: integrationKeys.proposals(status, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/proposals', { params: { query: { status: status || undefined, skip, take } } })),
  })
}

export function useProposalMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: integrationKeys.all })
  return {
    confirm: useMutation({
      mutationFn: (v: { id: string; note: string }) => unwrap(api.POST('/api/v1/integrations/proposals/{publicId}/confirm', { params: { path: { publicId: v.id } }, body: { note: v.note || null } })),
      onSettled: invalidate,
    }),
    reject: useMutation({
      mutationFn: (v: { id: string; note: string }) => unwrap(api.POST('/api/v1/integrations/proposals/{publicId}/reject', { params: { path: { publicId: v.id } }, body: { note: v.note || null } })),
      onSettled: invalidate,
    }),
  }
}

export function useOutbox(integration: string, status: string, skip: number, take: number) {
  return useQuery({
    queryKey: integrationKeys.outbox(integration, status, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/outbox', { params: { query: { integration: integration || undefined, status: status || undefined, skip, take } } })),
  })
}

export function useOutboxMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: integrationKeys.all })
  return {
    retry: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/integrations/outbox/{id}/retry', { params: { path: { id } } })),
      onSettled: invalidate,
    }),
    discard: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/integrations/outbox/{id}/discard', { params: { path: { id } } })),
      onSettled: invalidate,
    }),
  }
}

// ---------------------------------------------------------------- formato del archivo (Lote 137)

/** Vista previa de un archivo de muestra con el formato que se está configurando (no guarda nada). */
export function usePreviewFormat() {
  return useMutation({
    mutationFn: (v: { file: File; config: FileFormatConfig; delimiter?: string; hasHeader?: boolean }) => {
      const body = new FormData()
      body.append('file', v.file)
      body.append('format', JSON.stringify(v.config))
      if (v.delimiter) body.append('delimiter', v.delimiter)
      if (v.hasHeader !== undefined) body.append('hasHeader', String(v.hasHeader))
      return unwrap(api.POST('/api/v1/integrations/format/preview', { body: body as never }))
    },
  })
}

/** Lee un PDF de muestra (texto real u OCR) y devuelve el texto como lo vería un lector: base del asistente de lectura. No guarda nada. */
export function useReadSample() {
  return useMutation({
    mutationFn: (file: File) => {
      const body = new FormData()
      body.append('file', file)
      return unwrap(api.POST('/api/v1/integrations/read-sample', { body: body as never }))
    },
  })
}

// ---------------------------------------------------------------- integraciones de salida (Lote 137b)

/** Arma un ejemplo de la salida con órdenes recientes del cliente, sin mandar nada. */
export function usePreviewOutput() {
  return useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/integrations/{publicId}/output/preview', { params: { path: { publicId } } })) })
}

export function useOutputs(integration: string, status: string, skip: number, take: number) {
  return useQuery({
    queryKey: integrationKeys.outputs(integration, status, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/integrations/outputs', { params: { query: { integration: integration || undefined, status: status || undefined, skip, take } } })),
  })
}

export function useOutputMutations() {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: integrationKeys.all })
  return {
    retry: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/integrations/outputs/{id}/retry', { params: { path: { id } } })),
      onSettled: invalidate,
    }),
    discard: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/integrations/outputs/{id}/discard', { params: { path: { id } } })),
      onSettled: invalidate,
    }),
  }
}
