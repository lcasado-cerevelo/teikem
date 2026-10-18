// Pestaña «Mensajes»: una persona del cliente ve su conversación con el equipo de servicio al cliente y, arriba, el acceso a los mensajes de sus consignatarios (con cuántos tiene sin leer); un consignatario con acceso
// propio ve su conversación con su cliente (Lote 117).
import { useRouter } from 'expo-router'
import { useEffect, useState } from 'react'
import { Text, View } from 'react-native'

import { useIsConsignee } from '../../kernel/auth/role'
import { useT } from '../../kernel/i18n/useT'
import { Button, Chip, useColors } from '../../kernel/ui/ui'
import { ConsigneeMessagesScreen } from '../consigneeSpace/ConsigneeMessagesScreen'
import { getConsigneeConversationsUnread } from '../portalApi'
import { MessagesScreen } from './MessagesScreen'

export function MessagesTab() {
  return useIsConsignee() ? <ConsigneeMessagesScreen /> : <ClientMessagesTab />
}

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
