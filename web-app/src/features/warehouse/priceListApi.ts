// Lote 221 (P1) — hooks de listas de precios y ofertas (`/api/v1/price-lists`, `/api/v1/products/{id}/promo`). Todo con el cliente generado.
// Lectura: sales.view; escritura: sales.pricelist. Las mutaciones de una lista devuelven la lista completa: se deja en la caché del detalle.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import type { PriceListRequest, ProductPromoRequest } from './priceListRules'

type S = components['schemas']
export type PriceListListItemDto = S['PriceListListItemDto']
export type PriceListDto = S['PriceListDto']
export type PriceListItemDto = S['PriceListItemDto']
export type PriceListClientDto = S['PriceListClientDto']
export type ProductPromoDto = S['ProductPromoDto']

export const priceListKeys = {
  list: ['/api/v1/price-lists'] as const,
  detail: ['/api/v1/price-lists', 'detail'] as const,
  offers: ['/api/v1/price-lists/offers'] as const,
}

export function usePriceLists(query: { q?: string; state?: string; includeInactive?: boolean }) {
  return useQuery({
    queryKey: [...priceListKeys.list, query],
    queryFn: () => unwrap(api.GET('/api/v1/price-lists', { params: { query } })),
    meta: { handleAccessDenied: false },
  })
}

export function usePriceList(publicId: string | null | undefined) {
  return useQuery({
    queryKey: [...priceListKeys.detail, publicId],
    queryFn: () => unwrap(api.GET('/api/v1/price-lists/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId),
    meta: { handleAccessDenied: false },
  })
}

export function useOffers(includeInactive: boolean) {
  return useQuery({
    queryKey: [...priceListKeys.offers, includeInactive],
    queryFn: () => unwrap(api.GET('/api/v1/price-lists/offers', { params: { query: { includeInactive } } })),
    meta: { handleAccessDenied: false },
  })
}

/** Después de cualquier cambio: la lista devuelta queda en caché y la tabla y las ofertas se vuelven a pedir. */
function useAfterChange() {
  const qc = useQueryClient()
  return (dto: PriceListDto) => {
    qc.setQueryData([...priceListKeys.detail, dto.publicId], dto)
    void qc.invalidateQueries({ queryKey: priceListKeys.list, predicate: (q) => q.queryKey[1] !== 'detail' })
    void qc.invalidateQueries({ queryKey: priceListKeys.offers })
  }
}

export function usePriceListActions() {
  const after = useAfterChange()
  const opts = { onSuccess: after }
  const path = (publicId: string) => ({ params: { path: { publicId } } })
  return {
    create: useMutation({ mutationFn: (body: PriceListRequest) => unwrap(api.POST('/api/v1/price-lists', { body })), ...opts }),
    update: useMutation({
      mutationFn: ({ publicId, body }: { publicId: string; body: PriceListRequest }) => unwrap(api.PATCH('/api/v1/price-lists/{publicId}', { ...path(publicId), body })),
      ...opts,
    }),
    publish: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/price-lists/{publicId}/publish', path(publicId))), ...opts }),
    unpublish: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/price-lists/{publicId}/unpublish', path(publicId))), ...opts }),
    deactivate: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/price-lists/{publicId}/deactivate', path(publicId))), ...opts }),
    reactivate: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/price-lists/{publicId}/reactivate', path(publicId))), ...opts }),
    addItems: useMutation({
      mutationFn: ({ publicId, items }: { publicId: string; items: { productPublicId: string; price: number }[] }) =>
        unwrap(api.POST('/api/v1/price-lists/{publicId}/items', { ...path(publicId), body: { items } })),
      ...opts,
    }),
    setItemPrice: useMutation({
      mutationFn: ({ publicId, itemId, price }: { publicId: string; itemId: number; price: number }) =>
        unwrap(api.PATCH('/api/v1/price-lists/{publicId}/items/{itemId}', { params: { path: { publicId, itemId } }, body: { price } })),
      ...opts,
    }),
    removeItem: useMutation({
      mutationFn: ({ publicId, itemId }: { publicId: string; itemId: number }) => unwrap(api.DELETE('/api/v1/price-lists/{publicId}/items/{itemId}', { params: { path: { publicId, itemId } } })),
      ...opts,
    }),
    uploadItemImage: useMutation({
      mutationFn: ({ publicId, itemId, file }: { publicId: string; itemId: number; file: File }) => {
        const form = new FormData()
        form.append('file', file)
        return unwrap(api.POST('/api/v1/price-lists/{publicId}/items/{itemId}/image', { params: { path: { publicId, itemId } }, body: {}, bodySerializer: () => form }))
      },
      ...opts,
    }),
    removeItemImage: useMutation({
      mutationFn: ({ publicId, itemId }: { publicId: string; itemId: number }) => unwrap(api.DELETE('/api/v1/price-lists/{publicId}/items/{itemId}/image', { params: { path: { publicId, itemId } } })),
      ...opts,
    }),
    addClients: useMutation({
      mutationFn: ({ publicId, clientPublicIds }: { publicId: string; clientPublicIds: string[] }) =>
        unwrap(api.POST('/api/v1/price-lists/{publicId}/clients', { ...path(publicId), body: { clientPublicIds } })),
      ...opts,
    }),
    removeClient: useMutation({
      mutationFn: ({ publicId, clientPublicId }: { publicId: string; clientPublicId: string }) =>
        unwrap(api.DELETE('/api/v1/price-lists/{publicId}/clients/{clientPublicId}', { params: { path: { publicId, clientPublicId } } })),
      ...opts,
    }),
  }
}

// ---- Oferta (promoción) de un kit ----
export const promoKey = (publicId: string | null | undefined) => ['/api/v1/products/promo', publicId] as const

export function useProductPromo(publicId: string | null | undefined) {
  return useQuery({
    queryKey: promoKey(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}/promo', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId),
    meta: { handleAccessDenied: false },
  })
}

export function useProductPromoActions(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = (dto: ProductPromoDto) => {
    qc.setQueryData(promoKey(publicId), dto)
    void qc.invalidateQueries({ queryKey: priceListKeys.offers })
  }
  const path = { params: { path: { publicId } } }
  return {
    save: useMutation({ mutationFn: (body: ProductPromoRequest) => unwrap(api.PUT('/api/v1/products/{publicId}/promo', { ...path, body })), onSuccess }),
    uploadImage: useMutation({
      mutationFn: (file: File) => {
        const form = new FormData()
        form.append('file', file)
        return unwrap(api.POST('/api/v1/products/{publicId}/promo/image', { ...path, body: {}, bodySerializer: () => form }))
      },
      onSuccess,
    }),
    removeImage: useMutation({ mutationFn: () => unwrap(api.DELETE('/api/v1/products/{publicId}/promo/image', path)), onSuccess }),
  }
}
