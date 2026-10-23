// Estado compartido de la mensajería (Lote 53): cuántos mensajes hay sin leer, la conexión en vivo, el globo de Kemi «te llegó un mensaje de X»,
// el hilo que se está viendo (para no avisar de lo que ya se ve) y la ventana «Nuevo mensaje» (la abre la pantalla o Kemi). La presencia
// («en línea») se renueva cada 30 s mientras la web está abierta. Sin el permiso messages.use no se conecta ni pide nada.
import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSession } from '../../app/session'
import { API_BASE_URL } from '../../kernel/api/client'
import { getAccessToken } from '../../kernel/auth/tokens'
import { notificationKeys } from '../notifications/notificationsApi'
import { useNotifications } from '../notifications/NotificationsProvider'
import { liveDecision } from './messageRules'
import { messageKeys, pingPresence, PRESENCE_PING_MS, useMessageUnread, type MessageUnreadDto, useMessageMutations } from './messagesApi'
import { connectMessagesHub, type LiveEvent } from './messagesRealtime'

/** De qué orden o ruta se va a hablar (la tarjeta que lleva el mensaje). */
export interface AboutRef {
  entity: 'ORDER' | 'TRIP'
  publicId: string
  /** Número de la orden o código de la ruta. */
  title: string
}

export interface MessageBubble {
  threadId: string
  fromName: string
  preview: string
  count: number
}

interface MessagesState {
  /** La persona puede usar la mensajería (permiso messages.use). */
  enabled: boolean
  count: number
  threads: number
  connected: boolean
  bubble: MessageBubble | null
  dismissBubble: () => void
  /** La pantalla avisa qué hilo está viendo (null = ninguno) para no avisar ni contar lo que ya ve. */
  setActiveThread: (id: string | null) => void
  newMessageOpen: boolean
  /** Sobre qué orden o ruta se abrió «Nuevo mensaje» (null = un mensaje normal). */
  newMessageAbout: AboutRef | null
  openNewMessage: (about?: AboutRef) => void
  closeNewMessage: () => void
}

/** Sin proveedor (una pantalla o una prueba que no monta el shell) la mensajería está apagada: nada se conecta ni se muestra. */
const DISABLED: MessagesState = {
  enabled: false,
  count: 0,
  threads: 0,
  connected: false,
  bubble: null,
  dismissBubble: () => {},
  setActiveThread: () => {},
  newMessageOpen: false,
  newMessageAbout: null,
  openNewMessage: () => {},
  closeNewMessage: () => {},
}

const Ctx = createContext<MessagesState>(DISABLED)

export function useMessages(): MessagesState {
  return useContext(Ctx)
}

const GREETED_KEY = 'teikem.kemi.greeted.messages'

function readGreeted(): boolean {
  try {
    return globalThis.sessionStorage?.getItem(GREETED_KEY) === '1'
  } catch {
    return false
  }
}

function writeGreeted(): void {
  try {
    globalThis.sessionStorage?.setItem(GREETED_KEY, '1')
  } catch {
    /* ignorar */
  }
}

export const BUBBLE_SECONDS = 15

export function MessagesProvider({ children }: { children: ReactNode }) {
  const { me, permissions, tenantId } = useSession()
  const qc = useQueryClient()
  const { asleep } = useNotifications()
  const enabled = Boolean(me) && permissions.has('messages.use')
  const unread = useMessageUnread(enabled)
  const count = unread.data?.count ?? 0
  const [connected, setConnected] = useState(false)
  const [bubble, setBubble] = useState<MessageBubble | null>(null)
  const [newMessageOpen, setNewMessageOpen] = useState(false)
  const [newMessageAbout, setNewMessageAbout] = useState<AboutRef | null>(null)
  const previous = useRef<number | null>(null)
  const active = useRef<string | null>(null)
  const asleepRef = useRef(asleep)
  const { read } = useMessageMutations()
  const readRef = useRef(read.mutate)

  useEffect(() => {
    asleepRef.current = asleep
    readRef.current = read.mutate
  })

  // la conexión en vivo (la tiene toda persona con sesión: también trae los avisos manuales): un empuje solo invalida lo que cambió; la web lo vuelve a pedir
  const hasSession = Boolean(me)
  useEffect(() => {
    if (!hasSession) return
    const handle = connectMessagesHub({
      url: `${API_BASE_URL}/hubs/messages`,
      token: () => getAccessToken() ?? null,
      tenantId: tenantId ?? null,
      onEvent: (name, _event: LiveEvent) => void qc.invalidateQueries({ queryKey: name === 'NoticeReceived' ? notificationKeys.all : messageKeys.all }),
      onState: (ok) => {
        setConnected(ok)
        if (ok) {
          void qc.invalidateQueries({ queryKey: messageKeys.all })   // tras (re)conectar se pone al día
          void qc.invalidateQueries({ queryKey: notificationKeys.all })
        }
      },
    })
    return () => {
      void handle.stop()
      setConnected(false)
    }
  }, [hasSession, tenantId, qc])

  // presencia: cada 30 s mientras esté abierta (la conexión en vivo también la renueva al conectar)
  useEffect(() => {
    if (!me) return
    const ping = () => void pingPresence().catch(() => undefined)
    ping()
    const timer = setInterval(ping, PRESENCE_PING_MS)
    return () => clearInterval(timer)
  }, [me])

  // al cambiar de usuario o de compañía no se arrastra el contador anterior
  useEffect(() => {
    previous.current = null
    setBubble(null)
  }, [me?.userId, tenantId])

  // el globo: la primera lectura de la sesión saluda con lo pendiente; después, cada vez que el contador SUBE
  useEffect(() => {
    const data: MessageUnreadDto | undefined = unread.data
    if (!data) return
    const prev = previous.current
    previous.current = data.count
    const latest = data.latest
    const rise = prev === null ? (!readGreeted() && data.count > 0 ? data.count : 0) : data.count > prev ? data.count - prev : 0
    if (prev === null && data.count > 0) writeGreeted()
    if (rise <= 0 || !latest) return
    const decision = liveDecision({ muted: latest.isMuted ?? false, threadOpen: active.current === latest.threadPublicId, windowVisible: document.visibilityState === 'visible', asleep: asleepRef.current })
    if (decision === 'markRead') readRef.current(latest.threadPublicId!)
    else if (decision === 'bubble') setBubble({ threadId: latest.threadPublicId!, fromName: latest.fromName ?? '', preview: latest.preview ?? '', count: rise })
  }, [unread.data])

  useEffect(() => {
    if (!bubble) return
    const timer = setTimeout(() => setBubble(null), BUBBLE_SECONDS * 1000)
    return () => clearTimeout(timer)
  }, [bubble])

  const setActiveThread = useCallback((id: string | null) => {
    active.current = id
    if (id) setBubble((b) => (b?.threadId === id ? null : b))
  }, [])

  const value = useMemo<MessagesState>(
    () => ({
      enabled,
      count,
      threads: unread.data?.threads ?? 0,
      connected,
      bubble,
      dismissBubble: () => setBubble(null),
      setActiveThread,
      newMessageOpen,
      newMessageAbout,
      openNewMessage: (about?: AboutRef) => {
        setNewMessageAbout(about ?? null)
        setNewMessageOpen(true)
      },
      closeNewMessage: () => setNewMessageOpen(false),
    }),
    [enabled, count, unread.data?.threads, connected, bubble, setActiveThread, newMessageOpen, newMessageAbout],
  )
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
