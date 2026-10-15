// Conexión en vivo de la mensajería (Lote 53, SignalR). El servidor solo avisa que algo cambió (MessageReceived, ThreadChanged,
// ThreadRead); la web vuelve a pedir lo suyo por la API. El token viaja en el query (los WebSockets no mandan cabeceras). Si la conexión
// cae se reconecta sola (0, 2, 5, 10 y 30 s) y, mientras tanto, el sondeo de respaldo (30 s) mantiene todo al día.
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'

export type LiveEventName = 'MessageReceived' | 'ThreadChanged' | 'ThreadRead' | 'NoticeReceived'
export const LIVE_EVENTS: readonly LiveEventName[] = ['MessageReceived', 'ThreadChanged', 'ThreadRead', 'NoticeReceived']

export interface LiveEvent {
  tenantId: number
  threadPublicId: string
  messageId?: number | null
  senderId?: number | null
  senderName?: string | null
  preview?: string | null
  muted: boolean
}

export interface RealtimeHandle {
  stop: () => Promise<void>
}

export interface RealtimeOptions {
  url: string
  token: () => string | null
  /** Solo se atienden los eventos de ESTA compañía (la persona puede tener varias y el servidor agrupa por persona y compañía). */
  tenantId: number | null
  onEvent: (name: LiveEventName, event: LiveEvent) => void
  /** true al conectar o reconectar (la web aprovecha para volver a pedir todo lo suyo), false al caer. */
  onState: (connected: boolean) => void
  /** Solo para pruebas: fabrica la conexión. */
  create?: (url: string, token: () => string) => HubConnection
}

const RETRY_MS = 10_000

function buildConnection(url: string, token: () => string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory: token, withCredentials: false })
    .withAutomaticReconnect([0, 2000, 5000, 10_000, 30_000])
    .configureLogging(LogLevel.None)
    .build()
}

/** Abre la conexión (reintenta mientras no se pueda) y devuelve cómo cerrarla. Nunca lanza: un fallo solo deja el respaldo por sondeo. */
export function connectMessagesHub(opts: RealtimeOptions): RealtimeHandle {
  let stopped = false
  let retry: ReturnType<typeof setTimeout> | null = null
  const connection = (opts.create ?? buildConnection)(opts.url, () => opts.token() ?? '')

  for (const name of LIVE_EVENTS) {
    connection.on(name, (event: LiveEvent) => {
      if (opts.tenantId !== null && event.tenantId !== opts.tenantId) return
      opts.onEvent(name, event)
    })
  }
  connection.onreconnected(() => opts.onState(true))
  connection.onreconnecting(() => opts.onState(false))
  connection.onclose(() => {
    opts.onState(false)
    if (!stopped) schedule()
  })

  const start = async () => {
    if (stopped) return
    try {
      await connection.start()
      if (stopped) {
        await connection.stop()
        return
      }
      opts.onState(true)
    } catch {
      opts.onState(false)
      schedule()
    }
  }
  const schedule = () => {
    if (stopped || retry) return
    retry = setTimeout(() => {
      retry = null
      void start()
    }, RETRY_MS)
  }
  void start()

  return {
    stop: async () => {
      stopped = true
      if (retry) clearTimeout(retry)
      try {
        await connection.stop()
      } catch {
        /* ya estaba cerrada */
      }
    },
  }
}
