// Datos de «COD» (Lote 85, módulo COD): órdenes con COD, cobro, reconciliación por códigos y corridas.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type CodOrderListItemDto = Schemas['CodOrderListItemDto']
export type CodOrderDto = Schemas['CodOrderDto']
export type CodCollectionDto = Schemas['CodCollectionDto']
export type CodCollectionRequest = Schemas['CodCollectionRequest']
export type CodReconcileRequest = Schemas['CodReconcileRequest']
export type CodReconcileResultDto = Schemas['CodReconcileResultDto']
export type CodReconcileItemDto = Schemas['CodReconcileItemDto']
export type CodRunListItemDto = Schemas['CodRunListItemDto']
export type CodRunDto = Schemas['CodRunDto']
export type CodRemittanceDto = Schemas['CodRemittanceDto']
export type CodRemittanceListItemDto = Schemas['CodRemittanceListItemDto']
export type CodRemittanceLineDto = Schemas['CodRemittanceLineDto']
export type CodRemittanceCandidatesDto = Schemas['CodRemittanceCandidatesDto']
export type CodRemittanceRequest = Schemas['CodRemittanceRequest']
export type CodSettingsDto = Schemas['CodSettingsDto']
export type ExportFileDto = Schemas['ExportFileDto']
export type ExportTemplateDto = Schemas['ExportTemplateDto']

export const codKeys = {
  all: ['cod'] as const,
  orders: (status: string, q: string, skip: number, take: number) => ['cod', 'orders', status, q, skip, take] as const,
  order: (id: string | null) => ['cod', 'order', id] as const,
  runs: (skip: number, take: number) => ['cod', 'runs', skip, take] as const,
  run: (id: string | null) => ['cod', 'run', id] as const,
  remittances: (status: string, q: string, skip: number, take: number) => ['cod', 'remittances', status, q, skip, take] as const,
  remittance: (id: string | null) => ['cod', 'remittance', id] as const,
  candidates: (client: string | null, from: string, to: string, pct: string) => ['cod', 'candidates', client, from, to, pct] as const,
  settings: ['cod', 'settings'] as const,
  templates: ['cod', 'export-templates'] as const,
}

export function useCodOrders(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: codKeys.orders(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/cod/orders', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function codOrderPageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/cod/orders', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } }))
}

export function useCodOrder(publicId: string | null) {
  return useQuery({
    queryKey: codKeys.order(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/cod/orders/{orderPublicId}', { params: { path: { orderPublicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useCodRuns(skip: number, take: number) {
  return useQuery({
    queryKey: codKeys.runs(skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/cod/runs', { params: { query: { skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

export function useCodRun(publicId: string | null) {
  return useQuery({
    queryKey: codKeys.run(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/cod/runs/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useCodMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: codKeys.all })
  return {
    collect: useMutation({
      mutationFn: (v: { id: string; body: CodCollectionRequest }) => unwrap(api.POST('/api/v1/cod/orders/{orderPublicId}/collections', { params: { path: { orderPublicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    /** Con `dryRun` solo simula (no cambia nada); sin él cuadra y guarda la corrida. */
    reconcile: useMutation({
      mutationFn: (body: CodReconcileRequest) => unwrap(api.POST('/api/v1/cod/reconcile', { body })),
      onSuccess: (_data, vars) => {
        if (vars.dryRun !== true) void refresh()
      },
    }),
  }
}

// ---------------------------------------------------------------- remesas (Lote 87)

export function useRemittances(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: codKeys.remittances(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/cod/remittances', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function remittancePageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/cod/remittances', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } }))
}

export function useRemittance(publicId: string | null) {
  return useQuery({
    queryKey: codKeys.remittance(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/cod/remittances/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

/** Lo que entraría en la remesa (sin crearla): solo con cliente y período válidos. */
export function useCandidates(client: string | null, from: string, to: string, pct: number | null, enabled: boolean) {
  return useQuery({
    queryKey: codKeys.candidates(client, from, to, pct === null ? '' : String(pct)),
    queryFn: () => unwrap(api.GET('/api/v1/cod/remittances/candidates', { params: { query: { clientPublicId: client!, periodStart: from, periodEnd: to, commissionPct: pct ?? undefined } } })),
    enabled: enabled && client !== null,
    gcTime: 0,
  })
}

export function useCodSettings() {
  return useQuery({ queryKey: codKeys.settings, queryFn: () => unwrap(api.GET('/api/v1/cod/settings')) })
}

/** Plantillas de exportación de remesas: el API las pide con los permisos de facturación; sin ellos o sin el módulo, la lista no carga y se usa la plantilla estándar. */
export function useRemittanceTemplates(enabled: boolean) {
  return useQuery({
    queryKey: codKeys.templates,
    queryFn: () => unwrap(api.GET('/api/v1/export-templates', { params: { query: { scope: 'COD_REMITTANCE' } } })),
    enabled,
    retry: false,
  })
}

export function useRemittanceMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: codKeys.all })
  return {
    create: useMutation({ mutationFn: (body: CodRemittanceRequest) => unwrap(api.POST('/api/v1/cod/remittances', { body })), onSettled: refresh }),
    review: useMutation({ mutationFn: (id: string) => unwrap(api.POST('/api/v1/cod/remittances/{publicId}/review', { params: { path: { publicId: id } } })), onSettled: refresh }),
    // aprobar lleva [RequireAal2]: el cliente del API abre solo el modal de reautenticación al recibir 403 aal2_required y reintenta
    approve: useMutation({ mutationFn: (id: string) => unwrap(api.POST('/api/v1/cod/remittances/{publicId}/approve', { params: { path: { publicId: id } } })), onSettled: refresh }),
    cancel: useMutation({ mutationFn: (id: string) => unwrap(api.POST('/api/v1/cod/remittances/{publicId}/cancel', { params: { path: { publicId: id } } })), onSettled: refresh }),
    remit: useMutation({
      mutationFn: (v: { id: string; exportRef?: string }) => unwrap(api.POST('/api/v1/cod/remittances/{publicId}/remit', { params: { path: { publicId: v.id } }, body: { exportRef: v.exportRef } })),
      onSettled: refresh,
    }),
    updateSettings: useMutation({
      mutationFn: (pct: number) => unwrap(api.PUT('/api/v1/cod/settings', { body: { defaultCommissionPct: pct } })),
      onSuccess: (data) => qc.setQueryData(codKeys.settings, data),
    }),
  }
}

/** El archivo de la remesa (una fila por cobro) con la plantilla elegida o la primera activa. */
export function exportRemittance(id: string, templatePublicId: string | null) {
  return unwrap(api.GET('/api/v1/cod/remittances/{publicId}/export', { params: { path: { publicId: id }, query: { templatePublicId: templatePublicId ?? undefined } } }))
}
