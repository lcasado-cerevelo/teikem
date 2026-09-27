// Rutas de la aplicación. Una ruta por pantalla, con carga diferida: `lazy(() => import('../features/<modulo>/<Pantalla>'))`
// (la pantalla exporta `default`). `perm` y `module` usan los códigos exactos del API; `nav` la pone en el menú lateral.
// Para agregar una pantalla: añade una entrada a `appRoutes` (internas, dentro del shell) o `publicRoutes` (sin sesión).
import { lazy, type ComponentType } from 'react'
import { ForbiddenScreen, ModuleOffScreen } from '../kernel/access/AccessScreens'
import type { NavEntry } from './navigation'

export interface AppRoute {
  path: string
  element: ComponentType
  /** Permiso exigido (sin él: pantalla 'Sin permiso'). */
  perm?: string
  /** Módulo exigido (apagado: pantalla 'Módulo apagado'). */
  module?: string
  /** Entrada del menú lateral; sin `nav` la ruta existe pero no aparece en el menú. */
  nav?: NavEntry
}

/** Pantallas sin sesión (fuera del shell). */
export const publicRoutes: readonly AppRoute[] = [
  { path: '/login', element: lazy(() => import('../features/auth/LoginPage')) },
  { path: '/mfa', element: lazy(() => import('../features/auth/MfaPage')) },
  { path: '/select-tenant', element: lazy(() => import('../features/auth/SelectTenantPage')) },
]

/** Pantallas internas (dentro del shell, con sesión). */
export const appRoutes: readonly AppRoute[] = [
  { path: '/', element: lazy(() => import('../features/analytics/Pulse')), nav: { group: 'ops', labelKey: 'nav.pulse', order: 0 } },
  { path: '/account', element: lazy(() => import('../features/account/AccountPage')) },
  { path: '/forbidden', element: ForbiddenScreen },
  { path: '/module-off', element: ModuleOffScreen },
]
