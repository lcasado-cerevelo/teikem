// Estado compartido de Kemi en el portal (Lote 218), igual que NotificationsProvider en la web: si está dormida, dónde la dejó la persona, el panel de opciones y el globo «te llegó algo».
// En el portal lo que llega es MENSAJE (los avisos son una bandeja sin «leído»): el globo se alimenta del contador de mensajes sin leer de la pestaña Mensajes, con las mismas reglas de la web
// (saluda una vez por sesión, avisa cuando el contador SUBE, dormida no muestra globo).
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'

import { BUBBLE_SECONDS, decideBubble, type KemiPos } from '../../../../shared/kemi/rules'
import { sessionUserKey } from '../../kernel/auth/role'
import { useSessionState } from '../../kernel/auth/session'
import { useUnreadMessages } from '../messages/unread'
import { useKemiPrefs } from './kemiPrefs'

interface KemiState {
  unread: number
  asleep: boolean
  setAsleep: (v: boolean) => void
  pos: KemiPos
  setPos: (p: KemiPos) => void
  /** Cuántos mensajes nuevos anuncia el globo (null = sin globo). */
  bubble: number | null
  dismissBubble: () => void
  kemiOpen: boolean
  setKemiOpen: (v: boolean) => void
  ready: boolean
}

const Ctx = createContext<KemiState | null>(null)

/** Quien no está dentro del proveedor (consignatario, sin sesión) no tiene a Kemi. */
export function useKemiOptional(): KemiState | null {
  return useContext(Ctx)
}

export function useKemi(): KemiState {
  const v = useContext(Ctx)
  if (!v) throw new Error('useKemi debe usarse dentro de <KemiProvider>')
  return v
}

// saludó en esta sesión de la app (en la web, una por pestaña como sessionStorage; en el teléfono, hasta cerrar la app)
let greeted = false

export function KemiProvider({ children }: { children: ReactNode }) {
  const { session } = useSessionState()
  const userKey = sessionUserKey(session?.accessToken)
  const prefs = useKemiPrefs(userKey)
  const unread = useUnreadMessages()
  const [kemiOpen, setKemiOpenState] = useState(false)
  const [bubble, setBubble] = useState<number | null>(null)
  const previous = useRef<number | null>(null)
  const asleepRef = useRef(prefs.asleep)

  useEffect(() => {
    asleepRef.current = prefs.asleep
  }, [prefs.asleep])

  // al cambiar de persona se empieza de nuevo
  useEffect(() => {
    previous.current = null
    setBubble(null)
  }, [userKey])

  useEffect(() => {
    if (!prefs.ready) return
    const d = decideBubble(previous.current, unread, greeted, asleepRef.current)
    if (previous.current === null && unread > 0) greeted = true
    previous.current = d.seen
    if (d.bubble !== null) setBubble(d.bubble)
  }, [unread, prefs.ready])

  useEffect(() => {
    if (bubble === null) return
    const id = setTimeout(() => setBubble(null), BUBBLE_SECONDS * 1000)
    return () => clearTimeout(id)
  }, [bubble])

  const setKemiOpen = useCallback((v: boolean) => {
    setKemiOpenState(v)
    if (v) setBubble(null)
  }, [])

  const persistAsleep = prefs.setAsleep
  const setAsleep = useCallback(
    (v: boolean) => {
      persistAsleep(v)
      if (v) {
        setBubble(null)
        setKemiOpenState(false)
      }
    },
    [persistAsleep],
  )

  const value = useMemo<KemiState>(
    () => ({ unread, asleep: prefs.asleep, setAsleep, pos: prefs.pos, setPos: prefs.setPos, bubble, dismissBubble: () => setBubble(null), kemiOpen, setKemiOpen, ready: prefs.ready }),
    [unread, prefs.asleep, setAsleep, prefs.pos, prefs.setPos, bubble, kemiOpen, setKemiOpen, prefs.ready],
  )
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
