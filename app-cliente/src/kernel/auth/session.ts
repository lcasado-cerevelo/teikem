// Sesión del portal: el par de tokens (en memoria y guardado en el almacén seguro del aparato). `useSession()` avisa a las pantallas cuando cambia.
import { useSyncExternalStore } from 'react'

import { StoreKeys, storeDelete, storeGet, storeSet } from '../storage'

export interface Session {
  accessToken: string
  accessExpiresAtUtc?: string
  refreshToken: string
  refreshExpiresAtUtc?: string
  tenantId: number
}

interface State {
  /** false hasta que se leyó lo guardado en el aparato (arranque). */
  loaded: boolean
  session: Session | null
}

let state: State = { loaded: false, session: null }
const listeners = new Set<() => void>()

function emit(next: State): void {
  state = next
  listeners.forEach((l) => l())
}

export function getSessionState(): State {
  return state
}

export function subscribeSession(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useSessionState(): State {
  return useSyncExternalStore(subscribeSession, getSessionState, getSessionState)
}

/** Lee la sesión guardada (al arrancar la app). */
export async function loadSession(): Promise<void> {
  let session: Session | null = null
  try {
    const raw = await storeGet(StoreKeys.session)
    const parsed = raw ? (JSON.parse(raw) as Partial<Session>) : null
    if (parsed?.accessToken && parsed.refreshToken) session = parsed as Session
  } catch {
    session = null
  }
  emit({ loaded: true, session })
}

export async function saveSession(session: Session): Promise<void> {
  emit({ loaded: true, session })
  await storeSet(StoreKeys.session, JSON.stringify(session))
}

export async function clearSession(): Promise<void> {
  emit({ loaded: true, session: null })
  await storeDelete(StoreKeys.session)
}
