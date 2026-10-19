// Cinta de ambiente (2026-10-19): en una compilación de pruebas (variante 'staging' o 'test' de app.config.ts, extra.appVariant) el portal del
// cliente muestra SIEMPRE una franja roja arriba («TEST · AMBIENTE DE PRUEBAS») y una etiqueta al pie, en el teléfono y en la web
// (test-portal.advancelogisticspr.com), y en la web antepone la etiqueta al título de la pestaña. En producción y en desarrollo no pinta nada.
import Constants from 'expo-constants'
import { useEffect } from 'react'
import { Platform, StyleSheet, Text, View } from 'react-native'

import { useT } from '../i18n/useT'

const RED = '#DC2626'

/** Etiqueta del ambiente de pruebas de esta compilación ('STAGING' o 'TEST'); null en producción y en desarrollo. */
export function environmentLabel(): string | null {
  const v = (Constants.expoConfig?.extra as { appVariant?: string } | undefined)?.appVariant
  return v === 'staging' ? 'STAGING' : v === 'test' ? 'TEST' : null
}

/** Franja de arriba. */
export function EnvironmentBanner() {
  const label = environmentLabel()
  useEffect(() => {
    if (!label || Platform.OS !== 'web' || typeof document === 'undefined') return
    if (!document.title.startsWith(`[${label}]`)) document.title = `[${label}] ${document.title}`
  }, [label])
  if (!label) return null
  return (
    <View style={styles.bar} testID="environment-banner" accessibilityRole="alert">
      <Text style={styles.text}>{label} · AMBIENTE DE PRUEBAS</Text>
    </View>
  )
}

/** Etiqueta al pie («TEST · Ambiente de pruebas: no es producción»). */
export function EnvironmentFooter() {
  const label = environmentLabel()
  const { t } = useT()
  if (!label) return null
  return (
    <View style={styles.footer} testID="environment-footer">
      <Text style={styles.footerText}>
        {label} · {t('environment.notProduction')}
      </Text>
    </View>
  )
}

const styles = StyleSheet.create({
  bar: { backgroundColor: RED, paddingVertical: 4, alignItems: 'center' },
  text: { color: '#FFFFFF', fontSize: 14, fontWeight: '800', letterSpacing: 1 },
  footer: { backgroundColor: RED, paddingVertical: 3, alignItems: 'center' },
  footerText: { color: '#FFFFFF', fontSize: 11, fontWeight: '700' },
})
