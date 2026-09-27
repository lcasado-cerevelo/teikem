// Grupos del menú lateral (los de la maqueta). Los elementos del menú salen de `nav` en las rutas (routes.tsx):
// un grupo sin elementos visibles para el usuario no se pinta.
import type { ComponentType } from 'react'
import { IconAdmin, IconAnalytics, IconCatalog, IconOps, IconWarehouse } from './icons'

export type NavGroupKey = 'ops' | 'catalog' | 'warehouse' | 'analytics' | 'admin'

export interface NavGroup {
  key: NavGroupKey
  labelKey: string
  icon: ComponentType
}

export const NAV_GROUPS: readonly NavGroup[] = [
  { key: 'ops', labelKey: 'nav.groups.ops', icon: IconOps },
  { key: 'catalog', labelKey: 'nav.groups.catalog', icon: IconCatalog },
  { key: 'warehouse', labelKey: 'nav.groups.warehouse', icon: IconWarehouse },
  { key: 'analytics', labelKey: 'nav.groups.analytics', icon: IconAnalytics },
  { key: 'admin', labelKey: 'nav.groups.admin', icon: IconAdmin },
]

export interface NavEntry {
  group: NavGroupKey
  /** Clave i18n de la etiqueta del menú. */
  labelKey: string
  /** Orden dentro del grupo (menor primero). */
  order?: number
}
