// Lote 241 — Análisis de ventas: lecturas y escrituras de `/api/v1/sales-analysis/*` (cliente generado). Toda escritura invalida
// todo lo del análisis (las vistas dependen de rutas, representantes, cuotas, líneas, clientes y el historial).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { clientKeys } from '../clients/api'

export const SA = '/api/v1/sales-analysis'

export const saKeys = {
  all: [SA] as const,
  byRep: (month: string, compareMonth: string) => [SA, 'by-rep', month, compareMonth] as const,
  transactions: (q: object) => [SA, 'transactions', q] as const,
  movement: (from: string, to: string) => [SA, 'product-movement', from, to] as const,
  charts: (from: string, to: string, month: string) => [SA, 'charts', from, to, month] as const,
  status: [SA, 'status'] as const,
  settings: [SA, 'settings'] as const,
  routes: [SA, 'routes'] as const,
  reps: [SA, 'reps'] as const,
  quotas: (month: string) => [SA, 'quotas', month] as const,
  lines: [SA, 'lines'] as const,
  brands: [SA, 'brands'] as const,
  clients: (q: object) => [SA, 'clients', q] as const,
}

const opt = (v: string) => (v ? v : undefined)

export function useSalesByRep(month: string, compareMonth: string) {
  return useQuery({
    queryKey: saKeys.byRep(month, compareMonth),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/by-rep', { params: { query: { month: opt(month), compareMonth: opt(compareMonth) } } })),
    placeholderData: keepPreviousData,
  })
}

export interface TransactionQuery {
  month: string
  repId: number | null
  q: string
}

export function fetchTransactions(query: TransactionQuery, skip: number, take: number, all = false) {
  return unwrap(
    api.GET('/api/v1/sales-analysis/transactions', {
      params: { query: { month: opt(query.month), repId: query.repId ?? undefined, q: opt(query.q), skip, take, all: all || undefined } },
    }),
  )
}

export function useSalesTransactions(query: TransactionQuery, page: number, pageSize: number, enabled = true) {
  return useQuery({
    queryKey: saKeys.transactions({ ...query, page, pageSize }),
    queryFn: () => fetchTransactions(query, (page - 1) * pageSize, pageSize),
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function fetchProductMovement(fromMonth: string, toMonth: string) {
  return unwrap(api.GET('/api/v1/sales-analysis/product-movement', { params: { query: { fromMonth: opt(fromMonth), toMonth: opt(toMonth) } } }))
}

export function useProductMovement(fromMonth: string, toMonth: string) {
  return useQuery({ queryKey: saKeys.movement(fromMonth, toMonth), queryFn: () => fetchProductMovement(fromMonth, toMonth), placeholderData: keepPreviousData })
}

export function useSalesCharts(fromMonth: string, toMonth: string, month: string) {
  return useQuery({
    queryKey: saKeys.charts(fromMonth, toMonth, month),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/charts', { params: { query: { fromMonth: opt(fromMonth), toMonth: opt(toMonth), month: opt(month) } } })),
    placeholderData: keepPreviousData,
  })
}

export function useSalesStatus() {
  return useQuery({ queryKey: saKeys.status, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/status')), meta: { handleAccessDenied: false } })
}

export function useSalesSettings() {
  return useQuery({ queryKey: saKeys.settings, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/settings')) })
}

export function useSalesRoutes() {
  return useQuery({ queryKey: saKeys.routes, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/routes', { params: { query: { includeInactive: true } } })) })
}

export function useSalesReps() {
  return useQuery({ queryKey: saKeys.reps, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/reps', { params: { query: { includeInactive: true } } })) })
}

/** Las cuotas de varios meses (una llamada por mes, en paralelo). */
export function useQuotaMonths(months: readonly string[]) {
  return useQuery({
    queryKey: [SA, 'quotas', ...months],
    queryFn: () => Promise.all(months.map((month) => unwrap(api.GET('/api/v1/sales-analysis/quotas', { params: { query: { month } } })))),
    enabled: months.length > 0,
  })
}

export function useProductLines() {
  return useQuery({ queryKey: saKeys.lines, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/lines')) })
}

export function useSalesBrands() {
  return useQuery({ queryKey: saKeys.brands, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/brands')) })
}

export interface ClientRepQuery {
  repId: number | null
  unassigned: boolean
  q: string
}

export function fetchSalesClients(query: ClientRepQuery, skip: number, take: number) {
  return unwrap(
    api.GET('/api/v1/sales-analysis/clients', {
      params: { query: { repId: query.repId ?? undefined, unassigned: query.unassigned || undefined, q: opt(query.q), skip, take } },
    }),
  )
}

export function useSalesClients(query: ClientRepQuery, page: number, pageSize: number, enabled = true) {
  return useQuery({
    queryKey: saKeys.clients({ ...query, page, pageSize }),
    queryFn: () => fetchSalesClients(query, (page - 1) * pageSize, pageSize),
    placeholderData: keepPreviousData,
    enabled,
  })
}

/** Productos del catálogo para «Emparejar a mano» (id numérico). */
export function useProductSearch(search: string, enabled: boolean) {
  return useQuery({
    queryKey: [SA, 'product-search', search],
    queryFn: () => unwrap(api.GET('/api/v1/products', { params: { query: { search: opt(search), take: 20 } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** Usuarios de la compañía (para enlazar un representante a su usuario); sin permiso, la lista queda vacía. */
export function useCompanyUsers(enabled: boolean) {
  return useQuery({ queryKey: [SA, 'users'], queryFn: () => unwrap(api.GET('/api/v1/users')), enabled, retry: false, staleTime: 60_000, meta: { handleAccessDenied: false } })
}

function useInvalidate() {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: saKeys.all })
}

function useSaMutation<V, R>(fn: (v: V) => Promise<R>, extra?: (qc: ReturnType<typeof useQueryClient>, v: V) => void) {
  const qc = useQueryClient()
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: fn,
    onSuccess: (_r, v) => {
      void invalidate()
      extra?.(qc, v)
    },
  })
}

export function useSaveSettings() {
  return useSaMutation((body: { cutoffDate?: string | null; clearCutoff?: boolean; leadTimeDays?: number | null; serviceLevelZ?: number | null }) =>
    unwrap(api.PUT('/api/v1/sales-analysis/settings', { body })),
  )
}

export function useSaveRoute() {
  return useSaMutation((v: { id?: number; name: string; sortOrder: number | null }) =>
    v.id
      ? unwrap(api.PATCH('/api/v1/sales-analysis/routes/{id}', { params: { path: { id: v.id } }, body: { name: v.name, sortOrder: v.sortOrder } }))
      : unwrap(api.POST('/api/v1/sales-analysis/routes', { body: { name: v.name, sortOrder: v.sortOrder } })),
  )
}

export function useSetRouteActive() {
  return useSaMutation((v: { id: number; active: boolean }) =>
    v.active
      ? unwrap(api.POST('/api/v1/sales-analysis/routes/{id}/reactivate', { params: { path: { id: v.id } } }))
      : unwrap(api.POST('/api/v1/sales-analysis/routes/{id}/deactivate', { params: { path: { id: v.id } } })),
  )
}

export interface RepSave {
  id?: number
  name: string
  routeId: number | null
  userId: number | null
}

export function useSaveRep() {
  return useSaMutation((v: RepSave) => {
    const body = { name: v.name, routeId: v.routeId, clearRoute: v.routeId === null, userId: v.userId, clearUser: v.userId === null }
    return v.id
      ? unwrap(api.PATCH('/api/v1/sales-analysis/reps/{id}', { params: { path: { id: v.id } }, body }))
      : unwrap(api.POST('/api/v1/sales-analysis/reps', { body: { name: v.name, routeId: v.routeId, userId: v.userId } }))
  })
}

export function useSetRepActive() {
  return useSaMutation((v: { id: number; active: boolean }) =>
    v.active
      ? unwrap(api.POST('/api/v1/sales-analysis/reps/{id}/reactivate', { params: { path: { id: v.id } } }))
      : unwrap(api.POST('/api/v1/sales-analysis/reps/{id}/deactivate', { params: { path: { id: v.id } } })),
  )
}

export function useRepsFromCustomField() {
  return useSaMutation(() => unwrap(api.POST('/api/v1/sales-analysis/reps/from-custom-field')))
}

export function useSaveQuotas() {
  return useSaMutation(async (changes: { month: string; items: { repId: number; amount: number | null }[] }[]) => {
    for (const c of changes) await unwrap(api.PUT('/api/v1/sales-analysis/quotas/{month}', { params: { path: { month: c.month } }, body: { items: c.items } }))
    return changes.length
  })
}

export function useSaveLine() {
  return useSaMutation((v: { id?: number; name: string; sortOrder: number | null }) =>
    v.id
      ? unwrap(api.PATCH('/api/v1/sales-analysis/lines/{id}', { params: { path: { id: v.id } }, body: { name: v.name, sortOrder: v.sortOrder } }))
      : unwrap(api.POST('/api/v1/sales-analysis/lines', { body: { name: v.name, sortOrder: v.sortOrder } })),
  )
}

export function useSetLineActive() {
  return useSaMutation((v: { id: number; active: boolean }) =>
    v.active
      ? unwrap(api.POST('/api/v1/sales-analysis/lines/{id}/reactivate', { params: { path: { id: v.id } } }))
      : unwrap(api.POST('/api/v1/sales-analysis/lines/{id}/deactivate', { params: { path: { id: v.id } } })),
  )
}

export function useSetLineBrands() {
  return useSaMutation((v: { id: number; brands: string[] }) =>
    unwrap(api.PUT('/api/v1/sales-analysis/lines/{id}/brands', { params: { path: { id: v.id } }, body: { brands: v.brands } })),
  )
}

/** Cambia el representante de UN cliente (también desde su ficha: se refresca la ficha). */
export function useSetClientRep(clientPublicId?: string | null) {
  return useSaMutation(
    (v: { clientId: number; repId: number | null }) =>
      unwrap(api.PUT('/api/v1/sales-analysis/clients/{clientId}/rep', { params: { path: { clientId: v.clientId } }, body: { repId: v.repId } })),
    (qc) => {
      if (clientPublicId) void qc.invalidateQueries({ queryKey: clientKeys.detail(clientPublicId) })
    },
  )
}

export function useAssignClientsRep() {
  return useSaMutation((v: { clientIds: number[]; repId: number | null }) => unwrap(api.POST('/api/v1/sales-analysis/clients/assign-rep', { body: v })))
}

export function useLinkUnmatched() {
  return useSaMutation((v: { kind: 'CLIENT' | 'PRODUCT'; raw: string; clientId?: number; productId?: number }) =>
    unwrap(api.POST('/api/v1/sales-analysis/unmatched/link', { body: v })),
  )
}

export interface ImportInput {
  salesFile: File | null
  purchasesFile: File | null
  dryRun: boolean
  cutoffDate: string
  repsFromCustomField: boolean
}

/** `POST import` (multipart). La simulación (`dryRun`) no escribe nada; solo la real invalida las vistas. */
export function useImportHistory() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (v: ImportInput) => {
      const form = new FormData()
      if (v.salesFile) form.append('salesFile', v.salesFile)
      if (v.purchasesFile) form.append('purchasesFile', v.purchasesFile)
      form.append('dryRun', String(v.dryRun))
      if (v.cutoffDate) form.append('cutoffDate', v.cutoffDate)
      form.append('repsFromCustomField', String(v.repsFromCustomField))
      return unwrap(api.POST('/api/v1/sales-analysis/import', { body: {}, bodySerializer: () => form }))
    },
    onSuccess: (_r, v) => {
      if (!v.dryRun) void invalidate()
    },
  })
}
