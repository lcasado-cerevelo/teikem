import { createContext, useContext } from 'react'

export interface AccessState {
  /** Permisos efectivos del usuario en el tenant activo (`/api/v1/me` → permissions). */
  permissions: ReadonlySet<string>
  /** Módulos encendidos del tenant (`/api/v1/me` → enabledModules). */
  modules: ReadonlySet<string>
}

const EMPTY: AccessState = { permissions: new Set(), modules: new Set() }

export const AccessContext = createContext<AccessState>(EMPTY)

export function useAccess(): AccessState {
  return useContext(AccessContext)
}

/**
 * true si `permissions` cumple `perm`. `perm` admite alternativas separadas por `|` (cualquiera alcanza), igual que
 * `[RequirePermission("admin.users|admin.roles")]` del API. Sin `perm` (undefined o '') no exige nada.
 */
export function permAllowed(perm: string | null | undefined, permissions: ReadonlySet<string>): boolean {
  if (!perm) return true
  return perm
    .split('|')
    .map((p) => p.trim())
    .filter(Boolean)
    .some((p) => permissions.has(p))
}

/** true si el usuario tiene TODOS los permisos indicados (cada uno puede ser `a|b`). `useCan('orders.create')`. */
export function useCan(...perms: string[]): boolean {
  const { permissions } = useAccess()
  return perms.every((p) => permAllowed(p, permissions))
}

/** true si el usuario tiene AL MENOS UNO de los permisos indicados. `useCanAny('admin.users', 'admin.roles')`. */
export function useCanAny(...perms: string[]): boolean {
  const { permissions } = useAccess()
  return perms.some((p) => permAllowed(p, permissions))
}

/** true si el módulo está encendido para el tenant. `useModule('CATALOG')`. */
export function useModule(module: string): boolean {
  return useAccess().modules.has(module)
}
