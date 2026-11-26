// Lote 249 (Lote E) — datos de la «Modificación de la orden de venta»: opciones de edición, vista previa, registrar, historial, segunda aprobación, la orden interna de
// adición, la evidencia de la autorización, la bandeja «Por aprobar» y los ajustes (compañía y cliente). Todo pasa por el cliente generado (módulo SALES_ORDERS).
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../../kernel/api/client'
import type { components } from '../../../kernel/api/schema'

type Schemas = components['schemas']
export type EditOptionsDto = Schemas['SalesOrderEditOptionsDto']
export type ChangeRequest = Schemas['SalesOrderChangeRequest']
export type ChangeLineOp = Schemas['SalesOrderChangeLineOp']
export type ChangeTermsRequest = Schemas['SalesOrderChangeTermsRequest']
export type AdditionRequest = Schemas['SalesOrderAdditionRequest']
export type ChangePreviewDto = Schemas['SalesOrderChangePreviewDto']
export type ChangeDto = Schemas['SalesOrderChangeDto']
export type ChangeListItemDto = Schemas['SalesOrderChangeListItemDto']
export type ChangePendingPageDto = Schemas['SalesChangePendingPageDto']
export type ChangeLinePositionDto = Schemas['SalesChangeLinePositionDto']
export type ChangeBatchDto = Schemas['SalesChangeBatchDto']
export type ChangeDiffDto = Schemas['SalesChangeDiffDto']
export type ChangeSnapshotDto = Schemas['SalesChangeSnapshotDto']
export type ChangeSettingsDto = Schemas['SalesChangeSettingsDto']
export type ClientChangeSettingsDto = Schemas['SalesClientChangeSettingsDto']
export type StoredFileDto = Schemas['StoredFileDto']

export const changeKeys = {
  /** Bajo 'sales': registrar o aprobar una modificación vuelve a pedir la orden, la lista y el almacén. */
  options: (orderId: string) => ['sales', 'changes', 'options', orderId] as const,
  list: (orderId: string) => ['sales', 'changes', 'list', orderId] as const,
  one: (orderId: string, changeId: string) => ['sales', 'changes', 'one', orderId, changeId] as const,
  pending: (q: string, skip: number, take: number) => ['sales', 'changes', 'pending', q, skip, take] as const,
  settings: ['sales', 'changes', 'settings'] as const,
  clientSettings: (clientId: string) => ['sales', 'changes', 'settings', 'client', clientId] as const,
}

/** GET edit-options: la etapa de la orden, la posición por línea, las recolecciones, las facturas, los ajustes vigentes y los permisos del usuario. */
export function useEditOptions(orderPublicId: string | null, enabled = true) {
  return useQuery({
    queryKey: changeKeys.options(orderPublicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders/{publicId}/edit-options', { params: { path: { publicId: orderPublicId ?? '' } } })),
    enabled: enabled && Boolean(orderPublicId),
    meta: { handleAccessDenied: false },
  })
}

export function useOrderChanges(orderPublicId: string | null, enabled = true) {
  return useQuery({
    queryKey: changeKeys.list(orderPublicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders/{publicId}/changes', { params: { path: { publicId: orderPublicId ?? '' } } })),
    enabled: enabled && Boolean(orderPublicId),
    meta: { handleAccessDenied: false },
  })
}

export function useOrderChange(orderPublicId: string | null, changePublicId: string | null) {
  return useQuery({
    queryKey: changeKeys.one(orderPublicId ?? '', changePublicId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/sales-orders/{publicId}/changes/{changePublicId}', { params: { path: { publicId: orderPublicId ?? '', changePublicId: changePublicId ?? '' } } })),
    enabled: Boolean(orderPublicId) && Boolean(changePublicId),
    meta: { handleAccessDenied: false },
  })
}

/** La bandeja «Por aprobar» de la compañía: una página (skip/take) con el buscador libre. */
export function usePendingChanges(q: string, skip: number, take: number, enabled = true) {
  return useQuery({
    queryKey: changeKeys.pending(q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/sales-order-changes/pending', { params: { query: { q: q || undefined, skip, take } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** Lector de páginas de la bandeja (la exportación y el orden de TODO lo filtrado con `exportRows`). */
export function pendingChangesPageReader(q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/sales-order-changes/pending', { params: { query: { q: q || undefined, skip, take } } }))
}

/** Mutaciones de la modificación. Todas vuelven a pedir lo de ventas y el almacén (cambian reservas, recolecciones y envíos). */
export function useChangeMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries()
  return {
    preview: useMutation({
      mutationFn: (v: { orderId: string; body: ChangeRequest }) =>
        unwrap(api.POST('/api/v1/sales-orders/{publicId}/changes/preview', { params: { path: { publicId: v.orderId } }, body: v.body })),
    }),
    create: useMutation({
      mutationFn: (v: { orderId: string; body: ChangeRequest }) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/changes', { params: { path: { publicId: v.orderId } }, body: v.body })),
      onSettled: refresh,
    }),
    addition: useMutation({
      mutationFn: (v: { orderId: string; body: AdditionRequest }) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/additions', { params: { path: { publicId: v.orderId } }, body: v.body })),
      onSettled: refresh,
    }),
    approve: useMutation({
      mutationFn: (v: { orderId: string; changeId: string; note?: string; overrideCredit?: boolean }) =>
        unwrap(
          api.POST('/api/v1/sales-orders/{publicId}/changes/{changePublicId}/approve', {
            params: { path: { publicId: v.orderId, changePublicId: v.changeId } },
            body: { note: v.note || undefined, overrideCredit: v.overrideCredit || undefined },
          }),
        ),
      onSettled: refresh,
    }),
    reject: useMutation({
      mutationFn: (v: { orderId: string; changeId: string; note: string }) =>
        unwrap(api.POST('/api/v1/sales-orders/{publicId}/changes/{changePublicId}/reject', { params: { path: { publicId: v.orderId, changePublicId: v.changeId } }, body: { note: v.note } })),
      onSettled: refresh,
    }),
    withdraw: useMutation({
      mutationFn: (v: { orderId: string; changeId: string; note?: string }) =>
        unwrap(api.POST('/api/v1/sales-orders/{publicId}/changes/{changePublicId}/withdraw', { params: { path: { publicId: v.orderId, changePublicId: v.changeId } }, body: { note: v.note || undefined } })),
      onSettled: refresh,
    }),
  }
}

/** Sube la evidencia de la autorización (imagen o PDF) como documento de la orden; se refiere después por su PublicId. */
export function useUploadChangeAttachment(orderPublicId: string) {
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return unwrap(api.POST('/api/v1/sales-orders/{publicId}/change-attachments', { params: { path: { publicId: orderPublicId } }, body: {}, bodySerializer: () => form }))
    },
  })
}

// ---------------------------------------------------------------- ajustes de la segunda aprobación

export function useChangeSettings(enabled = true) {
  return useQuery({
    queryKey: changeKeys.settings,
    queryFn: () => unwrap(api.GET('/api/v1/sales-order-changes/settings')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export function useUpdateChangeSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['SalesChangeSettingsRequest']) => unwrap(api.PATCH('/api/v1/sales-order-changes/settings', { body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['sales', 'changes'] }),
  })
}

export function useClientChangeSettings(clientPublicId: string | null, enabled = true) {
  return useQuery({
    queryKey: changeKeys.clientSettings(clientPublicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/sales-order-changes/settings/clients/{clientPublicId}', { params: { path: { clientPublicId: clientPublicId ?? '' } } })),
    enabled: enabled && Boolean(clientPublicId),
    meta: { handleAccessDenied: false },
  })
}

export function useUpdateClientChangeSettings(clientPublicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['SalesClientChangeSettingsRequest']) =>
      unwrap(api.PATCH('/api/v1/sales-order-changes/settings/clients/{clientPublicId}', { params: { path: { clientPublicId } }, body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['sales', 'changes'] }),
  })
}
