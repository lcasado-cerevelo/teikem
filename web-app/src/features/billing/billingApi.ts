// Datos de «Facturación» (Lote 73, módulo BILLING): ajustes, corridas (generar, revisar, aprobar, cancelar) y facturas (lista, ficha, ajustar una línea, anular).
// Aprobar y anular llevan [RequireAal2] en el servidor: el cliente del API abre solo el modal de reautenticación al recibir 403 aal2_required y reintenta (kernel/auth).
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type BillingRunDto = Schemas['BillingRunDto']
export type BillingRunListItemDto = Schemas['BillingRunListItemDto']
export type BillingRunRequest = Schemas['BillingRunRequest']
export type BillingSettingsDto = Schemas['BillingSettingsDto']
export type BillingSettingsRequest = Schemas['BillingSettingsRequest']
export type InvoiceDto = Schemas['InvoiceDto']
export type InvoiceListItemDto = Schemas['InvoiceListItemDto']
export type InvoiceLineDto = Schemas['InvoiceLineDto']
export type PaymentDto = Schemas['PaymentDto']
export type PaymentRequest = Schemas['PaymentRequest']
export type AgingDto = Schemas['AgingDto']
export type ReceivableRowDto = Schemas['ReceivableRowDto']
export type ReceivablesDto = Schemas['ReceivablesDto']
export type StatementDto = Schemas['StatementDto']
export type StatementLineDto = Schemas['StatementLineDto']

export const billingKeys = {
  all: ['billing'] as const,
  settings: ['billing', 'settings'] as const,
  runs: (status: string) => ['billing', 'runs', status] as const,
  run: (id: number | null) => ['billing', 'run', id] as const,
  invoices: (status: string, q: string, skip: number, take: number) => ['billing', 'invoices', status, q, skip, take] as const,
  invoice: (id: string | null) => ['billing', 'invoice', id] as const,
  receivables: ['billing', 'receivables'] as const,
  statement: (clientId: string | null, from: string, to: string) => ['billing', 'statement', clientId, from, to] as const,
}

export function useBillingSettings() {
  return useQuery({ queryKey: billingKeys.settings, queryFn: () => unwrap(api.GET('/api/v1/billing/settings')) })
}

export function useUpdateBillingSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: BillingSettingsRequest) => unwrap(api.PUT('/api/v1/billing/settings', { body })),
    onSuccess: (data) => qc.setQueryData(billingKeys.settings, data),
  })
}

export function useRuns(status: string) {
  return useQuery({ queryKey: billingKeys.runs(status), queryFn: () => unwrap(api.GET('/api/v1/billing/runs', { params: { query: { status: status || undefined } } })) })
}

export function useRun(id: number | null) {
  return useQuery({
    queryKey: billingKeys.run(id),
    queryFn: () => unwrap(api.GET('/api/v1/billing/runs/{id}', { params: { path: { id: id! } } })),
    enabled: id !== null,
  })
}

export function useRunMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: billingKeys.all })
  return {
    generate: useMutation({ mutationFn: (body: BillingRunRequest) => unwrap(api.POST('/api/v1/billing/runs', { body })), onSettled: refresh }),
    review: useMutation({ mutationFn: (id: number) => unwrap(api.POST('/api/v1/billing/runs/{id}/review', { params: { path: { id } } })), onSettled: refresh }),
    approve: useMutation({ mutationFn: (id: number) => unwrap(api.POST('/api/v1/billing/runs/{id}/approve', { params: { path: { id } } })), onSettled: refresh }),
    cancel: useMutation({
      mutationFn: (v: { id: number; comment?: string }) => unwrap(api.POST('/api/v1/billing/runs/{id}/cancel', { params: { path: { id: v.id } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
  }
}

export function useInvoices(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: billingKeys.invoices(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/billing/invoices', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function invoicePageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/billing/invoices', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } }))
}

export function useInvoice(publicId: string | null) {
  return useQuery({
    queryKey: billingKeys.invoice(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/billing/invoices/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useInvoiceMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: billingKeys.all })
  return {
    adjust: useMutation({
      mutationFn: (v: { id: string; lineId: number; unitAmount: number; note: string }) =>
        unwrap(api.PATCH('/api/v1/billing/invoices/{publicId}/lines/{lineId}', { params: { path: { publicId: v.id, lineId: v.lineId } }, body: { unitAmount: v.unitAmount, note: v.note } })),
      onSettled: refresh,
    }),
    voidInvoice: useMutation({
      mutationFn: (v: { id: string; comment?: string }) => unwrap(api.POST('/api/v1/billing/invoices/{publicId}/void', { params: { path: { publicId: v.id } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
  }
}

// ---------------------------------------------------------------- pagos, cuentas por cobrar y estado de cuenta (Lote 76)

export function usePaymentMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: billingKeys.all })
  return {
    record: useMutation({
      mutationFn: (v: { id: string; body: PaymentRequest }) => unwrap(api.POST('/api/v1/billing/invoices/{publicId}/payments', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    // anular un pago lleva [RequireAal2]: el cliente del API reautentica solo
    voidPayment: useMutation({
      mutationFn: (v: { paymentId: number; reason: string }) => unwrap(api.POST('/api/v1/billing/payments/{id}/void', { params: { path: { id: v.paymentId } }, body: { reason: v.reason } })),
      onSettled: refresh,
    }),
  }
}

export function useReceivables() {
  return useQuery({ queryKey: billingKeys.receivables, queryFn: () => unwrap(api.GET('/api/v1/billing/receivables')) })
}

export function useStatement(clientId: string | null, from: string, to: string) {
  return useQuery({
    queryKey: billingKeys.statement(clientId, from, to),
    queryFn: () => unwrap(api.GET('/api/v1/billing/clients/{publicId}/statement', { params: { path: { publicId: clientId! }, query: { from, to } } })),
    enabled: clientId !== null && from !== '' && to !== '' && from <= to,
  })
}
