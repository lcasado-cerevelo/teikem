// Datos de «Impresoras y labels» (Sistema, Lote F-G1): impresoras y plantillas de label de la compañía (printers.manage, módulo
// SYSTEM), las opciones de impresión de una estación (también para quien imprime: órdenes, escaneo, inventario) y el ajuste de la
// compañía «Imprimir labels directo a la impresora». Cada escritura invalida todo lo que empieza con `printingKeys.all`.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type PrinterDto = Schemas['PrinterDto']
export type PrinterRequest = Schemas['PrinterRequest']
export type PrinterOption = Schemas['PrinterOptionDto']
export type LabelTemplateDto = Schemas['LabelTemplateDto']
export type LabelLayoutDto = Schemas['LabelLayoutDto']
export type StationOptions = Schemas['PrintStationOptionsDto']

/** Clase de plantilla: la de las PIEZAS (un label por pieza) o la de la FACTURA (un label con el código de barras del número de factura). */
export type TemplateKind = 'PIECE' | 'INVOICE'

export const printingKeys = {
  all: ['printing'] as const,
  printers: (includeInactive: boolean) => ['printing', '/api/v1/printers', { includeInactive }] as const,
  templates: (includeInactive: boolean, kind: TemplateKind) => ['printing', '/api/v1/label-templates', { includeInactive, kind }] as const,
  options: ['printing', '/api/v1/printing/station-options'] as const,
  settings: ['printing', '/api/v1/printing/settings'] as const,
}

export function usePrinters(includeInactive: boolean) {
  return useQuery({
    queryKey: printingKeys.printers(includeInactive),
    queryFn: () => unwrap(api.GET('/api/v1/printers', { params: { query: { includeInactive } } })),
  })
}

export function useLabelTemplates(includeInactive: boolean, kind: TemplateKind = 'PIECE') {
  return useQuery({
    queryKey: printingKeys.templates(includeInactive, kind),
    queryFn: () => unwrap(api.GET('/api/v1/label-templates', { params: { query: { includeInactive, kind } } })),
  })
}

/** Opciones de la estación (impresoras y plantillas activas, label integrado, «directo» y firma de QZ). */
export function useStationOptions(enabled = true) {
  return useQuery({
    queryKey: printingKeys.options,
    queryFn: () => unwrap(api.GET('/api/v1/printing/station-options')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export function usePrintingSettings() {
  return useQuery({ queryKey: printingKeys.settings, queryFn: () => unwrap(api.GET('/api/v1/printing/settings')) })
}

function useInvalidate() {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: printingKeys.all })
}

export function useSavePrintingSettings() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (printLabelsDirect: boolean) => unwrap(api.PUT('/api/v1/printing/settings', { body: { printLabelsDirect } })),
    onSettled: invalidate,
  })
}

export function usePrinterMutations() {
  const invalidate = useInvalidate()
  const path = (publicId: string) => ({ params: { path: { publicId } } })
  return {
    create: useMutation({ mutationFn: (body: PrinterRequest) => unwrap(api.POST('/api/v1/printers', { body })), onSettled: invalidate }),
    update: useMutation({
      mutationFn: (v: { publicId: string; body: PrinterRequest }) => unwrap(api.PATCH('/api/v1/printers/{publicId}', { ...path(v.publicId), body: v.body })),
      onSettled: invalidate,
    }),
    makeDefault: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/printers/{publicId}/make-default', path(publicId))), onSettled: invalidate }),
    setActive: useMutation({
      mutationFn: (v: { publicId: string; active: boolean }) =>
        v.active
          ? unwrap(api.POST('/api/v1/printers/{publicId}/reactivate', path(v.publicId)))
          : unwrap(api.POST('/api/v1/printers/{publicId}/deactivate', path(v.publicId))),
      onSettled: invalidate,
    }),
  }
}

export function useTemplateMutations() {
  const invalidate = useInvalidate()
  const path = (publicId: string) => ({ params: { path: { publicId } } })
  return {
    create: useMutation({
      mutationFn: (body: { name: string; layout: LabelLayoutDto; kind?: TemplateKind }) => unwrap(api.POST('/api/v1/label-templates', { body })),
      onSettled: invalidate,
    }),
    update: useMutation({
      mutationFn: (v: { publicId: string; name?: string; layout?: LabelLayoutDto; rowVersion?: string | null }) =>
        unwrap(api.PATCH('/api/v1/label-templates/{publicId}', { ...path(v.publicId), body: { name: v.name, layout: v.layout, rowVersion: v.rowVersion } })),
      onSettled: invalidate,
    }),
    makeDefault: useMutation({ mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/label-templates/{publicId}/make-default', path(publicId))), onSettled: invalidate }),
    setActive: useMutation({
      mutationFn: (v: { publicId: string; active: boolean }) =>
        v.active
          ? unwrap(api.POST('/api/v1/label-templates/{publicId}/reactivate', path(v.publicId)))
          : unwrap(api.POST('/api/v1/label-templates/{publicId}/deactivate', path(v.publicId))),
      onSettled: invalidate,
    }),
  }
}
