// Lote F17 (Rentas F-R1) — datos de Rentas (`/api/v1/rentals`, módulo RENTAL_EQUIPMENT; manual 11). Lecturas con
// `rental.view`; alta, edición, equipos, tarifas, programar, despachar y cancelar con `rental.manage`; extender con
// `rental.extend` (los aplica el API; la pantalla esconde lo que no se puede). Lecturas: `useQuery` con clave `[ruta, params]`;
// escrituras: `useMutation` que deja en caché la ficha devuelta e invalida la lista, la bitácora de extensiones, el historial de
// estatus, "Necesita tu atención" y, si mueven o reservan inventario, todo lo que depende de la existencia.
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import type { components } from '../../kernel/api/schema'
import { catalogKeys } from '../../kernel/catalogs'
import { ATTENTION_QUERY_KEY } from '../analytics/api'
import { invalidateStock } from '../warehouse/api'
import type { RentalDto, RentalListQuery } from './rentalRules'
import type { RentalProcessListQuery, RentalReturnCreateRequest, RentalReturnListQuery } from './returnRules'

type Schemas = components['schemas']
export type LocationDto = Schemas['LocationDto']
export type ClientContactDto = Schemas['ClientContactDto']

export interface RentalQueryOptions {
  enabled?: boolean
  handleAccessDenied?: boolean
}

function meta(options?: RentalQueryOptions) {
  return options?.handleAccessDenied === false ? { handleAccessDenied: false } : undefined
}

/** Prefijos para invalidar (`[ruta]`). */
export const rentalKeys = {
  rentals: ['/api/v1/rentals'],
  rental: ['/api/v1/rentals/{publicId}'],
  extensions: ['/api/v1/rentals/{publicId}/extensions'],
  locations: ['/api/v1/locations'],
  contacts: ['/api/v1/clients/{publicId}/contacts'],
  // Lote F18 (Rentas F-R2)
  returns: ['/api/v1/rental-returns'],
  rentalReturn: ['/api/v1/rental-returns/{publicId}'],
  processes: ['/api/v1/rental-processes'],
  summary: ['/api/v1/rentals', 'summary'],
  reports: ['/api/v1/analytics/reports', 'rentals'],
  reportRun: ['/api/v1/analytics/reports/{id}/run'],
} as const

/** `GET /api/v1/rentals` paginado (filtros: estatus, cliente, `dueWithinDays`, `overdue`, búsqueda). */
export function useRentals(query: RentalListQuery, options?: RentalQueryOptions) {
  return useQuery({
    queryKey: [rentalKeys.rentals[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/rentals', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Todo lo filtrado (Exportar), de a 200 hasta 10 000. */
export function exportRentals(query: RentalListQuery) {
  return fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/rentals', { params: { query: { ...query, skip, take } } })))
}

/** `GET /api/v1/rentals/{publicId}` (ficha). */
export function useRental(publicId: string | null | undefined, options?: RentalQueryOptions) {
  return useQuery({
    queryKey: [rentalKeys.rental[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/rentals/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** `GET /api/v1/rentals/{publicId}/extensions` (bitácora de extensiones). */
export function useRentalExtensions(publicId: string | null | undefined, options?: RentalQueryOptions) {
  return useQuery({
    queryKey: [rentalKeys.extensions[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/rentals/{publicId}/extensions', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/**
 * Localidades PROPIAS del cliente (`GET /api/v1/locations?clientId=&includeShared=false`: la renta exige una localidad del
 * cliente; las compartidas dan 400). Exige `locations.read` + CATALOG: un 403 no saca de la pantalla.
 */
export function useClientLocations(clientPublicId: string | null | undefined) {
  return useQuery({
    queryKey: [rentalKeys.locations[0], { clientId: clientPublicId, includeShared: false }],
    queryFn: () => unwrap(api.GET('/api/v1/locations', { params: { query: { clientId: clientPublicId ?? '', includeShared: false } } })),
    enabled: Boolean(clientPublicId),
    meta: { handleAccessDenied: false },
  })
}

/** Contactos del cliente (`GET /api/v1/clients/{publicId}/contacts`, `clients.read` + CATALOG; 403 sin sacar de la pantalla). */
export function useClientContacts(clientPublicId: string | null | undefined) {
  return useQuery({
    queryKey: [rentalKeys.contacts[0], clientPublicId],
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}/contacts', { params: { path: { publicId: clientPublicId ?? '' } } })),
    enabled: Boolean(clientPublicId),
    meta: { handleAccessDenied: false },
  })
}

/** Después de escribir: la ficha devuelta en caché y lo que depende de la renta (lista, bitácora, historial, aviso). */
async function afterWrite(qc: QueryClient, dto: RentalDto, opts: { stock: boolean }) {
  const publicId = dto.rental?.publicId
  if (publicId) qc.setQueryData([rentalKeys.rental[0], publicId], dto)
  await Promise.all([
    qc.invalidateQueries({ queryKey: rentalKeys.rentals }),
    qc.invalidateQueries({ queryKey: rentalKeys.extensions }),
    qc.invalidateQueries({ queryKey: [catalogKeys.history('', 0)[0]] }),
    qc.invalidateQueries({ queryKey: ATTENTION_QUERY_KEY }),
    opts.stock ? invalidateStock(qc) : Promise.resolve(),
  ])
}

/** `POST /api/v1/rentals` (alta en Borrador; no reserva nada). */
export function useCreateRental() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: Schemas['RentalCreateRequest']) => unwrap(api.POST('/api/v1/rentals', { body })),
    onSuccess: (dto) => afterWrite(qc, dto, { stock: false }),
  })
}

/** Acciones sobre una renta (unión por `action`). Las que reservan o mueven series invalidan también la existencia. */
export type RentalAction =
  | { action: 'patch'; publicId: string; body: Schemas['RentalPatchRequest'] }
  | { action: 'addLines'; publicId: string; body: Schemas['RentalLinesAddRequest'] }
  | { action: 'removeLine'; publicId: string; lineId: number }
  | { action: 'setRate'; publicId: string; lineId: number; body: Schemas['RentalLineRateRequest'] }
  | { action: 'schedule' | 'dispatch' | 'cancel'; publicId: string; body: Schemas['RentalStatusRequest'] }
  | { action: 'extend'; publicId: string; body: Schemas['RentalExtendRequest'] }

function runAction(a: RentalAction): Promise<RentalDto> {
  const path = { publicId: a.publicId }
  switch (a.action) {
    case 'patch':
      return unwrap(api.PATCH('/api/v1/rentals/{publicId}', { params: { path }, body: a.body }))
    case 'addLines':
      return unwrap(api.POST('/api/v1/rentals/{publicId}/lines', { params: { path }, body: a.body }))
    case 'removeLine':
      return unwrap(api.DELETE('/api/v1/rentals/{publicId}/lines/{lineId}', { params: { path: { ...path, lineId: a.lineId } } }))
    case 'setRate':
      return unwrap(api.PUT('/api/v1/rentals/{publicId}/lines/{lineId}/rate', { params: { path: { ...path, lineId: a.lineId } }, body: a.body }))
    case 'schedule':
      return unwrap(api.POST('/api/v1/rentals/{publicId}/schedule', { params: { path }, body: a.body }))
    case 'dispatch':
      return unwrap(api.POST('/api/v1/rentals/{publicId}/dispatch', { params: { path }, body: a.body }))
    case 'cancel':
      return unwrap(api.POST('/api/v1/rentals/{publicId}/cancel', { params: { path }, body: a.body }))
    case 'extend':
      return unwrap(api.POST('/api/v1/rentals/{publicId}/extensions', { params: { path }, body: a.body }))
  }
}

/** Acciones que reservan, liberan o mueven series (cambian el disponible o la posición). */
const STOCK_ACTIONS = new Set<RentalAction['action']>(['addLines', 'removeLine', 'schedule', 'dispatch', 'cancel'])

export function useRentalAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: runAction,
    onSuccess: (dto, a) => afterWrite(qc, dto, { stock: STOCK_ACTIONS.has(a.action) }),
  })
}

// =====================================================================================================================
// Lote F18 (Rentas F-R2) — devoluciones de renta, cola de proceso de los equipos devueltos, resumen de la lista y reportes.
// Lecturas con `rental.view`; registrar la devolución con `rental.return`; avanzar y completar un proceso con
// `rental.maintenance` y dar de baja además con `inventory.adjust` (los aplica el API; la pantalla esconde lo que no se puede).
// =====================================================================================================================

/** `GET /api/v1/rental-returns` paginado (motivo, renta, cliente, fechas, anticipada, búsqueda). */
export function useRentalReturns(query: RentalReturnListQuery, options?: RentalQueryOptions) {
  return useQuery({
    queryKey: [rentalKeys.returns[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/rental-returns', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Todas las devoluciones filtradas (Exportar), de a 200 hasta 10 000. */
export function exportRentalReturns(query: RentalReturnListQuery) {
  return fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/rental-returns', { params: { query: { ...query, skip, take } } })))
}

/** `GET /api/v1/rental-returns/{publicId}` (ficha de la devolución con sus equipos y el proceso de cada uno). */
export function useRentalReturn(publicId: string | null | undefined, options?: RentalQueryOptions) {
  return useQuery({
    queryKey: [rentalKeys.rentalReturn[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/rental-returns/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** Lo que cambia al devolver un equipo o mover su proceso: rentas, devoluciones, procesos, historial, aviso y existencia. */
async function afterReturnWrite(qc: QueryClient) {
  await Promise.all([
    qc.invalidateQueries({ queryKey: rentalKeys.rental }),
    qc.invalidateQueries({ queryKey: rentalKeys.rentals }),
    qc.invalidateQueries({ queryKey: rentalKeys.returns }),
    qc.invalidateQueries({ queryKey: rentalKeys.rentalReturn }),
    qc.invalidateQueries({ queryKey: rentalKeys.processes }),
    qc.invalidateQueries({ queryKey: rentalKeys.reportRun }),
    qc.invalidateQueries({ queryKey: [catalogKeys.history('', 0)[0]] }),
    qc.invalidateQueries({ queryKey: ATTENTION_QUERY_KEY }),
    invalidateStock(qc),
  ])
}

/** `POST /api/v1/rentals/{publicId}/returns` (`rental.return`): registra la devolución DRN y abre los procesos. */
export function useCreateRentalReturn() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: RentalReturnCreateRequest }) =>
      unwrap(api.POST('/api/v1/rentals/{publicId}/returns', { params: { path: { publicId } }, body })),
    onSuccess: async (dto) => {
      const id = dto.return?.publicId
      if (id) qc.setQueryData([rentalKeys.rentalReturn[0], id], dto)
      await afterReturnWrite(qc)
    },
  })
}

/** `GET /api/v1/rental-processes` paginado (estatus, abiertos/terminados, almacén, producto, búsqueda). */
export function useRentalProcesses(query: RentalProcessListQuery, options?: RentalQueryOptions) {
  return useQuery({
    queryKey: [rentalKeys.processes[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/rental-processes', { params: { query } })),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Todos los procesos filtrados (Exportar). */
export function exportRentalProcesses(query: RentalProcessListQuery) {
  return fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/rental-processes', { params: { query: { ...query, skip, take } } })))
}

/** Acciones sobre un proceso (unión por `action`): avanzar, completar (Lista, con traslado opcional) y dar de baja. */
export type RentalProcessAction =
  | { action: 'advance'; id: number; body: Schemas['RentalProcessAdvanceRequest'] }
  | { action: 'complete'; id: number; body: Schemas['RentalProcessCompleteRequest'] }
  | { action: 'scrap'; id: number; body: Schemas['RentalStatusRequest'] }

function runProcessAction(a: RentalProcessAction) {
  const path = { id: a.id }
  switch (a.action) {
    case 'advance':
      return unwrap(api.POST('/api/v1/rental-processes/{id}/advance', { params: { path }, body: a.body }))
    case 'complete':
      return unwrap(api.POST('/api/v1/rental-processes/{id}/complete', { params: { path }, body: a.body }))
    case 'scrap':
      return unwrap(api.POST('/api/v1/rental-processes/{id}/scrap', { params: { path }, body: a.body }))
  }
}

export function useRentalProcessAction() {
  const qc = useQueryClient()
  return useMutation({ mutationFn: runProcessAction, onSuccess: () => afterReturnWrite(qc) })
}

/**
 * Resumen de la lista de rentas con el API existente (solo `total`, `take=1`): En renta hoy (`status=ON_RENT`), por vencer en 7
 * días (`dueWithinDays=7`, recogido de hoy a hoy + 7) y vencidas (`overdue=true`). Mismo criterio que el aviso y los indicadores.
 */
export function useRentalSummary(dueDays: number) {
  const run = (query: RentalListQuery) => unwrap(api.GET('/api/v1/rentals', { params: { query: { ...query, skip: 0, take: 1 } } })).then((p) => p.total ?? 0)
  return useQuery({
    queryKey: [...rentalKeys.summary, dueDays],
    queryFn: async () => {
      const [onRent, dueSoon, overdue] = await Promise.all([run({ status: ['ON_RENT'] }), run({ dueWithinDays: dueDays }), run({ overdue: true })])
      return { onRent, dueSoon, overdue }
    },
    meta: { handleAccessDenied: false },
  })
}

/** Fuentes de datos de Rentas en Análisis (Lote 29). */
export const RENTAL_SOURCES = ['RENTAL', 'RENTAL_RETURN', 'RENTAL_PROCESS'] as const

export function isRentalSource(code: string | null | undefined): boolean {
  return (RENTAL_SOURCES as readonly string[]).includes((code ?? '').toUpperCase())
}

/**
 * Vistas (reportes) de Análisis sobre las fuentes de Rentas (`GET /api/v1/analytics/reports`, `analytics.view`; el servidor ya
 * oculta las de rentas sin `rental.view` o con el módulo apagado): primero las de sistema, luego las de la compañía, por nombre.
 */
export function useRentalReports(enabled = true) {
  return useQuery({
    queryKey: rentalKeys.reports,
    queryFn: async () => {
      const all = await unwrap(api.GET('/api/v1/analytics/reports'))
      return all
        .filter((r) => isRentalSource(r.baseEntityType) && r.isActive !== false)
        .sort((a, b) => Number(b.isSystem ?? false) - Number(a.isSystem ?? false) || (a.name ?? '').localeCompare(b.name ?? '', 'es'))
    },
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** Corre una vista con el motor de Análisis (`POST /api/v1/analytics/reports/{id}/run`, rango "Todo", hasta 500 filas). */
export function useReportRun(id: number | null | undefined) {
  return useQuery({
    queryKey: [rentalKeys.reportRun[0], id],
    queryFn: () => unwrap(api.POST('/api/v1/analytics/reports/{id}/run', { params: { path: { id: id as number } }, body: { dateRangeMode: 'ALL', take: 500 } })),
    enabled: id != null,
    meta: { handleAccessDenied: false },
  })
}
