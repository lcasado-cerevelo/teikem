import type { ReactNode } from 'react'
import { useAccess } from './accessContext'
import { ForbiddenScreen, ModuleOffScreen } from './AccessScreens'

interface Props {
  /** Código del módulo del API (`ModuleKeys`), p. ej. `CATALOG`. */
  module: string
  /** Permiso opcional que además se exige para ver el contenido. */
  perm?: string
  children: ReactNode
}

/** Muestra el contenido solo si el módulo está encendido ('Módulo apagado' si no) y, con `perm`, si hay permiso ('Sin permiso'). */
export function ModuleGate({ module, perm, children }: Props) {
  const { modules, permissions } = useAccess()
  if (!modules.has(module)) return <ModuleOffScreen module={module} />
  if (perm && !permissions.has(perm)) return <ForbiddenScreen />
  return <>{children}</>
}
