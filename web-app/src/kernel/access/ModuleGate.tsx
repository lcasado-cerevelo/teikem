import type { ReactNode } from 'react'
import { permAllowed, useAccess } from './accessContext'
import { ForbiddenScreen, ModuleOffScreen } from './AccessScreens'

interface Props {
  /** Código del módulo del API (`ModuleKeys`), p. ej. `CATALOG`. */
  module: string
  /** Permiso opcional que además se exige para ver el contenido (`a|b` = cualquiera de los dos). */
  perm?: string
  children: ReactNode
}

/** Muestra el contenido solo si el módulo está encendido ('Módulo apagado' si no) y, con `perm`, si hay permiso ('Sin permiso'). */
export function ModuleGate({ module, perm, children }: Props) {
  const { modules, permissions } = useAccess()
  if (!modules.has(module)) return <ModuleOffScreen module={module} />
  if (!permAllowed(perm, permissions)) return <ForbiddenScreen />
  return <>{children}</>
}
