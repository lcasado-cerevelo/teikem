// Sistema → Plantillas: lista de TODAS las plantillas de importación de cada entidad (sin pasar por el asistente de Importar), con las inactivas.
// Hoy: envíos (órdenes de transporte, `/import-templates`, `orders.edit`) y consignatarios (`/locations/import-templates`, `locations.create`).
// Las claves empiezan con las de cada módulo para que guardar, desactivar o reactivar desde cualquier pantalla refresque esta lista.
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { ImportTemplate } from '../consignees/consigneeRules'
import { consigneeKeys } from '../consignees/api'
import { orderImportKeys } from '../orders/importApi'
import { entityImportKeys, fetchEntityTemplates } from '../imports/entityImportApi'
import { ENTITY_CONFIG, type ImportEntity } from '../imports/entityImportRules'

export type TemplateEntity = 'ORDER' | 'CONSIGNEE' | ImportEntity

/** Permiso para ver y para cambiar las plantillas de cada entidad (el mismo que ya exige su asistente de importación). */
export const TEMPLATE_PERMISSION: Record<TemplateEntity, string> = { ORDER: 'orders.edit', CONSIGNEE: 'locations.create', PRODUCT: ENTITY_CONFIG.PRODUCT.manage, SUPPLIER: ENTITY_CONFIG.SUPPLIER.manage }

export function useAllTemplates(entity: TemplateEntity, enabled = true) {
  return useQuery({
    queryKey: entity === 'ORDER' ? [...orderImportKeys.templates, 'all'] : entity === 'CONSIGNEE' ? [...consigneeKeys.templates, 'all'] : [...entityImportKeys.templates(entity), 'all'],
    queryFn: (): Promise<ImportTemplate[]> =>
      entity === 'ORDER'
        ? unwrap(api.GET('/api/v1/import-templates', { params: { query: { kind: 'ORDER', includeInactive: true } } }))
        : entity === 'CONSIGNEE'
          ? unwrap(api.GET('/api/v1/locations/import-templates', { params: { query: { includeInactive: true } } }))
          : fetchEntityTemplates(entity, true),
     
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** Ordena para mostrar: activas primero, luego por nombre (sin distinguir mayúsculas). */
export function sortTemplates<T extends { name?: string | null; isActive?: boolean }>(list: readonly T[]): T[] {
  return [...list].sort((a, b) => Number(b.isActive !== false) - Number(a.isActive !== false) || (a.name ?? '').localeCompare(b.name ?? '', undefined, { sensitivity: 'base' }))
}
