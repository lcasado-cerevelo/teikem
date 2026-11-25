// Lote 241 — Análisis de ventas: lecturas y escrituras de `/api/v1/sales-analysis/*` (cliente generado). Toda escritura invalida
// todo lo del análisis (las vistas dependen de rutas, representantes, cuotas, líneas, clientes y el historial).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { clientKeys } from '../clients/api'

export const SA = '/api/v1/sales-analysis'

export const saKeys = {
  all: [SA] as const,
  byRep: (month: string, compareMonth: string) => [SA, 'by-rep', month, compareMonth] as const,
  byRoute: (month: string, compareMonth: string) => [SA, 'by-route', month, compareMonth] as const,
  routePeople: (routeId: number, history: boolean, includeVoided: boolean) => [SA, 'route-people', routeId, history, includeVoided] as const,
  candidates: (q: string) => [SA, 'seller-candidates', q] as const,
  clientSales: (clientId: number) => [SA, 'client-sales', clientId] as const,
  routeQuotas: (routeId: number) => [SA, 'route-quotas', routeId] as const,
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

/** Lote 244 — vista «Por ruta» (la de por omisión); «Por representante» (`useSalesByRep`) se conserva. */
export function useSalesByRoute(month: string, compareMonth: string) {
  return useQuery({
    queryKey: saKeys.byRoute(month, compareMonth),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/by-route', { params: { query: { month: opt(month), compareMonth: opt(compareMonth) } } })),
    placeholderData: keepPreviousData,
  })
}

export interface TransactionQuery {
  month: string
  repId: number | null
  /** Lote 244: 0 = sin ruta de venta; null = todas. */
  routeId?: number | null
  q: string
}

export function fetchTransactions(query: TransactionQuery, skip: number, take: number, all = false) {
  return unwrap(
    api.GET('/api/v1/sales-analysis/transactions', {
      params: { query: { month: opt(query.month), repId: query.repId ?? undefined, routeId: query.routeId ?? undefined, q: opt(query.q), skip, take, all: all || undefined } },
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

export function useSalesRoutes(enabled = true) {
  return useQuery({ queryKey: saKeys.routes, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/routes', { params: { query: { includeInactive: true } } })), enabled })
}

export function useSalesReps() {
  return useQuery({ queryKey: saKeys.reps, queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/reps', { params: { query: { includeInactive: true } } })) })
}

/** Lote 244 — solo los representantes sin usuario (los que hay que enlazar antes de pasarlos a personas de una ruta). */
export function useRepsWithoutUser(enabled = true) {
  return useQuery({
    queryKey: [SA, 'reps', 'without-user'],
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/reps', { params: { query: { includeInactive: false, withoutUser: true } } })),
    enabled,
  })
}

/** Lote 244 — personas de una ruta de venta: titular, apoyos y, si se pide, el historial (con las anuladas aparte). */
export function useRoutePeople(routeId: number | null, history = false, includeVoided = false) {
  return useQuery({
    queryKey: saKeys.routePeople(routeId ?? 0, history, includeVoided),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/routes/{id}/people', { params: { path: { id: routeId ?? 0 }, query: { history, includeVoided } } })),
    enabled: routeId != null,
  })
}

/** Lote 244 — personas activas de la compañía que se pueden agregar a una ruta (id, nombre y correo). */
export function useSellerCandidates(q: string, enabled = true) {
  return useQuery({
    queryKey: saKeys.candidates(q),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/sellers/candidates', { params: { query: { q: opt(q), take: 50 } } })),
    enabled,
    placeholderData: keepPreviousData,
  })
}

/** Lote 244 — la venta de un cliente: ruta (propia o heredada), equipo, vendedor preferido (con aviso si salió de la ruta) y el propuesto. */
export function useClientSales(clientId: number | null | undefined) {
  return useQuery({
    queryKey: saKeys.clientSales(clientId ?? 0),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/clients/{clientId}/sales', { params: { path: { clientId: clientId ?? 0 } } })),
    enabled: clientId != null,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

/** Lote 244 — las cuotas propias de una ruta, mes por mes. */
export function useRouteQuotas(routeId: number | null) {
  return useQuery({
    queryKey: saKeys.routeQuotas(routeId ?? 0),
    queryFn: () => unwrap(api.GET('/api/v1/sales-analysis/routes/{id}/quotas', { params: { path: { id: routeId ?? 0 } } })),
    enabled: routeId != null,
  })
}

/** Las cuotas de varios meses (una llamada por mes, en paralelo). */
export function useQuotaMonths(months: readonly string[], by?: 'REP' | 'ROUTE') {
  return useQuery({
    queryKey: [SA, 'quotas', by ?? 'default', ...months],
    queryFn: () => Promise.all(months.map((month) => unwrap(api.GET('/api/v1/sales-analysis/quotas', { params: { query: { month, by } } })))),
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
  /** Lote 244: ruta efectiva (la propia o la del representante). */
  routeId?: number | null
  noRoute?: boolean
  q: string
}

export function fetchSalesClients(query: ClientRepQuery, skip: number, take: number) {
  return unwrap(
    api.GET('/api/v1/sales-analysis/clients', {
      params: { query: { repId: query.repId ?? undefined, unassigned: query.unassigned || undefined, routeId: query.routeId ?? undefined, noRoute: query.noRoute || undefined, q: opt(query.q), skip, take } },
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
  return useSaMutation((body: { cutoffDate?: string | null; clearCutoff?: boolean; leadTimeDays?: number | null; serviceLevelZ?: number | null; quotaBy?: 'REP' | 'ROUTE' | null }) =>
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
    // Lote 244: el usuario es obligatorio, así que nunca se manda `clearUser`; un representante heredado sin usuario se puede renombrar sin mandarlo.
    const body = { name: v.name, routeId: v.routeId, clearRoute: v.routeId === null, userId: v.userId ?? undefined }
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
  return useSaMutation(async (changes: { month: string; items: { repId?: number; routeId?: number; amount: number | null }[] }[]) => {
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

// ---- Lote 244: personas de la ruta, venta del cliente y rutas desde los representantes ----

export type RoutePersonRole = 'PRIMARY' | 'SUPPORT'

export function useAssignRoutePerson() {
  return useSaMutation((v: { routeId: number; userId: number; role: RoutePersonRole; validFrom?: string | null }) =>
    unwrap(api.POST('/api/v1/sales-analysis/routes/{id}/people', { params: { path: { id: v.routeId } }, body: { userId: v.userId, role: v.role, validFrom: v.validFrom || undefined } })),
  )
}

export function useCloseRoutePerson() {
  return useSaMutation((v: { routeId: number; assignmentId: number; validTo?: string | null; rowVersion?: string | null }) =>
    unwrap(
      api.POST('/api/v1/sales-analysis/routes/{id}/people/{assignmentId}/close', {
        params: { path: { id: v.routeId, assignmentId: v.assignmentId } },
        body: { validTo: v.validTo || undefined, rowVersion: v.rowVersion ?? undefined },
      }),
    ),
  )
}

export function useVoidRoutePerson() {
  return useSaMutation((v: { routeId: number; assignmentId: number }) =>
    unwrap(api.POST('/api/v1/sales-analysis/routes/{id}/people/{assignmentId}/void', { params: { path: { id: v.routeId, assignmentId: v.assignmentId } } })),
  )
}

/** Guarda la venta de UN cliente (ruta propia y vendedor preferido); refresca también la ficha del cliente. */
export function useSaveClientSales(clientPublicId?: string | null) {
  return useSaMutation(
    (v: { clientId: number; routeId?: number | null; clearRoute?: boolean; preferredUserId?: number | null; clearPreferred?: boolean; rowVersion?: string | null }) =>
      unwrap(
        api.PUT('/api/v1/sales-analysis/clients/{clientId}/sales', {
          params: { path: { clientId: v.clientId } },
          body: { routeId: v.routeId ?? undefined, clearRoute: v.clearRoute, preferredUserId: v.preferredUserId ?? undefined, clearPreferred: v.clearPreferred, rowVersion: v.rowVersion ?? undefined },
        }),
      ),
    (qc) => {
      if (clientPublicId) void qc.invalidateQueries({ queryKey: clientKeys.detail(clientPublicId) })
    },
  )
}

/** Asigna (o quita, con `routeId` null) la ruta de venta propia a varios clientes. */
export function useAssignClientsRoute() {
  return useSaMutation((v: { clientIds: number[]; routeId: number | null }) => unwrap(api.POST('/api/v1/sales-analysis/clients/assign-route', { body: v })))
}

/** Crea las rutas desde los representantes: `dryRun` simula (no escribe ni invalida). */
export function useRoutesFromReps() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (dryRun: boolean) => unwrap(api.POST('/api/v1/sales-analysis/routes/from-reps', { params: { query: { dryRun } } })),
    onSuccess: (_r, dryRun) => {
      if (!dryRun) void invalidate()
    },
  })
}
