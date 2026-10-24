// Pestaña «Mensajes». Una persona del cliente tiene su conversación con el equipo de servicio al cliente y las conversaciones de sus consignatarios con acceso (Lote 117); un consignatario con
// acceso propio ve su conversación con su cliente. 2026-10-23 (Luis: «esa pantalla está como para el teléfono»): en la web ancha es una bandeja de DOS columnas —a la izquierda la lista de
// conversaciones (el equipo primero, luego los consignatarios, con lo sin leer, buscador y «solo sin leer»), a la derecha la conversación abierta—; en el celular sigue la conversación del
// equipo con el acceso a las de los consignatarios arriba.
import { useRouter } from 'expo-router'
import { useEffect, useState } from 'react'
import { Pressable, ScrollView, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useIsConsignee } from '../../kernel/auth/role'
import { useT } from '../../kernel/i18n/useT'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Check, Chip, Field, Muted, Notice, Spinner, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { ConsigneeConversationChat } from '../consigneeMessages/ConsigneeConversationScreen'
import { ConsigneeMessagesScreen } from '../consigneeSpace/ConsigneeMessagesScreen'
import { formatDateTime } from '../format'
import { type ConsigneeConversationSummaryDto, getConsigneeConversationsUnread, listConsigneeConversations, PAGE_SIZE } from '../portalApi'
import { MessagesScreen } from './MessagesScreen'
import { useUnreadMessages } from './unread'

export function MessagesTab() {
  const consignee = useIsConsignee()
  const wide = useIsWide()
  if (consignee) return <ConsigneeMessagesScreen />
  return wide ? <MessagesDesk /> : <ClientMessagesTab />
}

/** Celular: la conversación con el equipo y, arriba, el acceso a las de los consignatarios. */
function ClientMessagesTab() {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const [unread, setUnreadCount] = useState(0)
  useEffect(() => {
    let alive = true
    getConsigneeConversationsUnread().then((r) => alive && setUnreadCount(r.unread ?? 0)).catch(() => undefined)
    return () => {
      alive = false
    }
  }, [])
  return (
    <View style={{ flex: 1 }}>
      <View style={{ padding: 12, gap: 6, borderBottomWidth: 1, borderBottomColor: colors.line, backgroundColor: colors.card }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <Text style={{ fontWeight: '700', color: colors.text }}>{t('consigneeMessages.open')}</Text>
          {unread > 0 && <Chip label={t('consigneeMessages.unread', { n: unread })} tone="warn" />}
        </View>
        <Button title={t('consigneeMessages.openButton')} variant="secondary" onPress={() => router.push('/consignee-messages')} testID="open-consignee-messages" />
      </View>
      <MessagesScreen />
    </View>
  )
}

type Selection = { kind: 'team' } | { kind: 'consignee'; id: string; name: string }

/** Escritorio: lista de conversaciones a la izquierda y la conversación abierta a la derecha, del mismo ancho y alto. */
function MessagesDesk() {
  const colors = useColors()
  const { t, lang } = useT()
  const total = useUnreadMessages()
  const [q, setQ] = useState('')
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [take, setTake] = useState(PAGE_SIZE)
  const [sel, setSel] = useState<Selection>({ kind: 'team' })
  const list = useAsync(() => listConsigneeConversations(unreadOnly, q.trim(), 0, take), [q, unreadOnly, take])
  const items: ConsigneeConversationSummaryDto[] = list.data?.items ?? []
  const listTotal = list.data?.total ?? 0
  const consigneesUnread = items.reduce((n, c) => n + (c.unread ?? 0), 0)
  const teamUnread = Math.max(total - consigneesUnread, 0)

  const item = (key: string, title: string, subtitle: string | undefined, preview: string | undefined, when: string | undefined, unread: number, active: boolean, onPress: () => void, testID: string) => (
    <Pressable
      key={key}
      accessibilityRole="button"
      accessibilityState={{ selected: active }}
      testID={testID}
      onPress={onPress}
      style={({ hovered }: { pressed: boolean; hovered?: boolean }) => ({ padding: 12, gap: 2, borderBottomWidth: 1, borderBottomColor: colors.line, backgroundColor: active ? colors.neutralBg : hovered ? colors.bg : colors.card, borderLeftWidth: 3, borderLeftColor: active ? colors.flow : 'transparent' })}
    >
      <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <Text numberOfLines={1} style={{ fontWeight: unread > 0 ? '800' : '700', color: colors.text, flexShrink: 1 }}>{title}</Text>
        {unread > 0 && <Chip label={String(unread)} tone="warn" />}
      </View>
      {!!subtitle && <Muted>{subtitle}</Muted>}
      {!!preview && <Text numberOfLines={1} style={{ color: colors.text, fontSize: 13 }}>{preview}</Text>}
      {!!when && <Text style={{ fontSize: 11, color: colors.muted }}>{when}</Text>}
    </Pressable>
  )

  return (
    <View style={{ flex: 1, flexDirection: 'row', backgroundColor: colors.bg }} testID="messages-desk">
      <View style={{ width: 340, borderRightWidth: 1, borderRightColor: colors.line, backgroundColor: colors.card }}>
        <View style={{ padding: 12, gap: 8, borderBottomWidth: 1, borderBottomColor: colors.line }}>
          <Text accessibilityRole="header" style={{ fontSize: 18, fontWeight: '800', color: colors.text }}>{t('messages.inbox')}</Text>
          <Field label={t('consigneeMessages.search')} value={q} onChangeText={setQ} placeholder={t('consigneeMessages.searchPlaceholder')} autoCorrect={false} testID="cm-search" />
          <Check label={t('consigneeMessages.unreadOnly')} value={unreadOnly} onChange={setUnreadOnly} testID="cm-unread-only" />
        </View>
        <ScrollView style={{ flex: 1 }}>
          {item('team', t('messages.team'), t('messages.teamSubtitle'), undefined, undefined, teamUnread, sel.kind === 'team', () => setSel({ kind: 'team' }), 'conv-team')}
          <Text style={{ fontSize: 12, fontWeight: '700', letterSpacing: 0.4, textTransform: 'uppercase', color: colors.muted, paddingHorizontal: 12, paddingTop: 12, paddingBottom: 4 }}>{t('consigneeMessages.title')}</Text>
          {!!list.error && <View style={{ padding: 12 }}><Notice>{apiErrorMessage(list.error)}</Notice></View>}
          {list.loading && !list.data && <Spinner />}
          {!list.loading && !list.error && items.length === 0 && <View style={{ padding: 12 }}><Muted>{t('consigneeMessages.empty')}</Muted></View>}
          {items.map((c) =>
            item(String(c.publicId), c.consigneeName ?? '', c.personName ?? undefined, c.lastMessagePreview ?? undefined, c.lastMessageAtUtc ? formatDateTime(c.lastMessageAtUtc, lang) : undefined, c.unread ?? 0, sel.kind === 'consignee' && sel.id === c.publicId, () => setSel({ kind: 'consignee', id: c.publicId as string, name: c.consigneeName ?? '' }), `conv-${c.publicId}`),
          )}
          {!list.loading && items.length < listTotal && (
            <View style={{ padding: 12 }}>
              <Button title={t('consigneeMessages.loadMore', { shown: items.length, total: listTotal })} variant="secondary" onPress={() => setTake(take + PAGE_SIZE)} />
            </View>
          )}
          {!list.loading && listTotal > 0 && <Text style={{ fontSize: 12, color: colors.muted, padding: 12 }}>{t('table.count', { shown: items.length, total: listTotal })}</Text>}
        </ScrollView>
      </View>
      <View style={{ flex: 1, minWidth: 0 }}>
        <View style={{ paddingHorizontal: 16, paddingVertical: 10, borderBottomWidth: 1, borderBottomColor: colors.line, backgroundColor: colors.card }}>
          <Text accessibilityRole="header" style={{ fontSize: 16, fontWeight: '800', color: colors.text }} testID="conv-title">{sel.kind === 'team' ? t('messages.team') : sel.name}</Text>
        </View>
        {sel.kind === 'team' ? <MessagesScreen /> : <ConsigneeConversationChat id={sel.id} name={sel.name} />}
      </View>
    </View>
  )
}
