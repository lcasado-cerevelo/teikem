// Barra de pestañas del portal: Inicio, Órdenes, Avisos y Ajustes.
import { Ionicons } from '@expo/vector-icons'
import { Tabs } from 'expo-router'

import { useT } from '../../kernel/i18n/useT'
import { colors } from '../../kernel/ui/ui'

export default function TabsLayout() {
  const { t } = useT()
  const icon = (name: keyof typeof Ionicons.glyphMap) => ({ color, size }: { color: unknown; size: number }) => <Ionicons name={name} color={color as string} size={size} />
  return (
    <Tabs screenOptions={{ headerStyle: { backgroundColor: colors.brand }, headerTintColor: '#fff', tabBarActiveTintColor: colors.flow }}>
      <Tabs.Screen name="home" options={{ title: t('tabs.home'), tabBarIcon: icon('home-outline') }} />
      <Tabs.Screen name="orders" options={{ title: t('tabs.orders'), tabBarIcon: icon('cube-outline') }} />
      <Tabs.Screen name="notices" options={{ title: t('tabs.notices'), tabBarIcon: icon('notifications-outline') }} />
      <Tabs.Screen name="settings" options={{ title: t('tabs.settings'), tabBarIcon: icon('settings-outline') }} />
    </Tabs>
  )
}
