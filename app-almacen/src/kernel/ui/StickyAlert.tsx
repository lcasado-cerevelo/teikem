// Aviso fijo (pedido del dueño 2026-10-06): se queda a la vista aunque la pantalla se desplace y se puede cerrar con la ✕.
// Se pasa a `KeyboardScreen` en su propiedad `banner`, que lo dibuja encima del área desplazable.
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useT } from '../i18n/useT'
import { colors, fontSize, radius, spacing } from './theme'

export interface StickyAlertProps {
  message: string
  onClose: () => void
  testID?: string
}

export function StickyAlert({ message, onClose, testID = 'sticky-alert' }: StickyAlertProps) {
  const { t } = useT()
  return (
    <View style={styles.box} accessibilityRole="alert" testID={testID}>
      <Text style={styles.text}>{message}</Text>
      <Pressable accessibilityRole="button" accessibilityLabel={t('common.closeAlert')} onPress={onClose} style={styles.close} hitSlop={8}>
        <Text style={styles.closeLabel}>✕</Text>
      </Pressable>
    </View>
  )
}

const styles = StyleSheet.create({
  box: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    marginHorizontal: spacing.lg,
    marginTop: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.warn,
    backgroundColor: colors.panelAlt,
  },
  text: { flex: 1, color: colors.text, fontSize: fontSize.message, fontWeight: '600' },
  close: { minWidth: 44, minHeight: 44, alignItems: 'center', justifyContent: 'center' },
  closeLabel: { color: colors.text, fontSize: 22, fontWeight: '700' },
})
