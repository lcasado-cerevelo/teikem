import { useMemo, type ReactNode } from 'react'
import { AccessContext, type AccessState } from './accessContext'

interface Props {
  permissions: Iterable<string>
  modules: Iterable<string>
  children: ReactNode
}

/** Provee permisos y módulos. La app lo monta desde la sesión; las pruebas lo usan directo. */
export function AccessProvider({ permissions, modules, children }: Props) {
  const value = useMemo<AccessState>(
    () => ({ permissions: new Set(permissions), modules: new Set(modules) }),
    [permissions, modules],
  )
  return <AccessContext.Provider value={value}>{children}</AccessContext.Provider>
}
