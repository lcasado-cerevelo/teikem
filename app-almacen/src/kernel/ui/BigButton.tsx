import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native'

import { colors, radius, spacing, touchTarget } from './theme'

export interface BigButtonProps {
  label: string
  onPress: () => void
  variant?: 'primary' | 'secondary' | 'danger'
  disabled?: boolean
  loading?: boolean
  icon?: string
  fullWidth?: boolean
}

/** Botón grande de una sola acción (docs/mobile/app-almacen-plan.md §2: "botones grandes, un campo enfocado"). */
export function BigButton({ label, onPress, variant = 'primary', disabled, loading, icon, fullWidth = true }: BigButtonProps) {
  const isDisabled = disabled || loading
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: isDisabled }}
      onPress={onPress}
      disabled={isDisabled}
      style={({ pressed }) => [
        styles.base,
        styles[variant],
        fullWidth && styles.fullWidth,
        isDisabled && styles.disabled,
        pressed && !isDisabled && styles.pressed,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={colors.text} />
      ) : (
        <View style={styles.row}>
          {icon ? <Text style={styles.icon}>{icon}</Text> : null}
          <Text style={styles.label}>{label}</Text>
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
  label: { color: colors.text, fontSize: 18, fontWeight: '700' },
})
