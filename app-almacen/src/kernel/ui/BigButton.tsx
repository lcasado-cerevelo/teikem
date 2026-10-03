import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native'

import { colors, fontSize, radius, spacing, tileHeight, touchTarget } from './theme'

export interface BigButtonProps {
  label: string
  onPress: () => void
  variant?: 'primary' | 'secondary' | 'danger'
  disabled?: boolean
  loading?: boolean
  icon?: string
  fullWidth?: boolean
  /** 'tile': botón alto (88 dp) con el icono arriba y el texto debajo, para la grilla de dos columnas de Inicio. */
  layout?: 'row' | 'tile'
  testID?: string
}

/** Botón grande de una sola acción (docs/mobile/app-almacen-plan.md §2: "botones grandes, un campo enfocado"). */
export function BigButton({ label, onPress, variant = 'primary', disabled, loading, icon, fullWidth = true, layout = 'row', testID }: BigButtonProps) {
  const tile = layout === 'tile'
  const isDisabled = disabled || loading
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: isDisabled }}
      onPress={onPress}
      disabled={isDisabled}
      testID={testID}
      style={({ pressed }) => [
        styles.base,
        tile && styles.tile,
        styles[variant],
        fullWidth && styles.fullWidth,
        isDisabled && styles.disabled,
        pressed && !isDisabled && styles.pressed,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={colors.text} />
      ) : (
        <View style={tile ? styles.column : styles.row}>
          {icon ? <Text style={tile ? styles.tileIcon : styles.icon}>{icon}</Text> : null}
          <Text style={[styles.label, tile && styles.tileLabel]}>{label}</Text>
        </View>
      )}
    </Pressable>
  )
}

const styles = StyleSheet.create({
  base: {
    minHeight: touchTarget,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  fullWidth: { alignSelf: 'stretch' },
  primary: { backgroundColor: colors.brand },
  secondary: { backgroundColor: colors.panelAlt, borderWidth: 1, borderColor: colors.line },
  danger: { backgroundColor: colors.error },
  disabled: { opacity: 0.5 },
  pressed: { opacity: 0.85 },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  icon: { fontSize: 22 },
  label: { color: colors.text, fontSize: fontSize.button, fontWeight: '700' },
  tile: { minHeight: tileHeight, paddingHorizontal: spacing.sm, paddingVertical: spacing.sm },
  column: { alignItems: 'center', gap: spacing.xs },
  tileIcon: { fontSize: 30 },
  tileLabel: { textAlign: 'center' },
})
