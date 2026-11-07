// Lote 225 (P5) — QuickBooks Desktop por archivo IIF (`/api/v1/sales-orders/quickbooks/*`, módulo SALES_ORDERS). Ver la lista y el histórico: sales.quickbooks.view, .mark o .export;
// exportar y confirmar «sí, ya están»: sales.quickbooks.export; marcar / quitar la marca: sales.quickbooks.mark; ajustes: leer view o admin.tenant, cambiar admin.tenant.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { markBatches } from './quickBooksRules'

export type QuickBooksPendingDto = components['schemas']['QuickBooksPendingDto']
export type QuickBooksPendingInvoiceDto = components['schemas']['QuickBooksPendingInvoiceDto']
export type QuickBooksNewClientDto = components['schemas']['QuickBooksNewClientDto']
export type QuickBooksNewProductDto = components['schemas']['QuickBooksNewProductDto']
export type QuickBooksExportDto = components['schemas']['QuickBooksExportDto']
export type QuickBooksMarkResultDto = components['schemas']['QuickBooksMarkResultDto']
export type QuickBooksHistoryItemDto = components['schemas']['QuickBooksHistoryItemDto']
export type QuickBooksSettingsDto = components['schemas']['QuickBooksSettingsDto']
export type QuickBooksSettingsRequest = components['schemas']['QuickBooksSettingsRequest']
export type QuickBooksTermMapDto = components['schemas']['QuickBooksTermMapDto']

export interface QuickBooksHistoryQuery {
  from?: string
  to?: string
  q?: string
  skip: number
  take: number
}

export const quickBooksKeys = {
  all: ['quickbooks'] as const,
  pending: ['quickbooks', 'pending'] as const,
  history: (q: QuickBooksHistoryQuery) => ['quickbooks', 'history', q] as const,
  settings: ['quickbooks', 'settings'] as const,
}
/** La cifra «Facturas por pasar a QuickBooks» del panel Ventas del Pulso (misma clave que `useSalesPulse`). */
const PULSE_KEY = ['sales', '/api/v1/sales-orders/pulse'] as const

export function useQuickBooksPending() {
  return useQuery({ queryKey: quickBooksKeys.pending, queryFn: () => unwrap(api.GET('/api/v1/sales-orders/quickbooks')) })
}

/** Histórico de las ya pasadas (quién y cuándo), paginado por el servidor. */
export function quickBooksHistoryPage(query: Omit<QuickBooksHistoryQuery, 'skip' | 'take'>) {
  return (skip: number, take: number) =>
    unwrap(api.GET('/api/v1/sales-orders/quickbooks/history', { params: { query: { from: query.from || undefined, to: query.to || undefined, q: query.q || undefined, skip, take } } }))
}

export function useQuickBooksHistory(query: QuickBooksHistoryQuery) {
  return useQuery({
    queryKey: quickBooksKeys.history(query),
    queryFn: () => quickBooksHistoryPage(query)(query.skip, query.take),
    placeholderData: keepPreviousData,
  })
}

/** Suma los resultados de varias llamadas (los envíos de más de 1000 facturas se parten). */
function addResults(results: QuickBooksMarkResultDto[]): QuickBooksMarkResultDto {
  return results.reduce<QuickBooksMarkResultDto>(
    (acc, r) => ({ marked: (acc.marked ?? 0) + (r.marked ?? 0), alreadyMarked: (acc.alreadyMarked ?? 0) + (r.alreadyMarked ?? 0), clientsSent: (acc.clientsSent ?? 0) + (r.clientsSent ?? 0), productsSent: (acc.productsSent ?? 0) + (r.productsSent ?? 0), atUtc: r.atUtc ?? acc.atUtc }),
    { marked: 0, alreadyMarked: 0, clientsSent: 0, productsSent: 0, atUtc: '' },
  )
}

export function useQuickBooksMutations() {
  const qc = useQueryClient()
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: quickBooksKeys.all })
    void qc.invalidateQueries({ queryKey: PULSE_KEY })
  }
  /** Exporta TODAS las por pasar (no marca nada): el archivo en base64 y la lista para «¿Ya importaste estas N?». */
  const exportFile = useMutation({
    mutationFn: (includeNew: boolean) => unwrap(api.POST('/api/v1/sales-orders/quickbooks/export', { body: { includeNew } })),
    onSuccess: refresh,
  })
  /** «Sí, ya están»: marca las del archivo que se importaron. */
  const confirm = useMutation({
    mutationFn: async (ids: string[]) => {
      const out: QuickBooksMarkResultDto[] = []
      for (const b of markBatches(ids)) out.push(await unwrap(api.POST('/api/v1/sales-orders/quickbooks/confirm', { body: { invoiceIds: b } })))
      return addResults(out)
    },
    onSuccess: refresh,
  })
  /** Marcar a mano: `ids` (una o varias) o `'all'` (todas las por pasar). */
  const mark = useMutation({
    mutationFn: async (ids: string[] | 'all') => {
      if (ids === 'all') return unwrap(api.POST('/api/v1/sales-orders/quickbooks/mark', { body: { all: true } }))
      const out: QuickBooksMarkResultDto[] = []
      for (const b of markBatches(ids)) out.push(await unwrap(api.POST('/api/v1/sales-orders/quickbooks/mark', { body: { invoiceIds: b, all: false } })))
      return addResults(out)
    },
    onSuccess: refresh,
  })
  /** Quitar la marca (vuelve a «por pasar»). */
  const unmark = useMutation({
    mutationFn: (ids: string[]) => unwrap(api.POST('/api/v1/sales-orders/quickbooks/unmark', { body: { invoiceIds: ids, all: false } })),
    onSuccess: refresh,
  })
  return { exportFile, confirm, mark, unmark }
}

export function useQuickBooksSettings(enabled = true) {
  return useQuery({ queryKey: quickBooksKeys.settings, queryFn: () => unwrap(api.GET('/api/v1/sales-orders/quickbooks/settings')), enabled, meta: { handleAccessDenied: false } })
}

export function useSaveQuickBooksSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: QuickBooksSettingsRequest) => unwrap(api.PATCH('/api/v1/sales-orders/quickbooks/settings', { body })),
    onSuccess: (data) => qc.setQueryData(quickBooksKeys.settings, data),
  })
}
