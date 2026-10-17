// Datos de «Liquidación a choferes» (Lote 96, módulo LTL_GROUND): liquidaciones (LIQ-#####), vista previa, flujo generar → revisar → aprobar → exportar, ajuste por línea, intentos de entrega y plantillas.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type DriverSettlementDto = Schemas['DriverSettlementDto']
export type DriverSettlementListItemDto = Schemas['DriverSettlementListItemDto']
export type DriverSettlementLineDto = Schemas['DriverSettlementLineDto']
export type DriverSettlementPreviewDto = Schemas['DriverSettlementPreviewDto']
export type DriverSettlementRequest = Schemas['DriverSettlementRequest']
export type DeliveryAttemptDto = Schemas['DeliveryAttemptDto']
export type DeliveryAttemptCreateRequest = Schemas['DeliveryAttemptCreateRequest']
export type ExportFileDto = Schemas['ExportFileDto']
export type ExportTemplateDto = Schemas['ExportTemplateDto']

export const settlementKeys = {
  all: ['driver-settlements'] as const,
  list: (status: string, driver: string, q: string, skip: number, take: number) => ['driver-settlements', 'list', status, driver, q, skip, take] as const,
  one: (id: string | null) => ['driver-settlements', 'one', id] as const,
  preview: (driver: string, from: string, to: string) => ['driver-settlements', 'preview', driver, from, to] as const,
  attempts: (q: AttemptQuery) => ['driver-settlements', 'attempts', q] as const,
  templates: ['driver-settlements', 'templates'] as const,
}

export function useSettlements(status: string, driver: string, q: string, skip: number, take: number) {
  return useQuery({
    queryKey: settlementKeys.list(status, driver, q, skip, take),
    queryFn: () => unwrap(api.GET('/api/v1/driver-settlements', { params: { query: { status: status || undefined, driverPublicId: driver || undefined, q: q || undefined, skip, take } } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las páginas del filtro actual (la exportación de la tabla). */
export function settlementPageReader(status: string, driver: string, q: string) {
  return (skip: number, take: number) =>
    unwrap(api.GET('/api/v1/driver-settlements', { params: { query: { status: status || undefined, driverPublicId: driver || undefined, q: q || undefined, skip, take } } }))
}

export function useSettlement(publicId: string | null) {
  return useQuery({
    queryKey: settlementKeys.one(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/driver-settlements/{publicId}', { params: { path: { publicId: publicId! } } })),
    enabled: publicId !== null,
  })
}

/** Lo que entraría en la liquidación (sin crearla): solo con un período válido. */
export function useSettlementPreview(driver: string, from: string, to: string, enabled: boolean) {
  return useQuery({
    queryKey: settlementKeys.preview(driver, from, to),
    queryFn: () => unwrap(api.POST('/api/v1/driver-settlements/preview', { body: { driverPublicId: driver || undefined, periodStart: from, periodEnd: to } })),
    enabled,
    gcTime: 0,
  })
}

export function useSettlementMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: settlementKeys.all })
  return {
    generate: useMutation({ mutationFn: (body: DriverSettlementRequest) => unwrap(api.POST('/api/v1/driver-settlements', { body })), onSettled: refresh }),
    review: useMutation({ mutationFn: (id: string) => unwrap(api.POST('/api/v1/driver-settlements/{publicId}/review', { params: { path: { publicId: id } } })), onSettled: refresh }),
    // aprobar lleva [RequireAal2]: el cliente del API abre solo el modal de reautenticación al recibir 403 aal2_required y reintenta
    approve: useMutation({ mutationFn: (id: string) => unwrap(api.POST('/api/v1/driver-settlements/{publicId}/approve', { params: { path: { publicId: id } } })), onSettled: refresh }),
    cancel: useMutation({
      mutationFn: (v: { id: string; comment?: string }) => unwrap(api.POST('/api/v1/driver-settlements/{publicId}/cancel', { params: { path: { publicId: v.id } }, body: { comment: v.comment } })),
      onSettled: refresh,
    }),
    adjust: useMutation({
      mutationFn: (v: { id: string; lineId: number; amount: number; note: string }) =>
        unwrap(api.PATCH('/api/v1/driver-settlements/{publicId}/lines/{lineId}', { params: { path: { publicId: v.id, lineId: v.lineId } }, body: { amount: v.amount, note: v.note } })),
      onSettled: refresh,
    }),
  }
}

/** El archivo de la liquidación (una fila por línea) con la plantilla elegida o la primera activa; exportar pasa una aprobada a exportada. */
export function exportSettlement(id: string, templatePublicId: string | null) {
  return unwrap(api.POST('/api/v1/driver-settlements/{publicId}/export', { params: { path: { publicId: id }, query: { templatePublicId: templatePublicId ?? undefined } } }))
}

/** Plantillas de exportación de liquidaciones: el API las pide con permisos de ver facturación, COD o pago a choferes; sin el módulo de facturación la lista no carga y se usa la estándar. */
export function useSettlementTemplates(enabled: boolean) {
  return useQuery({
    queryKey: settlementKeys.templates,
    queryFn: () => unwrap(api.GET('/api/v1/export-templates', { params: { query: { scope: 'DRIVER_SETTLEMENT' } } })),
    enabled,
    retry: false,
    meta: { handleAccessDenied: false },   // sin el módulo de facturación el API responde 403: no es un error de la pantalla, solo se usa la plantilla estándar
  })
}

// ---------------------------------------------------------------- intentos de entrega (Lote 93)

export interface AttemptQuery {
  driver: string
  from: string
  to: string
  state: '' | 'delivered' | 'failed'
  settled: '' | 'yes' | 'no'
  withoutDriver: boolean
  includeVoided: boolean
  skip: number
  take: number
}

function attemptParams(q: AttemptQuery) {
  // el API enlaza el record de la consulta por nombre sin distinguir mayúsculas; el esquema generado los trae en PascalCase
  return {
    DriverPublicId: q.driver || undefined,
    From: q.from || undefined,
    To: q.to || undefined,
    Delivered: q.state === '' ? undefined : q.state === 'delivered',
    Settled: q.settled === '' ? undefined : q.settled === 'yes',
    WithoutDriver: q.withoutDriver ? true : undefined,
    IncludeVoided: q.includeVoided ? true : undefined,
    Skip: q.skip,
    Take: q.take,
  }
}

export function useAttempts(q: AttemptQuery) {
  return useQuery({
    queryKey: settlementKeys.attempts(q),
    queryFn: () => unwrap(api.GET('/api/v1/delivery-attempts', { params: { query: attemptParams(q) } })),
    placeholderData: keepPreviousData,
  })
}

export function attemptPageReader(q: AttemptQuery) {
  return (skip: number, take: number) => unwrap(api.GET('/api/v1/delivery-attempts', { params: { query: attemptParams({ ...q, skip, take }) } }))
}

export function useAttemptMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: settlementKeys.all })
  return {
    create: useMutation({ mutationFn: (body: DeliveryAttemptCreateRequest) => unwrap(api.POST('/api/v1/delivery-attempts', { body })), onSettled: refresh }),
    void: useMutation({
      mutationFn: (v: { id: string; reason: string }) => unwrap(api.POST('/api/v1/delivery-attempts/{publicId}/void', { params: { path: { publicId: v.id } }, body: { reason: v.reason } })),
      onSettled: refresh,
    }),
  }
}
