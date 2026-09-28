import { useSyncExternalStore } from 'react'

import { getSessionState, subscribeSession } from './session'

/** Se vuelve a renderizar cuando el aparato o la sesión de usuario cambian (registrar, entrar, salir, refrescar). */
export function useSession() {
  return useSyncExternalStore(subscribeSession, getSessionState, getSessionState)
}
