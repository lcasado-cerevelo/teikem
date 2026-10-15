// Estado compartido de la campanita y de Kemi (Lote 52): la cantidad de avisos sin leer, si la campanita está abierta, si Kemi está
// dormida y el globo «te llegó un aviso». Lo monta el shell una sola vez; la campanita y Kemi (en la cabecera y flotando) lo leen.
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSession } from '../../app/session'
import { useUnreadCount } from './notificationsApi'
import { BUBBLE_SECONDS, decideBubble, readAsleep, readGreeted, writeAsleep, writeGreeted } from './kemiState'

interface NotificationsState {
  unread: number
  bellOpen: boolean
  setBellOpen: (open: boolean) => void
  asleep: boolean
  setAsleep: (asleep: boolean) => void
  /** Cuántos avisos nuevos anuncia el globo de Kemi (null = sin globo). */
  bubble: number | null
  dismissBubble: () => void
  /** El panel de opciones de Kemi. */
  kemiOpen: boolean
  setKemiOpen: (open: boolean) => void
}

const Ctx = createContext<NotificationsState | null>(null)

export function useNotifications(): NotificationsState {
  const v = useContext(Ctx)
  if (!v) throw new Error('useNotifications debe usarse dentro de <NotificationsProvider>')
  return v
}

export function NotificationsProvider({ children }: { children: ReactNode }) {
  const { me } = useSession()
  const userKey = me?.userId ?? me?.email ?? null
  const unreadQuery = useUnreadCount(Boolean(me))
  const unread = unreadQuery.data?.count ?? 0
  const [bellOpen, setBellOpenState] = useState(false)
  const [kemiOpen, setKemiOpen] = useState(false)
  const [asleep, setAsleepState] = useState(() => readAsleep(userKey))
  const [bubble, setBubble] = useState<number | null>(null)
  const previous = useRef<number | null>(null)
  const asleepRef = useRef(asleep)

  useEffect(() => {
    asleepRef.current = asleep
  }, [asleep])

  // al cambiar de usuario (o cerrar sesión) se vuelve a leer su estado y no se arrastra el contador anterior
  useEffect(() => {
    setAsleepState(readAsleep(userKey))
    previous.current = null
    setBubble(null)
  }, [userKey])

  useEffect(() => {
    if (unreadQuery.data === undefined) return
    const decision = decideBubble(previous.current, unread, readGreeted(), asleepRef.current)
    if (previous.current === null && unread > 0) writeGreeted()
    previous.current = decision.seen
    if (decision.bubble !== null) setBubble(decision.bubble)
  }, [unread, unreadQuery.data])

  // el globo se esconde solo
  useEffect(() => {
    if (bubble === null) return
    const timer = setTimeout(() => setBubble(null), BUBBLE_SECONDS * 1000)
    return () => clearTimeout(timer)
  }, [bubble])

  const setBellOpen = useCallback((open: boolean) => {
    setBellOpenState(open)
    if (open) {
      setBubble(null)
      setKemiOpen(false)
    }
  }, [])

  const setAsleep = useCallback(
    (next: boolean) => {
      writeAsleep(userKey, next)
      setAsleepState(next)
      if (next) {
        setBubble(null)
        setKemiOpen(false)
      }
    },
    [userKey],
  )

  const value = useMemo<NotificationsState>(
    () => ({ unread, bellOpen, setBellOpen, asleep, setAsleep, bubble, dismissBubble: () => setBubble(null), kemiOpen, setKemiOpen }),
    [unread, bellOpen, setBellOpen, asleep, setAsleep, bubble, kemiOpen],
  )
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
