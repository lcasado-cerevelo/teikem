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

/** true si el usuario tiene TODOS los permisos indicados. `useCan('orders.create')`. */
export function useCan(...perms: string[]): boolean {
  const { permissions } = useAccess()
  return perms.every((p) => permissions.has(p))
}

/** true si el módulo está encendido para el tenant. `useModule('CATALOG')`. */
export function useModule(module: string): boolean {
  return useAccess().modules.has(module)
}
