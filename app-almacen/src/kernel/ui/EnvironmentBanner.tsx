// Cinta de ambiente (2026-10-12): el APK de staging muestra SIEMPRE, arriba de todas las pantallas, una franja roja «STAGING · AMBIENTE DE
// PRUEBAS», para que nadie la confunda con la de producción (que no muestra nada). La variante la fija app.config.ts (extra.appVariant).
// 2026-10-19: el APK de test (test.advancelogisticspr.com) hace lo mismo con «TEST · AMBIENTE DE PRUEBAS».
import Constants from 'expo-constants'
import { StyleSheet, Text, View } from 'react-native'

import { colors } from './theme'

/** Etiqueta del ambiente de pruebas de esta compilación ('STAGING' o 'TEST'); null en producción y en desarrollo. */
export function environmentLabel(): string | null {
  const v = (Constants.expoConfig?.extra as { appVariant?: string } | undefined)?.appVariant
  return v === 'staging' ? 'STAGING' : v === 'test' ? 'TEST' : null
}

export function isStagingBuild(): boolean {
  return environmentLabel() !== null
}

export function EnvironmentBanner() {
  const label = environmentLabel()
  if (!label) return null
  return (
    <View style={styles.bar} testID="environment-banner" accessibilityRole="alert">
      <Text style={styles.text}>{label} · AMBIENTE DE PRUEBAS</Text>
    </View>
  )
}

const styles = StyleSheet.create({
  bar: { backgroundColor: colors.errorStrong, paddingVertical: 4, alignItems: 'center' },
  text: { color: colors.onStrong, fontSize: 14, fontWeight: '800', letterSpacing: 1 },
})
