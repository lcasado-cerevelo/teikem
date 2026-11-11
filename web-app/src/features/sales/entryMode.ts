// Modo de captura de una orden de venta (2026-11-11, pedido de Luis): «Asistente» (paso a paso, pantalla completa, pensado para tableta y celular) o «Formulario»
// (la ventana de siempre, todo a la vista). Es UNA sola forma de crear la orden con dos modos: el modo se recuerda POR PERSONA (por usuario, en el servidor),
// un borrador se retoma en el último modo usado y, mientras esté en borrador, se puede cambiar de uno a otro (se guarda primero).
import { useCallback } from 'react'
import { useSession } from '../../app/session'
import { useUserPreference } from '../../kernel/prefs/useUserPreference'

export type EntryMode = 'wizard' | 'full'

const KEY = 'teikem.salesEntryMode.'

/** Sin preferencia guardada: el asistente en pantallas angostas (celular, tableta vertical) y el formulario en escritorio. */
export function defaultEntryMode(): EntryMode {
  try {
    return window.matchMedia('(max-width: 900px)').matches ? 'wizard' : 'full'
  } catch {
    return 'full'
  }
}

export function readEntryMode(userId: number | undefined): EntryMode {
  try {
    const v = userId != null ? window.localStorage.getItem(KEY + userId) : null
    if (v === 'wizard' || v === 'full') return v
  } catch {
    /* sin almacenamiento (ventana privada): se usa el modo por omisión */
  }
  return defaultEntryMode()
}

export function writeEntryMode(userId: number | undefined, mode: EntryMode): void {
  if (userId == null) return
  try {
    window.localStorage.setItem(KEY + userId, mode)
  } catch {
    /* sin almacenamiento: no se recuerda, no es un error */
  }
}

/** `const [mode, setMode] = useEntryMode()` — el modo de esta persona, guardado en el servidor (por persona y compañía, en cualquier aparato). Mientras llega la respuesta
 * (o si falla) se usa la copia de este navegador y, sin ella, el modo por omisión; `setMode` lo guarda en los dos lados (y no hace nada si ya es ese). */
export function useEntryMode(): [EntryMode, (m: EntryMode) => void] {
  const userId = useSession().me?.userId
  const [server, saveServer] = useUserPreference('sales.entryMode')
  const mode: EntryMode = server === 'wizard' || server === 'full' ? server : readEntryMode(userId)
  const setMode = useCallback(
    (m: EntryMode) => {
      if (m === mode) return
      writeEntryMode(userId, m)
      saveServer(m)
    },
    [userId, saveServer, mode],
  )
  return [mode, setMode]
}

/** Dirección del asistente: sin id abre uno nuevo; con id retoma ese borrador. */
export function wizardPath(orderPublicId?: string | null): string {
  return orderPublicId ? `/sales-orders/new-order?order=${encodeURIComponent(orderPublicId)}` : '/sales-orders/new-order'
}
