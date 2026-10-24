// Pie discreto (2026-10-21, pedido de Luis): enlaces a la política de privacidad y los términos de uso para quien ya tiene sesión, los derechos reservados del
// dueño y «Powered by Teikem». Chico y apagado a propósito. Los enlaces abren el documento solo para leer (`/legal-view`), parado en la parte que toca.
import { useRouter } from 'expo-router'
import { Pressable, Text, View } from 'react-native'

import { useT } from '../i18n/useT'
import { useIsWide } from './layout'
import { useColors } from './theme'

export const OWNER_NAME = 'Cerevelo LLC'
export const PRODUCT_NAME = 'Teikem'

export function LegalFooter({ year = new Date().getFullYear() }: { year?: number }) {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const wide = useIsWide()   // 2026-10-23: en la web de escritorio es un pie de página de verdad (todo el ancho, con su línea), pegado al borde de abajo cuando el contenido no llena la ventana
  const tiny = { fontSize: 11, color: colors.muted, opacity: 0.75 } as const
  const open = (part: 'privacy' | 'terms') => router.push({ pathname: '/legal-view', params: { part } })
  return (
    <View accessibilityLabel={t('legalFooter.label')} testID="legal-footer" style={{ alignItems: 'center', gap: 2, paddingVertical: 12, width: '100%', borderTopWidth: wide ? 1 : 0, borderTopColor: colors.line, backgroundColor: wide ? colors.card : 'transparent' }}>
      <View style={{ flexDirection: 'row', gap: 6 }}>
        <Pressable accessibilityRole="link" onPress={() => open('privacy')} testID="legal-footer-privacy" hitSlop={6}>
          <Text style={[tiny, { textDecorationLine: 'underline' }]}>{t('legalFooter.privacy')}</Text>
        </Pressable>
        <Text style={tiny}>·</Text>
        <Pressable accessibilityRole="link" onPress={() => open('terms')} testID="legal-footer-terms" hitSlop={6}>
          <Text style={[tiny, { textDecorationLine: 'underline' }]}>{t('legalFooter.terms')}</Text>
        </Pressable>
      </View>
      <Text style={tiny}>{t('legalFooter.rights', { year: String(year), owner: OWNER_NAME })}</Text>
      <Text style={tiny}>{t('legalFooter.powered', { product: PRODUCT_NAME })}</Text>
    </View>
  )
}
