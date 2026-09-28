import { Image, StyleSheet } from 'react-native'

import type { Lang } from '../i18n/i18n'
import { useT } from '../i18n/useT'

// Lote F8a (P8) — lockup de Teikem (símbolo + TEIKEM + lema) en su variante -inv, porque la app usa tema oscuro. El
// lema va en el idioma activo y cambia sin recargar. Los PNG (1x/@2x/@3x, 240x77 de base) los genera
// scripts/brand-icons.mjs desde Logos/; Metro elige la densidad del aparato.
const LOCKUPS: Record<Lang, number> = {
  es: require('../../../assets/brand-lockup-es.png') as number,
  en: require('../../../assets/brand-lockup-en.png') as number,
}

/** Encabezado de marca de las pantallas de acceso (Registrar y Entrar). */
export function BrandLockup() {
  const { lang } = useT()
  return (
    <Image
      source={LOCKUPS[lang]}
      style={styles.lockup}
      resizeMode="contain"
      accessibilityRole="image"
      accessibilityLabel="Teikem"
      testID={`brand-lockup-${lang}`}
    />
  )
}

const styles = StyleSheet.create({
  lockup: { width: 240, height: 77, alignSelf: 'center' },
})
