import { useCallback, useState } from 'react'
import { StyleSheet, Text, TextInput, View } from 'react-native'

import { useScanner } from '../scanner/useScanner'
import { colors, radius, spacing } from './theme'

export interface ScanFieldProps {
  label: string
  help?: string
  error?: string | null
  onSubmit: (code: string) => void
  autoFocus?: boolean
  keyboardType?: 'default' | 'numeric'
}

/**
 * Campo único de captura (docs/mobile/app-almacen-plan.md §2, "un solo campo enfocado; el escaneo escribe y avanza").
 * Recibe tanto el teclado (físico o en pantalla, Enter = escanear) como una lectura de DataWedge en modo intent.
 */
export function ScanField({ label, help, error, onSubmit, autoFocus = true, keyboardType = 'default' }: ScanFieldProps) {
  const [value, setValue] = useState('')

  const submit = useCallback(
    (code: string) => {
      const trimmed = code.trim()
      if (!trimmed) return
      onSubmit(trimmed)
      setValue('')
    },
    [onSubmit],
  )

  useScanner(submit)

  return (
    <View style={styles.wrap}>
      <Text style={styles.label}>{label}</Text>
      <TextInput
        value={value}
        onChangeText={setValue}
        onSubmitEditing={(e) => submit(e.nativeEvent.text)}
        autoFocus={autoFocus}
        blurOnSubmit={false}
        returnKeyType="done"
        keyboardType={keyboardType}
        style={[styles.input, error && styles.inputError]}
        placeholder={help}
        placeholderTextColor={colors.muted}
        accessibilityLabel={label}
      />
      {error ? <Text style={styles.error}>{error}</Text> : help ? <Text style={styles.help}>{help}</Text> : null}
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs },
  label: { color: colors.text, fontSize: 16, fontWeight: '600' },
  input: {
    minHeight: 56,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
  inputError: { borderColor: colors.error },
  help: { color: colors.muted, fontSize: 13 },
  error: { color: colors.error, fontSize: 13 },
})
