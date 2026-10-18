// Barra de pestañas del portal: Inicio, Órdenes, Avisos, Mensajes y Ajustes.
import { Ionicons } from '@expo/vector-icons'
import { Tabs } from 'expo-router'

import { useT } from '../../kernel/i18n/useT'
import { useUnreadMessages } from '../../features/messages/unread'
import { useIsConsignee } from '../../kernel/auth/role'
import { useColors } from '../../kernel/ui/theme'

export default function TabsLayout() {
  const colors = useColors()
  const { t } = useT()
  const unread = useUnreadMessages()
  const consignee = useIsConsignee()   // Lote 117: el consignatario con acceso propio solo tiene Envíos, Avisos, Mensajes y Ajustes
  const icon = (name: keyof typeof Ionicons.glyphMap) => ({ color, size }: { color: unknown; size: number }) => <Ionicons name={name} color={color as string} size={size} />
  return (
    <Tabs
      screenOptions={{
        headerStyle: { backgroundColor: colors.brand },
        headerTintColor: colors.onBrand,
        tabBarActiveTintColor: colors.link,
        tabBarInactiveTintColor: colors.muted,
        tabBarStyle: { backgroundColor: colors.card, borderTopColor: colors.line },
        sceneStyle: { backgroundColor: colors.bg },
      }}
    >
      <Tabs.Screen name="home" options={{ title: t('tabs.home'), tabBarIcon: icon('home-outline'), href: consignee ? null : undefined }} />
      <Tabs.Screen name="orders" options={{ title: consignee ? t('tabs.shipments') : t('tabs.orders'), tabBarIcon: icon('cube-outline') }} />
      <Tabs.Screen name="notices" options={{ title: t('tabs.notices'), tabBarIcon: icon('notifications-outline') }} />
      <Tabs.Screen name="messages" options={{ title: t('tabs.messages'), tabBarIcon: icon('chatbubbles-outline'), tabBarBadge: unread > 0 ? (unread > 99 ? '99+' : unread) : undefined }} />
      <Tabs.Screen name="settings" options={{ title: t('tabs.settings'), tabBarIcon: icon('settings-outline') }} />
    </Tabs>
  )
}
