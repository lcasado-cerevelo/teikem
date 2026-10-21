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
export type ExportTemplateDto = Schemas['ExportTemplateDto']
export type ExportTemplateRequest = Schemas['ExportTemplateRequest']
export type ExportFieldDto = Schemas['ExportFieldDto']
export type ExportFileDto = Schemas['ExportFileDto']
export type CreditNoteDto = Schemas['CreditNoteDto']
export type CreditNoteListItemDto = Schemas['CreditNoteListItemDto']
export type CreditNoteLineDto = Schemas['CreditNoteLineDto']
export type CreditNoteRequest = Schemas['CreditNoteRequest']

export const billingKeys = {
  all: ['billing'] as const,
  settings: ['billing', 'settings'] as const,
  runs: (status: string) => ['billing', 'runs', status] as const,
  run: (id: number | null) => ['billing', 'run', id] as const,
  invoices: (status: string, q: string, skip: number, take: number) => ['billing', 'invoices', status, q, skip, take] as const,
  invoice: (id: string | null) => ['billing', 'invoice', id] as const,
  receivables: ['billing', 'receivables'] as const,
  templates: ['billing', 'export-templates'] as const,
  creditNotes: (status: string, q: string, skip: number, take: number) => ['billing', 'credit-notes', status, q, skip, take] as const,
  creditNote: (id: string | null) => ['billing', 'credit-note', id] as const,
  fields: ['billing', 'export-fields'] as const,
  preview: (runId: number, templateId: string | null) => ['billing', 'export-preview', runId, templateId] as const,
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

// ---------------------------------------------------------------- plantillas de exportación y archivo de una corrida (Lote 78)

/** Plantillas de exportación del alcance (Lote 181: también las de contabilización de compras y de despachos, PURCHASE_ACCOUNTING / DISPATCH_ACCOUNTING). Sin alcance, las de facturación. */
export function useExportTemplates(scope = 'BILLING', enabled = true) {
  return useQuery({
    queryKey: scope === 'BILLING' ? billingKeys.templates : [...billingKeys.templates, scope],
    queryFn: () => unwrap(api.GET('/api/v1/export-templates', { params: { query: { scope } } })),
    enabled,
  })
}

export function useExportFields(scope = 'BILLING') {
  return useQuery({
    queryKey: scope === 'BILLING' ? billingKeys.fields : [...billingKeys.fields, scope],
    queryFn: () => unwrap(api.GET('/api/v1/export-templates/fields', { params: { query: { scope } } })),
  })
}

export function useTemplateMutations(scope = 'BILLING') {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: billingKeys.templates })
  return {
    create: useMutation({ mutationFn: (body: ExportTemplateRequest) => unwrap(api.POST('/api/v1/export-templates', { body: { ...body, scope } })), onSettled: refresh }),
    update: useMutation({
      mutationFn: (v: { id: string; body: ExportTemplateRequest }) => unwrap(api.PUT('/api/v1/export-templates/{publicId}', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    deactivate: useMutation({ mutationFn: (id: string) => unwrap(api.DELETE('/api/v1/export-templates/{publicId}', { params: { path: { publicId: id } } })), onSettled: refresh }),
  }
}

/** La vista previa del archivo con una plantilla (no cambia nada en el servidor). */
export function useExportPreview(runId: number, templateId: string | null) {
  return useQuery({
    queryKey: billingKeys.preview(runId, templateId),
    queryFn: () => unwrap(api.GET('/api/v1/billing/runs/{id}/export-preview', { params: { path: { id: runId }, query: { templateId: templateId ?? undefined } } })),
    enabled: templateId !== null,
    gcTime: 0,
  })
}

/** Arma el archivo y pasa la corrida de Aprobada a Exportada (la primera vez). */
export function useExportRun() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { runId: number; templateId: string | null }) =>
      unwrap(api.POST('/api/v1/billing/runs/{id}/export', { params: { path: { id: v.runId } }, body: { templatePublicId: v.templateId ?? undefined } })),
    onSettled: () => qc.invalidateQueries({ queryKey: billingKeys.all }),
  })
}

// ---------------------------------------------------------------- notas de crédito (Lote 80)

export function useCreditNotes(status: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: billingKeys.creditNotes(status, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/billing/credit-notes', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function creditNotePageReader(status: string, q: string) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/billing/credit-notes', { params: { query: { status: status || undefined, q: q || undefined, skip, take } } }))
}

export function useCreditNote(publicId: string | null) {
  return useQuery({
    queryKey: billingKeys.creditNote(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/billing/credit-notes/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

export function useCreditMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: billingKeys.all })
  return {
    create: useMutation({ mutationFn: (body: CreditNoteRequest) => unwrap(api.POST('/api/v1/billing/credit-notes', { body })), onSettled: refresh }),
    update: useMutation({
      mutationFn: (v: { id: string; body: CreditNoteRequest }) => unwrap(api.PUT('/api/v1/billing/credit-notes/{publicId}', { params: { path: { publicId: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    // aprobar lleva [RequireAal2]: el cliente del API reautentica solo
    approve: useMutation({ mutationFn: (id: string) => unwrap(api.POST('/api/v1/billing/credit-notes/{publicId}/approve', { params: { path: { publicId: id } } })), onSettled: refresh }),
    apply: useMutation({
      mutationFn: (v: { id: string; invoicePublicId: string; amount: number }) =>
        unwrap(api.POST('/api/v1/billing/credit-notes/{publicId}/apply', { params: { path: { publicId: v.id } }, body: { invoicePublicId: v.invoicePublicId, amount: v.amount } })),
      onSettled: refresh,
    }),
    voidNote: useMutation({
      mutationFn: (v: { id: string; reason: string }) => unwrap(api.POST('/api/v1/billing/credit-notes/{publicId}/void', { params: { path: { publicId: v.id } }, body: { reason: v.reason } })),
      onSettled: refresh,
    }),
  }
}
