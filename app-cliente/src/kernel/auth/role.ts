// ¿La sesión es de un CONSIGNATARIO con acceso propio (Lote 117)? Su access token trae el claim `loc`. Esto solo ORIENTA la interfaz (qué pestañas y qué pantallas se muestran): quien limita lo que puede ver y hacer es el
// servidor, que le responde 403 en todo lo que no sea su espacio, aunque alguien altere el token en el aparato.
import { useSessionState } from './session'

function payloadOf(token: string): Record<string, unknown> | null {
  try {
    const part = token.split('.')[1]
    if (!part) return null
    const b64 = part.replace(/-/g, '+').replace(/_/g, '/')
    const padded = b64 + '='.repeat((4 - (b64.length % 4)) % 4)
    const json = typeof atob === 'function' ? atob(padded) : ''
    return json ? (JSON.parse(json) as Record<string, unknown>) : null
  } catch {
    return null
  }
}

export function isConsigneeToken(accessToken: string | null | undefined): boolean {
  if (!accessToken) return false
  const claims = payloadOf(accessToken)
  return !!claims && claims.loc !== undefined && claims.loc !== null && claims.loc !== ''
}

/** true si la sesión actual es de un consignatario. */
export function useIsConsignee(): boolean {
  const { session } = useSessionState()
  return isConsigneeToken(session?.accessToken)
}
