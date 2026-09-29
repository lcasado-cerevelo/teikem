// Grupos del menú lateral (los 7 de la maqueta, en su orden). Los elementos del menú salen de `nav` en las rutas
// (routes.tsx): un grupo sin elementos visibles para el usuario no se pinta.
import type { ComponentType } from 'react'
import { permAllowed } from '../kernel/access/accessContext'
import { IconBox, IconCash, IconChart, IconGear, IconLayers, IconUsers } from './icons'

export type NavGroupKey = 'ops' | 'warehouse' | 'money' | 'catalog' | 'analytics' | 'system' | 'portal'

export interface NavGroup {
  key: NavGroupKey
  labelKey: string
  icon: ComponentType
}

/** Orden, etiqueta (`nav.groups.<key>`) e ícono de la maqueta (`NAV` en Diseño/teikem-mockups.html). */
export const NAV_GROUPS: readonly NavGroup[] = [
  { key: 'ops', labelKey: 'nav.groups.ops', icon: IconBox },
  { key: 'warehouse', labelKey: 'nav.groups.warehouse', icon: IconLayers },
  { key: 'money', labelKey: 'nav.groups.money', icon: IconCash },
  { key: 'catalog', labelKey: 'nav.groups.catalog', icon: IconUsers },
  { key: 'analytics', labelKey: 'nav.groups.analytics', icon: IconChart },
  { key: 'system', labelKey: 'nav.groups.system', icon: IconGear },
  { key: 'portal', labelKey: 'nav.groups.portal', icon: IconUsers },
]

export interface NavEntry {
  group: NavGroupKey
  /** Clave del ítem: título en `nav.<key>.title` y subtítulo en `nav.<key>.subtitle` (textos `nav.*` de la maqueta). */
  key: string
  /** Orden dentro del grupo (menor primero): posición en la maqueta × 10. */
  order?: number
}

/** Clave i18n del título del ítem (menú, paleta, pantalla pendiente). */
export function navTitleKey(key: string): string {
  return `nav.${key}.title`
}

/** Clave i18n del subtítulo del ítem (paleta, pantalla pendiente). */
export function navSubtitleKey(key: string): string {
  return `nav.${key}.subtitle`
}

/** Lo que el menú necesita de una ruta (AppRoute lo cumple). */
export interface NavRoute {
  nav?: NavEntry
  perm?: string
  module?: string
}

/** true si el usuario puede entrar a la ruta: su `module` encendido y su `perm` (`a|b` = cualquiera de los dos). */
export function routeAllowed(route: NavRoute, permissions: ReadonlySet<string>, modules: ReadonlySet<string>): boolean {
  return (!route.module || modules.has(route.module)) && permAllowed(route.perm, permissions)
}

/**
 * Grupos del menú con sus rutas visibles: una ruta aparece si tiene `nav`, su `module` está encendido y el usuario
 * tiene su `perm` (`a|b`: cualquiera de los dos); dentro del grupo, por `nav.order` (sin orden, al final). Un grupo
 * sin rutas visibles no se devuelve.
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
      .filter((r) => routeAllowed(r, permissions, modules))
      .sort((a, b) => (a.nav?.order ?? 1000) - (b.nav?.order ?? 1000)),
  })).filter((g) => g.items.length > 0)
}
