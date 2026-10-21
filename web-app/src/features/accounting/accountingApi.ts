// Datos de «Contabilización de compras» (módulo de compras) y «de despachos» (módulo de almacén) — Lote 181, H1: corridas CMP-##### / DSP-#####, vista previa, flujo generar → aceptar → exportar y cancelar.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import type { AccountingKind } from './accountingRules'

type Schemas = components['schemas']
export type AccountingRunDto = Schemas['AccountingRunDto']
export type AccountingRunListItemDto = Schemas['AccountingRunListItemDto']
export type AccountingLineDto = Schemas['AccountingLineDto']
export type AccountingPreviewDto = Schemas['AccountingPreviewDto']
export type AccountingRunRequest = Schemas['AccountingRunRequest']
export type ExportFileDto = Schemas['ExportFileDto']

export const accountingKeys = {
  all: (kind: AccountingKind) => ['accounting', kind] as const,
  list: (kind: AccountingKind, status: string, q: string, skip: number, take: number) => ['accounting', kind, 'list', status, q, skip, take] as const,
  one: (kind: AccountingKind, id: string | null) => ['accounting', kind, 'one', id] as const,
  preview: (kind: AccountingKind, from: string, to: string) => ['accounting', kind, 'preview', from, to] as const,
  filePreview: (kind: AccountingKind, id: string, template: string | null) => ['accounting', kind, 'file', id, template] as const,
}

/** Una página de corridas del filtro (compras y despachos son dos controladores con la misma forma). */
function listPage(kind: AccountingKind, status: string, q: string, skip: number, take: number) {
  const query = { status: status || undefined, q: q || undefined, skip, take }
  return kind === 'PURCHASES'
    ? unwrap(api.GET('/api/v1/purchase-accounting', { params: { query } }))
    : unwrap(api.GET('/api/v1/dispatch-accounting', { params: { query } }))
}

export function useRuns(kind: AccountingKind, status: string, q: string, skip: number, take: number) {
  return useQuery({ queryKey: accountingKeys.list(kind, status, q, skip, take), queryFn: () => listPage(kind, status, q, skip, take), placeholderData: keepPreviousData })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function runPageReader(kind: AccountingKind, status: string, q: string) {
  return (skip: number, take: number) => listPage(kind, status, q, skip, take)
}

export function useRun(kind: AccountingKind, publicId: string | null) {
  return useQuery({
    queryKey: accountingKeys.one(kind, publicId),
    queryFn: () =>
      kind === 'PURCHASES'
        ? unwrap(api.GET('/api/v1/purchase-accounting/{publicId}', { params: { path: { publicId: publicId! } } }))
        : unwrap(api.GET('/api/v1/dispatch-accounting/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

/** Lo que entraría en la corrida (sin crearla): solo con un período válido. */
export function useRunPreview(kind: AccountingKind, from: string, to: string, enabled: boolean) {
  return useQuery({
    queryKey: accountingKeys.preview(kind, from, to),
    queryFn: () => {
      const body: AccountingRunRequest = { periodStart: from, periodEnd: to }
      return kind === 'PURCHASES' ? unwrap(api.POST('/api/v1/purchase-accounting/preview', { body })) : unwrap(api.POST('/api/v1/dispatch-accounting/preview', { body }))
    },
    enabled,
    gcTime: 0,
  })
}

export function useRunMutations(kind: AccountingKind) {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: accountingKeys.all(kind) })
  const p = kind === 'PURCHASES'
  return {
    generate: useMutation({
      mutationFn: (body: AccountingRunRequest) => (p ? unwrap(api.POST('/api/v1/purchase-accounting', { body })) : unwrap(api.POST('/api/v1/dispatch-accounting', { body }))),
      onSettled: refresh,
    }),
    accept: useMutation({
      mutationFn: (id: string) =>
        p ? unwrap(api.POST('/api/v1/purchase-accounting/{publicId}/accept', { params: { path: { publicId: id } } })) : unwrap(api.POST('/api/v1/dispatch-accounting/{publicId}/accept', { params: { path: { publicId: id } } })),
      onSettled: refresh,
    }),
    cancel: useMutation({
      mutationFn: (v: { id: string; comment?: string }) =>
        p
          ? unwrap(api.POST('/api/v1/purchase-accounting/{publicId}/cancel', { params: { path: { publicId: v.id } }, body: { comment: v.comment } }))
          : unwrap(api.POST('/api/v1/dispatch-accounting/{publicId}/cancel', { params: { path: { publicId: v.id } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
  }
}

/** El archivo de la corrida con la plantilla elegida o la primera activa, sin cambiar nada. */
export function exportPreview(kind: AccountingKind, id: string, templatePublicId: string | null) {
  const query = { templatePublicId: templatePublicId ?? undefined }
  return kind === 'PURCHASES'
    ? unwrap(api.GET('/api/v1/purchase-accounting/{publicId}/export-preview', { params: { path: { publicId: id }, query } }))
    : unwrap(api.GET('/api/v1/dispatch-accounting/{publicId}/export-preview', { params: { path: { publicId: id }, query } }))
}

export function useExportFilePreview(kind: AccountingKind, id: string, templatePublicId: string | null, enabled: boolean) {
  return useQuery({ queryKey: accountingKeys.filePreview(kind, id, templatePublicId), queryFn: () => exportPreview(kind, id, templatePublicId), enabled, gcTime: 0 })
}

/** Exporta: arma el archivo y pasa la corrida aceptada a exportada. */
export function exportRun(kind: AccountingKind, id: string, templatePublicId: string | null) {
  const query = { templatePublicId: templatePublicId ?? undefined }
  return kind === 'PURCHASES'
    ? unwrap(api.POST('/api/v1/purchase-accounting/{publicId}/export', { params: { path: { publicId: id }, query } }))
    : unwrap(api.POST('/api/v1/dispatch-accounting/{publicId}/export', { params: { path: { publicId: id }, query } }))
}
