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
