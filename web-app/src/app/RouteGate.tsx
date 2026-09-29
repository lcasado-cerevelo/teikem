import type { AppRoute } from './routes'
import { useCan } from '../kernel/access/accessContext'
import { ForbiddenScreen } from '../kernel/access/AccessScreens'
import { ModuleGate } from '../kernel/access/ModuleGate'

/** Aplica `module` y `perm` de la ruta antes de pintar la pantalla. `perm: 'a|b'` deja pasar con cualquiera de los dos. */
export function RouteGate({ route }: { route: AppRoute }) {
  const Screen = route.element
  const allowed = useCan(...(route.perm ? [route.perm] : []))
  if (route.module) {
    return (
      <ModuleGate module={route.module} perm={route.perm}>
        <Screen />
      </ModuleGate>
    )
  }
  return allowed ? <Screen /> : <ForbiddenScreen />
}
