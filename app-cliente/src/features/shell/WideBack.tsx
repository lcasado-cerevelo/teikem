// «← Volver» de las pantallas de detalle en la web de escritorio (allí no hay encabezado de pila): aparece solo si la ruta no es una sección de la barra superior y hay a dónde volver.
import { usePathname, useRouter } from 'expo-router'
import { Pressable, Text } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { useColors } from '../../kernel/ui/theme'

const SECTIONS = ['/home', '/orders', '/warehouse', '/reports', '/notices', '/messages', '/settings', '/login', '/mfa', '/lock', '/legal', '/confirm-browser', '/accept-invite']

export const isSectionPath = (pathname: string) => pathname === '/' || SECTIONS.includes(pathname)

export function WideBack() {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const pathname = usePathname()
  if (isSectionPath(pathname) || !router.canGoBack()) return null
  return (
    <Pressable accessibilityRole="link" testID="wide-back" onPress={() => router.back()} style={{ alignSelf: 'flex-start', paddingVertical: 4 }}>
      <Text style={{ color: colors.link, fontSize: 15, fontWeight: '700' }}>← {t('common.back')}</Text>
    </Pressable>
  )
}
