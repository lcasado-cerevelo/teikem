// Tipos de catálogos y estatus que consume la interfaz, derivados de los DTO generados (schema.d.ts).
import type { components } from '../api/schema'

export type LookupValueDto = components['schemas']['LookupValueDto']
export type StatusDto = components['schemas']['StatusDto']
export type StatusHistoryDto = components['schemas']['StatusHistoryDto']
export type StatusLateralEntryDto = components['schemas']['StatusLateralEntryDto']
export type PipelineValidationResult = components['schemas']['PipelineValidationResult']
export type TenantSettingsDto = components['schemas']['TenantSettingsDto']

/** Clasificación de una etapa (StageKinds del dominio). */
export const StageKinds = { Pipeline: 'PIPELINE', Lateral: 'LATERAL', Terminal: 'TERMINAL' } as const
export type StageKind = (typeof StageKinds)[keyof typeof StageKinds]

/** Opción de catálogo lista para un select: `code` es el InternalCode que se envía al API. */
export interface LookupOption {
  code: string
  label: string
  description: string | null
  sortOrder: number
  isEnabled: boolean
}

/** Etapa de un dominio de estatus (etiqueta y color ya resueltos para el tenant e idioma). */
export interface StatusOption {
  code: string
  label: string
  /** Color hexadecimal (`#RRGGBB`) o null si el catálogo no define uno. */
  color: string | null
  stageKind: StageKind | string
  isInitial: boolean
  isEnabled: boolean
  sortOrder: number
  icon: string | null
}

export function toLookupOption(dto: LookupValueDto): LookupOption {
  const code = dto.code ?? ''
  return {
    code,
    label: dto.label || code,
    description: dto.description ?? null,
    sortOrder: dto.sortOrder ?? 0,
    isEnabled: dto.isEnabled ?? true,
  }
}

const HEX = /^#(?:[0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$/i

export function toStatusOption(dto: StatusDto): StatusOption {
  const code = dto.code ?? ''
  const color = dto.colorHex?.trim() ?? ''
  return {
    code,
    label: dto.label || code,
    color: HEX.test(color) ? color : null,
    stageKind: (dto.stageKind ?? '').toUpperCase(),
    isInitial: dto.isInitial ?? false,
    isEnabled: dto.isEnabled ?? true,
    sortOrder: dto.sortOrder ?? 0,
    icon: dto.icon ?? null,
  }
}

/**
 * Etiqueta de un código de catálogo entre las opciones cargadas (sin distinguir mayúsculas); si no está (valor deshabilitado
 * o catálogo aún sin cargar) devuelve el código tal cual. Sin código → null. P. ej. el predeterminado de la compañía:
 * `lookupLabelOrCode(settings?.defaultServiceType, serviceTypes)` → 'Estándar'.
 */
export function lookupLabelOrCode(code: string | null | undefined, options: readonly LookupOption[]): string | null {
  if (!code) return null
  return options.find((o) => sameCode(o.code, code))?.label || code
}

export function sameCode(a: string | null | undefined, b: string | null | undefined): boolean {
  return !!a && !!b && a.toLowerCase() === b.toLowerCase()
}
