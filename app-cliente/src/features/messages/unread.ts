// Mensajes del equipo sin leer (el número de la pestaña «Mensajes»): se consulta al entrar y cada 30 segundos mientras hay sesión, y la pantalla de mensajes lo pone en 0 al leer.
import { useEffect, useSyncExternalStore } from 'react'

import { useSessionState } from '../../kernel/auth/session'
import { getMessagesUnread } from '../portalApi'

let unread = 0
const listeners = new Set<() => void>()

export function setUnread(n: number): void {
  if (n === unread) return
  unread = n
  listeners.forEach((l) => l())
}

export function getUnread(): number {
  return unread
}

export async function refreshUnread(): Promise<void> {
  try {
    setUnread((await getMessagesUnread()).unread ?? 0)
  } catch {
    // sin red o sin sesión: se deja el número que había
  }
}

export const POLL_MS = 30_000

/** El número de la pestaña; consulta al tener sesión y cada 30 s. */
export function useUnreadMessages(): number {
  const { session } = useSessionState()
  useEffect(() => {
    if (!session) {
      setUnread(0)
      return
    }
    void refreshUnread()
    const id = setInterval(() => void refreshUnread(), POLL_MS)
    return () => clearInterval(id)
  }, [session])
  return useSyncExternalStore(
    (l) => {
      listeners.add(l)
      return () => listeners.delete(l)
    },
    getUnread,
    getUnread,
  )
}
