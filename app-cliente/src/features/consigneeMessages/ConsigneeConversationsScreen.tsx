// Mensajes de mis consignatarios (Lote 117): las conversaciones de los consignatarios con acceso propio con este cliente, las más recientes primero, con lo no leído marcado; buscar y filtrar «solo sin leer».
// Al tocar una se abre la conversación para leer y contestar.
import { useRouter } from 'expo-router'
import { useState } from 'react'
import { Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Check, Chip, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { apiErrorMessage } from '../../kernel/api/client'
import { formatDateTime } from '../format'
import { listConsigneeConversations, PAGE_SIZE } from '../portalApi'

export function ConsigneeConversationsScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const [q, setQ] = useState('')
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [take, setTake] = useState(PAGE_SIZE)
  const list = useAsync(() => listConsigneeConversations(unreadOnly, q.trim(), 0, take), [q, unreadOnly, take])

  const items = list.data?.items ?? []
  const total = list.data?.total ?? 0
  return (
    <Screen>
      <Title>{t('consigneeMessages.title')}</Title>
      <Muted>{t('consigneeMessages.openHelp')}</Muted>
      <Field label={t('consigneeMessages.search')} value={q} onChangeText={setQ} placeholder={t('consigneeMessages.searchPlaceholder')} autoCorrect={false} testID="cm-search" />
      <Check label={t('consigneeMessages.unreadOnly')} value={unreadOnly} onChange={setUnreadOnly} testID="cm-unread-only" />
      {!!list.error && <Notice>{apiErrorMessage(list.error)}</Notice>}
      {list.loading && !list.data && <Spinner />}
      {!list.loading && !list.error && items.length === 0 && <Muted>{t('consigneeMessages.empty')}</Muted>}
      {items.map((c) => (
        <Card key={c.publicId} onPress={() => router.push({ pathname: '/consignee-messages/[id]', params: { id: c.publicId as string, name: c.consigneeName ?? '' } })}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Text style={{ fontWeight: '700', color: colors.text }}>{c.consigneeName}</Text>
            {(c.unread ?? 0) > 0 && <Chip label={t('consigneeMessages.unread', { n: c.unread ?? 0 })} tone="warn" />}
          </View>
          <Muted>{c.personName}</Muted>
          {!!c.lastMessagePreview && <Text style={{ color: colors.text }} numberOfLines={2}>{c.lastMessagePreview}</Text>}
          <Muted>{formatDateTime(c.lastMessageAtUtc, lang)}</Muted>
        </Card>
      ))}
      {!list.loading && items.length < total && <Button title={t('consigneeMessages.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => setTake(take + PAGE_SIZE)} />}
    </Screen>
  )
}
