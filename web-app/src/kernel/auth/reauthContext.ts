import { createContext, useContext } from 'react'

export interface ReauthApi {
  /** Abre el modal de reautenticación. Resuelve true si el usuario se reautenticó (AAL2), false si canceló. */
  reauth: () => Promise<boolean>
}

export const ReauthContext = createContext<ReauthApi | null>(null)

/**
 * Reautenticación (step-up AAL2). El cliente del API ya la pide sola cuando una llamada responde 403 aal2_required
 * y reintenta la acción; usa `reauth()` para pedirla antes de una acción sensible.
 */
export function useReauth(): ReauthApi {
  const ctx = useContext(ReauthContext)
  if (!ctx) throw new Error('useReauth requiere <ReauthProvider>.')
  return ctx
}
