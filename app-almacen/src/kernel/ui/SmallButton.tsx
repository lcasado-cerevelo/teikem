// Lote 201 — botón pequeño de acción secundaria dentro de una fila (p. ej. «Reasignar»): una pastilla compacta alineada a la derecha, sin ocupar todo el ancho como BigButton.
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { colors, fontSize, radius, spacing } from './theme'

export function SmallButton({ label, onPress, testID }: { label: string; onPress: () => void; testID?: string }) {
  return (
    <View style={styles.wrap}>
      <Pressable accessibilityRole="button" accessibilityLabel={label} onPress={onPress} hitSlop={8} testID={testID} style={({ pressed }) => [styles.pill, pressed && styles.pressed]}>
        <Text style={styles.label}>{label}</Text>
      </Pressable>
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { alignItems: 'flex-end', marginTop: spacing.xs },
  pill: { minHeight: 36, paddingHorizontal: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.brand, justifyContent: 'center', alignItems: 'center' },
  pressed: { backgroundColor: colors.panel },
  label: { color: colors.brand, fontSize: fontSize.message, fontWeight: '700' },
})
