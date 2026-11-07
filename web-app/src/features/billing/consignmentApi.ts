// Datos de la pestaña «Consignación» de Facturación (Lote 224, P6): facturas con producto dejado en consignación (dejado, devuelto, pagado, saldo),
// el reporte «Consignado por cliente», registrar un pago y anularlo. Anular lleva [RequireAal2]: el cliente del API reautentica solo.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { billingKeys } from './billingApi'

type Schemas = components['schemas']
export type ConsignmentInvoiceListItemDto = Schemas['ConsignmentInvoiceListItemDto']
export type ConsignmentInvoiceDto = Schemas['ConsignmentInvoiceDto']
export type ConsignmentLineDto = Schemas['ConsignmentLineDto']
export type ConsignmentPaymentDto = Schemas['ConsignmentPaymentDto']
export type ConsignmentPaymentRequest = Schemas['ConsignmentPaymentRequest']
export type ConsignmentByClientDto = Schemas['ConsignmentByClientDto']
export type ConsignmentClientRowDto = Schemas['ConsignmentClientRowDto']

export type BalanceFilter = 'OPEN' | 'PAID' | 'ALL'

export const consignmentKeys = {
  invoices: (balance: string, client: string, q: string, skip: number, take: number) => [...billingKeys.all, 'consignment', 'invoices', balance, client, q, skip, take] as const,
  invoice: (id: string | null) => [...billingKeys.all, 'consignment', 'invoice', id] as const,
  byClient: [...billingKeys.all, 'consignment', 'by-client'] as const,
}

export function useConsignmentInvoices(balance: BalanceFilter, clientPublicId: string | null, q: string, skip: number, take: number, enabled = true) {
  return useQuery({
    queryKey: consignmentKeys.invoices(balance, clientPublicId ?? '', q, skip, take),
    queryFn: () =>
      unwrap(api.GET('/api/v1/billing/consignment/invoices', { params: { query: { balance, clientPublicId: clientPublicId ?? undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
    enabled,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function consignmentPageReader(balance: BalanceFilter, clientPublicId: string | null, q: string) {
  return (skip: number, take: number) =>
    unwrap(api.GET('/api/v1/billing/consignment/invoices', { params: { query: { balance, clientPublicId: clientPublicId ?? undefined, q: q || undefined, skip, take } } }))
}

export function useConsignmentInvoice(publicId: string | null) {
  return useQuery({
    queryKey: consignmentKeys.invoice(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/billing/consignment/invoices/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useConsignmentByClient() {
  return useQuery({ queryKey: consignmentKeys.byClient, queryFn: () => unwrap(api.GET('/api/v1/billing/consignment/by-client')) })
}

export function useConsignmentMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: billingKeys.all })
  return {
    pay: useMutation({
      mutationFn: (v: { id: string; body: ConsignmentPaymentRequest }) =>
        unwrap(api.POST('/api/v1/billing/consignment/invoices/{publicId}/payments', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    voidPayment: useMutation({
      mutationFn: (v: { paymentId: string; reason: string }) =>
        unwrap(api.POST('/api/v1/billing/consignment/payments/{publicId}/void', { params: { path: { publicId: v.paymentId } }, body: { reason: v.reason } })),
      onSettled: refresh,
    }),
  }
}
