// Una conversación: burbujas (las mías a la derecha, las del otro lado a la izquierda con su nombre), «Cargar mensajes anteriores», y abajo donde escribir (máximo 2000 caracteres). Se actualiza sola cada 15 segundos
// mientras la pantalla está abierta, y al verla se marcan como leídos los del otro lado. La usan los mensajes con el equipo de servicio al cliente (Lote 104) y, con otras llamadas, los del consignatario con su cliente
// y los del cliente con sus consignatarios (Lote 117).
import { useCallback, useEffect, useRef, useState } from 'react'
import { KeyboardAvoidingView, Platform, ScrollView, Text, TextInput, View } from 'react-native'

import { ApiError, apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Muted, Notice, Spinner, useColors } from '../../kernel/ui/ui'
import { formatDateTime } from '../format'
import type { ClientMessageDto, PortalMessagesDto } from '../portalApi'

export const REFRESH_MS = 15_000
export const MAX_LENGTH = 2000

/** Las llamadas de una conversación: leer (últimos o anteriores a `before`), escribir y marcar leído lo del otro lado. */
export interface ChatApi {
  load: (before: number | undefined, take: number) => Promise<PortalMessagesDto>
  send: (body: string) => Promise<ClientMessageDto>
  markRead: () => Promise<void>
}

/** Junta los mensajes nuevos con los que ya había (por id), en orden cronológico. */
export function mergeMessages(current: readonly ClientMessageDto[], incoming: readonly ClientMessageDto[]): ClientMessageDto[] {
  const byId = new Map<number, ClientMessageDto>()
  for (const m of [...current, ...incoming]) if (m.id !== undefined) byId.set(m.id, m)
  return [...byId.values()].sort((a, b) => (a.id ?? 0) - (b.id ?? 0))
}

export interface ChatScreenProps {
  api: ChatApi
  /** Texto de ayuda de arriba. */
  help: string
  /** Texto cuando todavía no hay mensajes. */
  empty: string
  /** Etiqueta del otro lado junto al nombre («Servicio al cliente», «Su cliente»…). */
  otherLabel: string
  /** Se llama con los mensajes sin leer que trajo la carga (y con 0 al marcarlos leídos). */
  onUnread?: (n: number) => void
  /** Si hay texto, no se puede escribir (solo lectura) y se muestra este aviso en lugar del cuadro de mensaje. */
  readOnlyNote?: string
}

export function ChatScreen({ api, help, empty, otherLabel, onUnread, readOnlyNote }: ChatScreenProps) {
  const colors = useColors()
  const { t, lang } = useT()
  const [messages, setMessages] = useState<ClientMessageDto[]>([])
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(true)
  const [draft, setDraft] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const scroller = useRef<ScrollView>(null)
  const stick = useRef(true)   // pegado al final: los mensajes nuevos bajan solos

  const load = useCallback(async () => {
    try {
      const page = await api.load(undefined, 30)
      setMessages((cur) => mergeMessages(cur, page.messages ?? []))
      setHasMore((h) => h || !!page.hasMore)
      setError(null)
      if ((page.unread ?? 0) > 0) {
        await api.markRead()
        onUnread?.(0)
      }
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [api, onUnread])

  useEffect(() => {
    void load()
    const id = setInterval(() => void load(), REFRESH_MS)
    return () => clearInterval(id)
  }, [load])

  async function older() {
    const first = messages[0]?.id
    if (first === undefined) return
    stick.current = false
    try {
      const page = await api.load(first, 30)
      setMessages((cur) => mergeMessages(cur, page.messages ?? []))
      setHasMore(!!page.hasMore)
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  async function send() {
    const body = draft.trim()
    if (!body || sending) return
    setSending(true)
    setError(null)
    try {
      const sent = await api.send(body)
      stick.current = true
      setMessages((cur) => mergeMessages(cur, [sent]))
      setDraft('')
    } catch (e) {
      setError(e instanceof ApiError && e.status === 429 ? t('messages.tooMany') : apiErrorMessage(e))
    } finally {
      setSending(false)
    }
  }

  const left = MAX_LENGTH - draft.length
  return (
    <KeyboardAvoidingView style={{ flex: 1, backgroundColor: colors.bg }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <View style={{ flex: 1, width: '100%', maxWidth: 720, alignSelf: 'center' }}>
        <ScrollView
          ref={scroller}
          testID="messages-scroll"
          style={{ flex: 1 }}
          contentContainerStyle={{ padding: 16, gap: 8, flexGrow: 1 }}
          onContentSizeChange={() => stick.current && scroller.current?.scrollToEnd({ animated: false })}
        >
          <Muted>{help}</Muted>
          {hasMore && <Button title={t('messages.older')} variant="secondary" onPress={() => void older()} testID="messages-older" />}
          {loading && messages.length === 0 && <Spinner />}
          {!loading && messages.length === 0 && !error && <Notice tone="info">{empty}</Notice>}
          {messages.map((m) => (
            <View key={m.id} testID={m.mine ? 'bubble-mine' : 'bubble-team'} style={{ alignSelf: m.mine ? 'flex-end' : 'flex-start', maxWidth: '85%' }}>
              {!m.mine && <Text style={{ fontSize: 12, fontWeight: '700', color: colors.muted, marginBottom: 2 }}>{m.senderName} · {otherLabel}</Text>}
              <View style={{ backgroundColor: m.mine ? colors.flow : colors.card, borderColor: colors.line, borderWidth: m.mine ? 0 : 1, borderRadius: 14, paddingHorizontal: 12, paddingVertical: 8 }}>
                <Text selectable style={{ fontSize: 15, lineHeight: 21, color: m.mine ? colors.onFlow : colors.text }}>{m.body}</Text>
              </View>
              <Text style={{ fontSize: 11, color: colors.muted, marginTop: 2, alignSelf: m.mine ? 'flex-end' : 'flex-start' }}>{formatDateTime(m.createdAtUtc, lang)}</Text>
            </View>
          ))}
        </ScrollView>
        {!!error && (
          <View style={{ paddingHorizontal: 16 }}>
            <Notice>{error}</Notice>
          </View>
        )}
        {readOnlyNote ? (
          <View style={{ padding: 12, borderTopWidth: 1, borderTopColor: colors.line, backgroundColor: colors.card }}>
            <Muted>{readOnlyNote}</Muted>
          </View>
        ) : (
        <View style={{ padding: 12, gap: 6, borderTopWidth: 1, borderTopColor: colors.line, backgroundColor: colors.card }}>
          <TextInput
            testID="messages-input"
            accessibilityLabel={t('messages.placeholder')}
            value={draft}
            onChangeText={setDraft}
            placeholder={t('messages.placeholder')}
            placeholderTextColor={colors.muted}
            multiline
            maxLength={MAX_LENGTH}
            style={{ minHeight: 44, maxHeight: 120, borderRadius: 10, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.bg, color: colors.text, paddingHorizontal: 12, paddingVertical: 8, fontSize: 16 }}
          />
          <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
            <Text style={{ fontSize: 12, color: left < 100 ? colors.warn : colors.muted }}>{t('messages.charsLeft', { n: left })}</Text>
            <View style={{ minWidth: 120 }}>
              <Button title={t('messages.send')} onPress={() => void send()} busy={sending} disabled={draft.trim().length === 0} testID="messages-send" />
            </View>
          </View>
        </View>
        )}
      </View>
    </KeyboardAvoidingView>
  )
}
