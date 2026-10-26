// El logo de arriba de la pantalla de entrada del portal (Lote 219): el de la compañía (por tema claro u oscuro), o su nombre si no subió logo, o el de Teikem.
import { useEffect, useState } from 'react'
import { Image, Text, View } from 'react-native'

import { loadPublicBrand, pickPublicSlot, usePublicBrand } from '../../kernel/brand/publicBrand'
import { useT } from '../../kernel/i18n/useT'
import { useTheme } from '../../kernel/ui/theme'

// Letras claras para el tema oscuro y oscuras para el claro (si no, «TEIKEM» en blanco desaparece sobre el fondo claro).
const TEIKEM = {
  dark: { es: require('../../../assets/brand-lockup-es.png'), en: require('../../../assets/brand-lockup-en.png') },
  light: { es: require('../../../assets/brand-lockup-es-light.png'), en: require('../../../assets/brand-lockup-en-light.png') },
}

export function LoginBrand() {
  const { lang } = useT()
  const { scheme, colors } = useTheme()
  const brand = usePublicBrand()
  const [failed, setFailed] = useState<string | null>(null)

  useEffect(() => {
    const ctl = new AbortController()
    void loadPublicBrand(ctl.signal)
    return () => ctl.abort()
  }, [])

  const slot = pickPublicSlot(Object.keys(brand.logos), scheme === 'dark')
  const uri = slot ? brand.logos[slot] : undefined
  if (uri && failed !== uri) {
    return (
      <View style={{ alignItems: 'center', paddingVertical: 8 }} testID="login-brand">
        <Image source={{ uri }} accessibilityLabel={brand.companyName ?? 'Logo'} resizeMode="contain" style={{ width: 220, height: 64 }} onError={() => setFailed(uri)} />
      </View>
    )
  }
  if (brand.companyName) {
    return (
      <View style={{ alignItems: 'center', paddingVertical: 8 }} testID="login-brand">
        <Text style={{ color: colors.text, fontSize: 22, fontWeight: '800' }}>{brand.companyName}</Text>
      </View>
    )
  }
  return (
    <View style={{ alignItems: 'center', paddingVertical: 8 }} testID="login-brand">
      <Image source={TEIKEM[scheme === 'dark' ? 'dark' : 'light'][lang === 'en' ? 'en' : 'es']} accessibilityLabel="Teikem" resizeMode="contain" style={{ width: 200, height: 56 }} />
    </View>
  )
}
