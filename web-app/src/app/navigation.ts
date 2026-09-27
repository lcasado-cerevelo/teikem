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

/** Lo que el menú necesita de una ruta (AppRoute lo cumple). */
export interface NavRoute {
  nav?: NavEntry
  perm?: string
  module?: string
}

/**
 * Grupos del menú con sus rutas visibles: una ruta aparece si tiene `nav`, su `module` está encendido y el usuario
 * tiene su `perm`; dentro del grupo, por `nav.order` (sin orden, al final). Un grupo sin rutas visibles no se devuelve.
 */
export function visibleNav<R extends NavRoute>(
  routes: readonly R[],
  permissions: ReadonlySet<string>,
  modules: ReadonlySet<string>,
): (NavGroup & { items: R[] })[] {
  return NAV_GROUPS.map((g) => ({
    ...g,
    items: routes
      .filter((r) => r.nav?.group === g.key)
      .filter((r) => (!r.module || modules.has(r.module)) && (!r.perm || permissions.has(r.perm)))
      .sort((a, b) => (a.nav?.order ?? 100) - (b.nav?.order ?? 100)),
  })).filter((g) => g.items.length > 0)
}
