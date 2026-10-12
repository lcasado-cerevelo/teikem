// Cinta de ambiente (2026-10-12): el APK de staging muestra SIEMPRE, arriba de todas las pantallas, una franja roja «STAGING · AMBIENTE DE
// PRUEBAS», para que nadie la confunda con la de producción (que no muestra nada). La variante la fija app.config.ts (extra.appVariant).
import Constants from 'expo-constants'
import { StyleSheet, Text, View } from 'react-native'

import { colors } from './theme'

export function isStagingBuild(): boolean {
  return (Constants.expoConfig?.extra as { appVariant?: string } | undefined)?.appVariant === 'staging'
}

export function EnvironmentBanner() {
  if (!isStagingBuild()) return null
  return (
    <View style={styles.bar} testID="environment-banner" accessibilityRole="alert">
      <Text style={styles.text}>STAGING · AMBIENTE DE PRUEBAS</Text>
    </View>
  )
}

const styles = StyleSheet.create({
  bar: { backgroundColor: colors.errorStrong, paddingVertical: 4, alignItems: 'center' },
  text: { color: colors.onStrong, fontSize: 14, fontWeight: '800', letterSpacing: 1 },
})
