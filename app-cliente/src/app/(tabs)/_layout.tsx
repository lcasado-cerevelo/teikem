// Barra de pestañas del portal: Inicio y Órdenes (con transporte), Almacén (con almacén), Avisos, Mensajes y Ajustes: lo que ofrece la compañía de la sesión (capabilities).
import { Ionicons } from '@expo/vector-icons'
import { Tabs } from 'expo-router'

import { useT } from '../../kernel/i18n/useT'
import { useUnreadMessages } from '../../features/messages/unread'
import { useIsConsignee } from '../../kernel/auth/role'
import { useOffer, visibleTabs } from '../../features/capabilities'
import { useIsWide } from '../../kernel/ui/layout'
import { useColors } from '../../kernel/ui/theme'

export default function TabsLayout() {
  const colors = useColors()
  const { t } = useT()
  const unread = useUnreadMessages()
  const offer = useOffer()
  const wide = useIsWide()   // 2026-10-22: en la web de escritorio el menú va a la izquierda; en celular y web angosta, abajo
  const consignee = useIsConsignee()   // Lote 117: el consignatario con acceso propio solo tiene Envíos, Avisos, Mensajes y Ajustes
  const show = visibleTabs(offer, consignee)
  const icon = (name: keyof typeof Ionicons.glyphMap) => ({ color, size }: { color: unknown; size: number }) => <Ionicons name={name} color={color as string} size={size} />
  return (
    <Tabs
      screenOptions={{
        headerStyle: { backgroundColor: colors.brand },
        headerTintColor: colors.onBrand,
        tabBarActiveTintColor: colors.link,
        tabBarInactiveTintColor: colors.muted,
        tabBarPosition: wide ? 'left' : 'bottom',
        tabBarStyle: { backgroundColor: colors.card, borderTopColor: colors.line, borderRightColor: colors.line },
        sceneStyle: { backgroundColor: colors.bg },
      }}
    >
      <Tabs.Screen name="home" options={{ title: t('tabs.home'), tabBarIcon: icon('home-outline'), href: show.home ? undefined : null }} />
      <Tabs.Screen name="orders" options={{ title: consignee ? t('tabs.shipments') : t('tabs.orders'), tabBarIcon: icon('cube-outline'), href: show.orders ? undefined : null }} />
      <Tabs.Screen name="warehouse" options={{ title: t('tabs.warehouse'), tabBarIcon: icon('layers-outline'), href: show.warehouse ? undefined : null }} />
      <Tabs.Screen name="reports" options={{ title: t('tabs.reports'), tabBarIcon: icon('document-text-outline'), href: !consignee && (show.orders || show.warehouse) ? undefined : null }} />
      <Tabs.Screen name="notices" options={{ title: t('tabs.notices'), tabBarIcon: icon('notifications-outline') }} />
      <Tabs.Screen name="messages" options={{ title: t('tabs.messages'), tabBarIcon: icon('chatbubbles-outline'), tabBarBadge: unread > 0 ? (unread > 99 ? '99+' : unread) : undefined }} />
      <Tabs.Screen name="settings" options={{ title: t('tabs.settings'), tabBarIcon: icon('settings-outline') }} />
    </Tabs>
  )
}
