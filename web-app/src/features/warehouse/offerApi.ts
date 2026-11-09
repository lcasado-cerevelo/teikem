// Lote 229 — hooks de las OFERTAS (`/api/v1/offers`, aparte de las listas de precios) y de la foto del producto (`/api/v1/products/{id}/image`).
// Ofertas: lectura sales.view, escritura sales.pricelist (módulo SALES_ORDERS). Foto del producto: lectura inventory.view o sales.view, escritura inventory.manage.
// Toda respuesta de cambio de una oferta trae la oferta completa (con sus `warnings`): se deja en la caché del detalle y se vuelve a pedir la lista.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type S = components['schemas']
export type OfferListItemDto = S['OfferListItemDto']
export type OfferDto = S['OfferDto']
export type OfferItemDto = S['OfferItemDto']
export type OfferWarningDto = S['OfferWarningDto']
export type OfferRequest = S['OfferRequest']
export type OfferKitRequest = S['OfferKitRequest']
export type OfferKitUpdateRequest = S['OfferKitUpdateRequest']
export type ProductImageDto = S['ProductImageDto']

export const offerKeys = {
  list: ['/api/v1/offers'] as const,
  detail: (publicId: string | null | undefined) => ['/api/v1/offers', 'detail', publicId ?? ''] as const,
}

export function useOfferList(query: { q?: string; state?: string; includeInactive?: boolean }) {
  return useQuery({
    queryKey: [...offerKeys.list, 'list', query],
    queryFn: () => unwrap(api.GET('/api/v1/offers', { params: { query } })),
    meta: { handleAccessDenied: false },
  })
}

export function useOffer(publicId: string | null | undefined) {
  return useQuery({
    queryKey: offerKeys.detail(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/offers/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId),
    meta: { handleAccessDenied: false },
  })
}

/** Un archivo en multipart (campo `file`), como las demás fotos. */
function fileForm(file: File) {
  const form = new FormData()
  form.append('file', file)
  return { body: {}, bodySerializer: () => form }
}

export function useOfferActions() {
  const qc = useQueryClient()
  const after = (dto: OfferDto | undefined) => {
    if (dto?.publicId) qc.setQueryData(offerKeys.detail(dto.publicId), dto)
    void qc.invalidateQueries({ queryKey: [...offerKeys.list, 'list'] })
  }
  const opts = { onSuccess: after }
  const path = (publicId: string) => ({ params: { path: { publicId } } })
  const item = (publicId: string, itemId: number) => ({ params: { path: { publicId, itemId } } })
  return {
    create: useMutation({ mutationFn: (body: OfferRequest) => unwrap(api.POST('/api/v1/offers', { body })), ...opts }),
    update: useMutation({
      mutationFn: ({ publicId, body }: { publicId: string; body: OfferRequest }) => unwrap(api.PATCH('/api/v1/offers/{publicId}', { ...path(publicId), body })),
      ...opts,
    }),
    publish: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/offers/{publicId}/publish', path(publicId))), ...opts }),
    unpublish: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/offers/{publicId}/unpublish', path(publicId))), ...opts }),
    deactivate: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/offers/{publicId}/deactivate', path(publicId))), ...opts }),
    reactivate: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/offers/{publicId}/reactivate', path(publicId))), ...opts }),
    uploadImage: useMutation({
      mutationFn: ({ publicId, file }: { publicId: string; file: File }) => unwrap(api.POST('/api/v1/offers/{publicId}/image', { ...path(publicId), ...fileForm(file) })),
      ...opts,
    }),
    removeImage: useMutation({ mutationFn: (publicId: string) => unwrap(api.DELETE('/api/v1/offers/{publicId}/image', path(publicId))), ...opts }),
    addItems: useMutation({
      mutationFn: ({ publicId, items }: { publicId: string; items: { productPublicId: string; price: number }[] }) =>
        unwrap(api.POST('/api/v1/offers/{publicId}/items', { ...path(publicId), body: { items } })),
      ...opts,
    }),
    setItemPrice: useMutation({
      mutationFn: ({ publicId, itemId, price }: { publicId: string; itemId: number; price: number }) =>
        unwrap(api.PATCH('/api/v1/offers/{publicId}/items/{itemId}', { ...item(publicId, itemId), body: { price } })),
      ...opts,
    }),
    removeItem: useMutation({
      mutationFn: ({ publicId, itemId }: { publicId: string; itemId: number }) => unwrap(api.DELETE('/api/v1/offers/{publicId}/items/{itemId}', item(publicId, itemId))),
      ...opts,
    }),
    uploadItemImage: useMutation({
      mutationFn: ({ publicId, itemId, file }: { publicId: string; itemId: number; file: File }) =>
        unwrap(api.POST('/api/v1/offers/{publicId}/items/{itemId}/image', { ...item(publicId, itemId), ...fileForm(file) })),
      ...opts,
    }),
    removeItemImage: useMutation({
      mutationFn: ({ publicId, itemId }: { publicId: string; itemId: number }) => unwrap(api.DELETE('/api/v1/offers/{publicId}/items/{itemId}/image', item(publicId, itemId))),
      ...opts,
    }),
    addClients: useMutation({
      mutationFn: ({ publicId, clientPublicIds }: { publicId: string; clientPublicIds: string[] }) =>
        unwrap(api.POST('/api/v1/offers/{publicId}/clients', { ...path(publicId), body: { clientPublicIds } })),
      ...opts,
    }),
    removeClient: useMutation({
      mutationFn: ({ publicId, clientPublicId }: { publicId: string; clientPublicId: string }) =>
        unwrap(api.DELETE('/api/v1/offers/{publicId}/clients/{clientPublicId}', { params: { path: { publicId, clientPublicId } } })),
      ...opts,
    }),
    /** Kit creado para la oferta (nace con su renglón y su precio); responde el kit y la oferta. */
    createKit: useMutation({
      mutationFn: ({ publicId, body }: { publicId: string; body: OfferKitRequest }) => unwrap(api.POST('/api/v1/offers/{publicId}/kits', { ...path(publicId), body })),
      onSuccess: (dto) => after(dto.offer),
    }),
    updateKit: useMutation({
      mutationFn: ({ publicId, productPublicId, body }: { publicId: string; productPublicId: string; body: OfferKitUpdateRequest }) =>
        unwrap(api.PUT('/api/v1/offers/{publicId}/kits/{productPublicId}', { params: { path: { publicId, productPublicId } }, body })),
      ...opts,
    }),
  }
}

// ---- Foto del producto (Lote 229) ----
export const productImageKey = (publicId: string | null | undefined) => ['/api/v1/products/image', publicId ?? ''] as const

export function useProductImage(publicId: string | null | undefined) {
  return useQuery({
    queryKey: productImageKey(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}/image', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: Boolean(publicId),
    meta: { handleAccessDenied: false },
  })
}

export function useProductImageActions(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = (dto: ProductImageDto) => {
    qc.setQueryData(productImageKey(publicId), dto)
    // la foto efectiva de las listas, las ofertas y el catálogo del pedido puede cambiar
    void qc.invalidateQueries({ queryKey: ['/api/v1/price-lists'] })
    void qc.invalidateQueries({ queryKey: offerKeys.list })
    void qc.invalidateQueries({ queryKey: ['/api/v1/sales-orders/catalog'] })
  }
  const path = { params: { path: { publicId } } }
  return {
    upload: useMutation({ mutationFn: (file: File) => unwrap(api.POST('/api/v1/products/{publicId}/image', { ...path, ...fileForm(file) })), onSuccess }),
    remove: useMutation({ mutationFn: () => unwrap(api.DELETE('/api/v1/products/{publicId}/image', path)), onSuccess }),
  }
}
