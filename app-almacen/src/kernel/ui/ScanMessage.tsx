import { StyleSheet, Text, View } from 'react-native'

import { colors, fontSize, radius, spacing } from './theme'

export interface ScanMessageProps {
  /** Texto del aviso; sin texto no se pinta nada. */
  message: string | null | undefined
  tone: 'error' | 'ok'
}

/**
 * Aviso que sale al escanear (producto o posición no encontrada, línea agregada…): bloque grande, de color y con contraste
 * alto (docs/mobile/mejoras-ux-zebra.md §4.4). Se queda en pantalla hasta la siguiente lectura o acción (la pantalla lo
 * limpia), sin temporizador: así da tiempo a leerlo. `accessibilityLiveRegion` lo anuncia en lectores de pantalla.
 */
export function ScanMessage({ message, tone }: ScanMessageProps) {
  if (!message) return null
  return (
    <View
      style={[styles.block, tone === 'error' ? styles.error : styles.ok]}
      accessibilityRole={tone === 'error' ? 'alert' : 'text'}
      accessibilityLiveRegion="polite"
      testID={`scan-message-${tone}`}
    >
      <Text style={styles.icon}>{tone === 'error' ? '✕' : '✓'}</Text>
      <Text style={styles.text}>{message}</Text>
    </View>
  )
}

const styles = StyleSheet.create({
  block: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.md,
  },
  error: { backgroundColor: colors.errorStrong },
  ok: { backgroundColor: colors.okStrong },
  icon: { color: colors.onStrong, fontSize: 26, fontWeight: '800' },
  text: { flex: 1, color: colors.onStrong, fontSize: fontSize.scanMessage, fontWeight: '700' },
})
