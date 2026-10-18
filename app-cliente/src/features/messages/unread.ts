// Mensajes sin leer (el número de la pestaña «Mensajes»): se consulta al entrar y cada 30 segundos mientras hay sesión, y la pantalla de mensajes lo pone en 0 al leer.
// Una persona del cliente suma los del equipo de servicio al cliente (`team`) y las conversaciones de sus consignatarios con algo sin leer (`consignees`); un consignatario, solo lo que su cliente le contestó (`consignees`).
import { useEffect, useSyncExternalStore } from 'react'

import { getSessionState, useSessionState } from '../../kernel/auth/session'
import { isConsigneeToken } from '../../kernel/auth/role'
import { getConsigneeConversationsUnread, getConsigneeOwnUnread, getMessagesUnread } from '../portalApi'

let team = 0
let consignees = 0
let total = 0
const listeners = new Set<() => void>()

function publish(): void {
  const next = team + consignees
  if (next === total) return
  total = next
  listeners.forEach((l) => l())
}

/** Lo del equipo de servicio al cliente. */
export function setUnread(n: number): void {
  team = n
  publish()
}

/** Lo de los consignatarios (para el cliente: conversaciones sin leer; para el consignatario: mensajes de su cliente sin leer). */
export function setConsigneesUnread(n: number): void {
  consignees = n
  publish()
}

export function getUnread(): number {
  return total
}

export async function refreshUnread(): Promise<void> {
  const consignee = isConsigneeToken(getSessionState().session?.accessToken)
  try {
    if (consignee) {
      setUnread(0)
      setConsigneesUnread((await getConsigneeOwnUnread()).unread ?? 0)
      return
    }
    setUnread((await getMessagesUnread()).unread ?? 0)
  } catch {
    // sin red o sin sesión: se deja el número que había
  }
  if (consignee) return
  try {
    setConsigneesUnread((await getConsigneeConversationsUnread()).unread ?? 0)
  } catch {
    // igual
  }
}

export const POLL_MS = 30_000

/** El número de la pestaña; consulta al tener sesión y cada 30 s. */
export function useUnreadMessages(): number {
  const { session } = useSessionState()
  useEffect(() => {
    if (!session) {
      setUnread(0)
      setConsigneesUnread(0)
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
