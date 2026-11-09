// Lote 223 (P3) — datos del pedido guiado: el catálogo por marca con los precios del cliente, el borrador por pasos (POST/PATCH con `step`), la firma de quien
// autoriza (archivo PNG), «Enviar pedido» con la contraseña (POST /submit), la comprobación de inventario y crédito, la búsqueda de clientes y el alta mínima del
// cliente nuevo con sus fotos. Permisos: catálogo sales.view; borrador, firma y enviar sales.create; alta mínima clients.create_basic (o clients.create).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../../kernel/api/client'
import type { components } from '../../../kernel/api/schema'
import { clientKeys } from '../../clients/api'
import { salesKeys, type SalesOrderRequest } from '../salesApi'

type Schemas = components['schemas']
export type SalesCatalogDto = Schemas['SalesCatalogDto']
export type SalesCatalogBrandDto = Schemas['SalesCatalogBrandDto']
export type SalesCatalogItemDto = Schemas['SalesCatalogItemDto']
export type ClientBasicCreateRequest = Schemas['ClientBasicCreateRequest']
export type ClientListItemDto = Schemas['ClientListItemDto']
export type SalesOrderSubmitRequest = Schemas['SalesOrderSubmitRequest']

export const wizardKeys = {
  catalog: (clientPublicId: string | null) => ['/api/v1/sales-orders/catalog', clientPublicId ?? ''] as const,
  clients: (q: string) => ['/api/v1/clients', 'wizard', q] as const,
}

/** Catálogo del pedido: marcas con sus ofertas vigentes y la lista completa, con el precio ya resuelto para el cliente. */
export function useSalesCatalog(clientPublicId: string | null) {
  return useQuery({
    queryKey: wizardKeys.catalog(clientPublicId),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders/catalog', { params: { query: { clientPublicId: clientPublicId ?? undefined } } })),
    enabled: !!clientPublicId,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

/** Buscador de clientes del paso 1 (número o nombre; sin texto no consulta). */
export function useClientSearch(q: string) {
  return useQuery({
    queryKey: wizardKeys.clients(q),
    queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query: { search: q, includeInactive: false } } })),
    // se consulta siempre (también sin texto): la lista se despliega al enfocar el buscador y, con un solo cliente, se escoge solo
    enabled: true,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

export function useWizardMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: salesKeys.all })
  return {
    create: useMutation({ mutationFn: (body: SalesOrderRequest) => unwrap(api.POST('/api/v1/sales-orders', { body })), onSettled: refresh }),
    update: useMutation({
      mutationFn: (v: { id: string; body: SalesOrderRequest }) => unwrap(api.PATCH('/api/v1/sales-orders/{publicId}', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    /** La firma dibujada (PNG) y el nombre de quien autoriza. */
    signature: useMutation({
      mutationFn: (v: { id: string; file: Blob; authorizedByName: string }) => {
        const form = new FormData()
        form.append('file', v.file, 'firma.png')
        form.append('authorizedByName', v.authorizedByName)
        return unwrap(api.POST('/api/v1/sales-orders/{publicId}/signature', { params: { path: { publicId: v.id } }, body: {}, bodySerializer: () => form }))
      },
    }),
    removeSignature: useMutation({
      mutationFn: (id: string) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/signature/remove', { params: { path: { publicId: id } } })),
    }),
    availability: useMutation({ mutationFn: (id: string) => unwrap(api.GET('/api/v1/sales-orders/{publicId}/availability', { params: { path: { publicId: id } } })) }),
    submit: useMutation({
      mutationFn: (v: { id: string; body: SalesOrderSubmitRequest }) => unwrap(api.POST('/api/v1/sales-orders/{publicId}/submit', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    createClient: useMutation({
      mutationFn: (body: ClientBasicCreateRequest) => unwrap(api.POST('/api/v1/clients/basic', { body })),
      onSuccess: (detail) => {
        if (detail.publicId) qc.setQueryData(clientKeys.detail(detail.publicId), detail)
        void qc.invalidateQueries({ queryKey: clientKeys.list })
      },
    }),
    uploadClientDoc: useMutation({
      mutationFn: (v: { clientPublicId: string; file: File; documentType: string }) => {
        const form = new FormData()
        form.append('file', v.file)
        form.append('documentType', v.documentType)
        return unwrap(api.POST('/api/v1/clients/{publicId}/documents', { params: { path: { publicId: v.clientPublicId } }, body: {}, bodySerializer: () => form }))
      },
    }),
  }
}
