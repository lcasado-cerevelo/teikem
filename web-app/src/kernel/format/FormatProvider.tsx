// Proveedor único de los formatos de la compañía: lee `GET /api/v1/tenant/settings` (la misma consulta que `useTenantSettings`)
// y deja sus valores en `store.ts`. Mientras carga, sin sesión o si la consulta falla, vale Puerto Rico. Al guardar Ajustes →
// Región y formatos (o invalidar `catalogKeys.tenantSettings`) toda la app se vuelve a pintar con los valores nuevos.
import { useLayoutEffect, useMemo, type ReactNode } from 'react'
import { useTenantSettings } from '../catalogs/api'
import { DEFAULT_FORMAT, toFormatSettings } from './settings'
import { setFormatSettings } from './store'

export function FormatProvider({ enabled, children }: { enabled: boolean; children: ReactNode }) {
  const { data } = useTenantSettings(enabled)
  const settings = useMemo(() => (enabled && data ? toFormatSettings(data) : DEFAULT_FORMAT), [enabled, data])
  useLayoutEffect(() => {
    setFormatSettings(settings)
  }, [settings])
  return <>{children}</>
}
