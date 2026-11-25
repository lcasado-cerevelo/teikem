// Lote 246 (Lote F) — QuickBooks de Órdenes de compra (`/api/v1/purchase-orders/quickbooks/*`, módulo PURCHASING, todo con purchasing.quickbooks): la lista de por pasar, exportar el
// archivo .iif (PURCHORD), confirmar «sí, ya están», «No pasó», marcar y quitar la marca. Ninguna acción cambia el estatus de la orden.
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { batchesOf, selectionBody } from './purchaseQuickBooksRules'

export type PurchaseQbExportDto = components['schemas']['PurchaseQuickBooksExportDto']
export type PurchaseQbMarkResultDto = components['schemas']['PurchaseQuickBooksMarkResultDto']
export type PurchaseQbNotPassedResultDto = components['schemas']['PurchaseQuickBooksNotPassedResultDto']

export const purchaseQbKeys = { all: ['purchase-quickbooks'] as const, pending: ['purchase-quickbooks', 'pending'] as const }

/** Lo que cambia con cualquier acción: la lista de por pasar, las listas y fichas de órdenes (estado y chips). */
function refresh(qc: QueryClient) {
  void qc.invalidateQueries({ queryKey: purchaseQbKeys.all })
  void qc.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('/api/v1/purchase-orders') })
}

export function usePurchaseQbPending(enabled = true) {
  return useQuery({ queryKey: purchaseQbKeys.pending, queryFn: () => unwrap(api.GET('/api/v1/purchase-orders/quickbooks')), enabled, meta: { handleAccessDenied: false } })
}

function addMarks(results: PurchaseQbMarkResultDto[]): PurchaseQbMarkResultDto {
  return results.reduce<PurchaseQbMarkResultDto>(
    (acc, r) => ({ marked: (acc.marked ?? 0) + (r.marked ?? 0), alreadyMarked: (acc.alreadyMarked ?? 0) + (r.alreadyMarked ?? 0), suppliersSent: (acc.suppliersSent ?? 0) + (r.suppliersSent ?? 0), productsSent: (acc.productsSent ?? 0) + (r.productsSent ?? 0), atUtc: r.atUtc ?? acc.atUtc }),
    { marked: 0, alreadyMarked: 0, suppliersSent: 0, productsSent: 0, atUtc: '' },
  )
}

export function usePurchaseQbMutations() {
  const qc = useQueryClient()
  const done = () => refresh(qc)
  /** Exporta las elegidas (o todas) en un archivo; anota quién y cuándo, pero NO las marca. */
  const exportFile = useMutation({
    mutationFn: (v: { ids: string[]; totalPending: number; includeNew: boolean }) =>
      unwrap(api.POST('/api/v1/purchase-orders/quickbooks/export', { body: { ...selectionBody(v.ids, v.totalPending), includeNew: v.includeNew } })),
    onSuccess: done,
  })
  /** «Sí, ya están»: marca como en QuickBooks las del archivo que se importaron. */
  const confirm = useMutation({
    mutationFn: async (ids: string[]) => addMarks(await Promise.all(batchesOf(ids).map((b) => unwrap(api.POST('/api/v1/purchase-orders/quickbooks/confirm', { body: { purchaseOrderIds: b } }))))),
    onSuccess: done,
  })
  /** «No pasó»: pierden la fecha del archivo y vuelven a por pasar. */
  const notPassed = useMutation({
    mutationFn: (ids: string[]) => unwrap(api.POST('/api/v1/purchase-orders/quickbooks/not-passed', { body: { purchaseOrderIds: ids } })),
    onSuccess: done,
  })
  /** Marcar a mano: `ids` o `'all'` (todas las por pasar). */
  const mark = useMutation({
    mutationFn: async (ids: string[] | 'all') => {
      if (ids === 'all') return unwrap(api.POST('/api/v1/purchase-orders/quickbooks/mark', { body: { all: true } }))
      return addMarks(await Promise.all(batchesOf(ids).map((b) => unwrap(api.POST('/api/v1/purchase-orders/quickbooks/mark', { body: { purchaseOrderIds: b } })))))
    },
    onSuccess: done,
  })
  /** Quitar la marca (vuelve a «exportada» o a «por pasar»). */
  const unmark = useMutation({
    mutationFn: (ids: string[]) => unwrap(api.POST('/api/v1/purchase-orders/quickbooks/unmark', { body: { purchaseOrderIds: ids } })),
    onSuccess: done,
  })
  return { exportFile, confirm, notPassed, mark, unmark }
}
