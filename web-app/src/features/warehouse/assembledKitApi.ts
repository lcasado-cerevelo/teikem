// Lote 232 — datos del kit ARMADO (`/api/v1/products/{publicId}/assembled-kit`, módulo WMS_LOTSERIAL). Lecturas con `inventory.view`; la
// composición se cambia con `inventory.manage`; armar y desarmar con `inventory.adjust`. Armar y desarmar mueven inventario: invalidan todo lo que
// depende de la existencia (`invalidateStock`) además del kit y su historial. Reglas puras en `assembledKitRules.ts`.
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { invalidateStock, type WarehouseQueryOptions } from './api'

type Schemas = components['schemas']

export const assembledKitKeys = {
  kit: ['/api/v1/products/{publicId}/assembled-kit'],
  assemblies: ['/api/v1/products/{publicId}/assembled-kit/assemblies'],
} as const

const meta = (options?: WarehouseQueryOptions) => (options?.handleAccessDenied === false ? { handleAccessDenied: false } : undefined)

/** `GET /products/{id}/assembled-kit`: composición, si ya no se cambia (`compositionLocked`) y «se pueden armar» por almacén. */
export function useAssembledKit(publicId: string | null | undefined, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [assembledKitKeys.kit[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}/assembled-kit', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    meta: meta(options),
  })
}

/** Una página del historial de armados (más reciente primero; `take` ≤ 200). */
export function kitAssembliesPage(publicId: string) {
  return (skip: number, take: number) =>
    unwrap(api.GET('/api/v1/products/{publicId}/assembled-kit/assemblies', { params: { path: { publicId }, query: { skip, take } } })).then((r) => ({
      items: r.items ?? [],
      total: r.total ?? 0,
    }))
}

/** `GET /products/{id}/assembled-kit/assemblies?skip&take` (paginado por el servidor). */
export function useKitAssemblies(publicId: string | null | undefined, query: { skip: number; take: number }, options?: WarehouseQueryOptions) {
  return useQuery({
    queryKey: [assembledKitKeys.assemblies[0], publicId, query],
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}/assembled-kit/assemblies', { params: { path: { publicId: publicId ?? '' }, query } })),
    enabled: Boolean(publicId) && (options?.enabled ?? true),
    placeholderData: keepPreviousData,
    meta: meta(options),
  })
}

/** Lo que cambia al armar o desarmar: el kit, su historial y toda la existencia (saldos, Kárdex, productos, lotes…). */
export function invalidateAssembledKit(qc: QueryClient) {
  return Promise.all([
    qc.invalidateQueries({ queryKey: assembledKitKeys.kit }),
    qc.invalidateQueries({ queryKey: assembledKitKeys.assemblies }),
    invalidateStock(qc),
  ])
}

/** `PUT /products/{id}/assembled-kit` (`inventory.manage`): reemplaza la composición (solo si el kit no tiene armados, movimientos ni existencia). */
export function useSetKitComposition() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['AssembledKitSetRequest'] }) =>
      unwrap(api.PUT('/api/v1/products/{publicId}/assembled-kit', { params: { path: { publicId } }, body })),
    onSuccess: (dto, { publicId }) => {
      qc.setQueryData([assembledKitKeys.kit[0], publicId], dto)
      return invalidateStock(qc)
    },
  })
}

/** `POST /products/{id}/assembled-kit/assemblies` (`inventory.adjust`): arma N kits (salen las piezas, entran los kits). */
export function useAssembleKit() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: Schemas['KitAssembleRequest'] }) =>
      unwrap(api.POST('/api/v1/products/{publicId}/assembled-kit/assemblies', { params: { path: { publicId } }, body })),
    onSuccess: () => invalidateAssembledKit(qc),
  })
}

/** `POST /products/{id}/assembled-kit/assemblies/{assemblyId}/disassemble` (`inventory.adjust`): desarma N kits de ese armado. */
export function useDisassembleKit() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, assemblyPublicId, body }: { publicId: string; assemblyPublicId: string; body: Schemas['KitDisassembleRequest'] }) =>
      unwrap(
        api.POST('/api/v1/products/{publicId}/assembled-kit/assemblies/{assemblyPublicId}/disassemble', {
          params: { path: { publicId, assemblyPublicId } },
          body,
        }),
      ),
    onSuccess: () => invalidateAssembledKit(qc),
  })
}

/** `GET /inventory/exit-options` de VARIAS piezas en un almacén (en el orden de rotación del servidor: rank 1 = la que sale primero). */
export function usePiecesExitOptions(warehousePublicId: string | null | undefined, productPublicIds: readonly string[], options?: WarehouseQueryOptions) {
  const ids = [...productPublicIds].sort()
  return useQuery({
    queryKey: ['/api/v1/inventory/balances', 'exit-options', warehousePublicId, ids],
    queryFn: () =>
      unwrap(api.GET('/api/v1/inventory/exit-options', { params: { query: { warehousePublicId: warehousePublicId ?? undefined, productPublicIds: ids, take: 500 } } })),
    enabled: Boolean(warehousePublicId) && ids.length > 0 && (options?.enabled ?? true),
    meta: meta(options),
  })
}
