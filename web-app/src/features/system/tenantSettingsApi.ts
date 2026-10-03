// Lecturas y escrituras de "Ajustes de la compañía" (/system/settings) sobre el cliente generado. Los ajustes se leen con
// `useTenantSettings` (kernel, misma clave que el proveedor de formatos): guardar deja el DTO devuelto en esa caché y toda la
// app se vuelve a pintar con los formatos nuevos sin recargar.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { CATALOG_STALE_MS, catalogKeys } from '../../kernel/catalogs'
import { brandLogoKeys, type BrandLogoDto, type LogoSlot } from '../../kernel/ui'

export type TenantSettingsDto = components['schemas']['TenantSettingsDto']
export type TenantSettingsUpdateRequest = components['schemas']['TenantSettingsUpdateRequest']
export type TenantFormatOptionsDto = components['schemas']['TenantFormatOptionsDto']
export type TenantHolidayDto = components['schemas']['TenantHolidayDto']
export type TenantHolidayRequest = components['schemas']['TenantHolidayRequest']
export type ModuleDto = components['schemas']['ModuleDto']
export type StatusCapabilityDto = components['schemas']['StatusCapabilityDto']

export const tenantSettingsKeys = {
  formatOptions: ['/api/v1/tenant/format-options'] as const,
  holidays: ['/api/v1/tenant/holidays'] as const,
  modules: ['/api/v1/modules'] as const,
  capabilities: (entityType: string) => ['/api/v1/status/capabilities/{entityType}', { entityType }] as const,
}

/** `GET /api/v1/tenant/format-options`: regiones (PR/US) con sus valores y los valores permitidos de cada campo. */
export function useFormatOptions() {
  return useQuery({
    queryKey: tenantSettingsKeys.formatOptions,
    queryFn: () => unwrap(api.GET('/api/v1/tenant/format-options')),
    staleTime: CATALOG_STALE_MS,
    meta: { handleAccessDenied: false },
  })
}

/** `PUT /api/v1/tenant/settings` (admin.tenant): parcial (null = sin cambio). Deja el DTO devuelto en la caché de ajustes. */
export function useSaveTenantSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: TenantSettingsUpdateRequest) => unwrap(api.PUT('/api/v1/tenant/settings', { body })),
    onSuccess: (dto) => {
      qc.setQueryData(catalogKeys.tenantSettings, dto)
    },
  })
}

/**
 * `PUT /api/v1/tenant/brand/logos/{slot}` (admin.tenant): sube o reemplaza el logo de la ranura (multipart, campo `file`).
 * Errores del servidor con su mensaje: 400 (vacío, imagen dañada, SVG con contenido activo), 413 (más de 512 KB), 415 (formato).
 * Invalida la lista de logos: `TenantBrand` vuelve a bajar el archivo cambiado y toda la interfaz lo usa sin recargar.
 */
export function useUploadBrandLogo() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ slot, file }: { slot: LogoSlot; file: File }): Promise<BrandLogoDto> => {
      const form = new FormData()
      form.append('file', file)
      return unwrap(api.PUT('/api/v1/tenant/brand/logos/{slot}', { params: { path: { slot } }, body: {}, bodySerializer: () => form }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: brandLogoKeys.list }),
  })
}

/** `DELETE /api/v1/tenant/brand/logos/{slot}` (admin.tenant): quita el logo; la interfaz vuelve al de Teikem (o a la otra variante). */
export function useRemoveBrandLogo() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (slot: LogoSlot): Promise<void> => {
      await unwrap(api.DELETE('/api/v1/tenant/brand/logos/{slot}', { params: { path: { slot } } }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: brandLogoKeys.list }),
  })
}

/** `GET /api/v1/tenant/holidays` (todos los activos, ordenados por fecha). */
export function useHolidays() {
  return useQuery({
    queryKey: tenantSettingsKeys.holidays,
    queryFn: () => unwrap(api.GET('/api/v1/tenant/holidays')),
    meta: { handleAccessDenied: false },
  })
}

/** Alta (`POST`) o baja (`DELETE`) de un feriado (admin.tenant); invalida la lista. */
export function useHolidayAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (action: { kind: 'add'; body: TenantHolidayRequest } | { kind: 'remove'; id: number }) => {
      if (action.kind === 'add') return unwrap(api.POST('/api/v1/tenant/holidays', { body: action.body }))
      await unwrap(api.DELETE('/api/v1/tenant/holidays/{id}', { params: { path: { id: action.id } } }))
      return null
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: tenantSettingsKeys.holidays }),
  })
}

/** `GET /api/v1/modules`: catálogo de módulos de la plataforma con su estado en la compañía. */
export function useModulesCatalog() {
  return useQuery({
    queryKey: tenantSettingsKeys.modules,
    queryFn: () => unwrap(api.GET('/api/v1/modules')),
    meta: { handleAccessDenied: false },
  })
}

/**
 * `PUT /api/v1/modules/{moduleKey}` (admin.tenant + AAL2: el cliente abre la reautenticación solo ante 403 aal2_required y
 * reintenta). Devuelve el catálogo completo (apagar apaga en cascada los dependientes) y lo deja en la caché.
 */
export function useSetModule() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ key, enabled }: { key: string; enabled: boolean }) =>
      unwrap(api.PUT('/api/v1/modules/{moduleKey}', { params: { path: { moduleKey: key } }, body: { isEnabled: enabled } })),
    onSuccess: (list) => qc.setQueryData(tenantSettingsKeys.modules, list),
  })
}

/** `GET /api/v1/status/capabilities/{entityType}`: matriz estatus × acción efectiva (la regla del tenant pisa la global). */
export function useCapabilities(entityType: string, enabled = true) {
  return useQuery({
    queryKey: tenantSettingsKeys.capabilities(entityType),
    queryFn: () => unwrap(api.GET('/api/v1/status/capabilities/{entityType}', { params: { path: { entityType } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** `PUT /api/v1/status/capabilities/{entityType}?statusDomain=` (admin.statusconfig): una o más celdas; deja la matriz en caché. */
export function useSetCapabilities(entityType: string, statusDomain: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (items: { statusCode: string; capability: string; isAllowed: boolean }[]) =>
      unwrap(
        api.PUT('/api/v1/status/capabilities/{entityType}', {
          params: { path: { entityType }, query: { statusDomain } },
          body: items,
        }),
      ),
    onSuccess: (list) => qc.setQueryData(tenantSettingsKeys.capabilities(entityType), list),
  })
}
