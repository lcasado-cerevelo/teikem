// Lote 247 (Lote C) — hooks de «Precios especiales» del cliente y de la cadena (`/api/v1/special-prices`, módulo SALES_ORDERS). Ver: sales.view|sales.pricelist;
// escribir: sales.pricelist. Toda escritura responde la vista completa (`SpecialPricesDto`): se deja en la caché del dueño. Todo llega en una respuesta (sin paginar).
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type S = components['schemas']
export type SpecialPricesDto = S['SpecialPricesDto']
export type SpecialPriceRowDto = S['SpecialPriceRowDto']
export type SpecialPriceListRefDto = S['SpecialPriceListRefDto']
export type SpecialPriceRequest = S['SpecialPriceRequest']
export type SpecialPricePatchRequest = S['SpecialPricePatchRequest']

export type SpecialOwnerKind = 'client' | 'chain'

export const SPECIAL_VIEW = 'sales.view|sales.pricelist'
export const SPECIAL_MANAGE = 'sales.pricelist'

export const specialKeys = {
  all: ['special-prices'] as const,
  owner: (kind: SpecialOwnerKind, id: string, includeExpired: boolean) => ['special-prices', kind, id, includeExpired] as const,
}

export function useSpecialPrices(kind: SpecialOwnerKind, ownerPublicId: string, includeExpired: boolean) {
  return useQuery({
    queryKey: specialKeys.owner(kind, ownerPublicId, includeExpired),
    queryFn: () =>
      kind === 'client'
        ? unwrap(api.GET('/api/v1/special-prices/clients/{clientPublicId}', { params: { path: { clientPublicId: ownerPublicId }, query: { includeExpired } } }))
        : unwrap(api.GET('/api/v1/special-prices/chains/{chainPublicId}', { params: { path: { chainPublicId: ownerPublicId }, query: { includeExpired } } })),
    enabled: Boolean(ownerPublicId),
    meta: { handleAccessDenied: false },
  })
}

/** Alta, cambio y quitar: la vista devuelta queda en caché y se vuelven a pedir las demás vistas (la de la cadena en el cliente, las listas). */
export function useSpecialPriceActions(kind: SpecialOwnerKind, ownerPublicId: string, includeExpired: boolean) {
  const qc = useQueryClient()
  const onSuccess = (dto: SpecialPricesDto) => {
    qc.setQueryData(specialKeys.owner(kind, ownerPublicId, includeExpired), dto)
    void qc.invalidateQueries({ queryKey: specialKeys.all, predicate: (q) => q.queryKey[3] !== includeExpired || q.queryKey[2] !== ownerPublicId })
    void qc.invalidateQueries({ queryKey: ['/api/v1/price-lists'] })
    void qc.invalidateQueries({ queryKey: ['/api/v1/sales-orders/catalog'] })
  }
  const query = { includeExpired }
  return {
    add: useMutation({
      mutationFn: (body: SpecialPriceRequest) =>
        kind === 'client'
          ? unwrap(api.POST('/api/v1/special-prices/clients/{clientPublicId}', { params: { path: { clientPublicId: ownerPublicId }, query }, body }))
          : unwrap(api.POST('/api/v1/special-prices/chains/{chainPublicId}', { params: { path: { chainPublicId: ownerPublicId }, query }, body })),
      onSuccess,
    }),
    update: useMutation({
      mutationFn: ({ itemId, body }: { itemId: number; body: SpecialPricePatchRequest }) =>
        kind === 'client'
          ? unwrap(api.PATCH('/api/v1/special-prices/clients/{clientPublicId}/{itemId}', { params: { path: { clientPublicId: ownerPublicId, itemId }, query }, body }))
          : unwrap(api.PATCH('/api/v1/special-prices/chains/{chainPublicId}/{itemId}', { params: { path: { chainPublicId: ownerPublicId, itemId }, query }, body })),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (itemId: number) =>
        kind === 'client'
          ? unwrap(api.DELETE('/api/v1/special-prices/clients/{clientPublicId}/{itemId}', { params: { path: { clientPublicId: ownerPublicId, itemId }, query } }))
          : unwrap(api.DELETE('/api/v1/special-prices/chains/{chainPublicId}/{itemId}', { params: { path: { chainPublicId: ownerPublicId, itemId }, query } })),
      onSuccess,
    }),
  }
}
