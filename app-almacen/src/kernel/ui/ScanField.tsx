import { useCallback, useState } from 'react'
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native'

import { useT } from '../i18n/useT'
import { useScanner } from '../scanner/useScanner'
import { colors, radius, spacing } from './theme'

export interface ScanFieldProps {
  label: string
  help?: string
  error?: string | null
  onSubmit: (code: string) => void
  autoFocus?: boolean
  keyboardType?: 'default' | 'numeric'
  /** Valor sugerido (p. ej. la posición destino): muestra el botón "Usar {valor}" que lo toma sin escribirlo. */
  suggestedValue?: string | null
}

/**
 * Campo único de captura (docs/mobile/app-almacen-plan.md §2, "un solo campo enfocado; el escaneo escribe y avanza").
 * Recibe tanto el teclado (físico o en pantalla, Enter = escanear) como una lectura de DataWedge en modo intent.
 */
export function ScanField({ label, help, error, onSubmit, autoFocus = true, keyboardType = 'default', suggestedValue }: ScanFieldProps) {
  const { t } = useT()
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
      <View style={styles.inputRow}>
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
        {/* en un teléfono sin lector no hay un Enter evidente: "Aceptar" toma lo escrito como si se hubiera escaneado */}
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={t('scan.accept')}
          disabled={!value.trim()}
          onPress={() => submit(value)}
          style={[styles.acceptBtn, !value.trim() && styles.btnDisabled]}
        >
          <Text style={styles.acceptLabel}>{t('scan.accept')}</Text>
        </Pressable>
      </View>
      {suggestedValue ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={t('scan.useSuggested', { value: suggestedValue })}
          onPress={() => submit(suggestedValue)}
          style={styles.suggestBtn}
        >
          <Text style={styles.suggestLabel}>{t('scan.useSuggested', { value: suggestedValue })}</Text>
        </Pressable>
      ) : null}
      {error ? <Text style={styles.error}>{error}</Text> : help ? <Text style={styles.help}>{help}</Text> : null}
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs },
  inputRow: { flexDirection: 'row', alignItems: 'stretch', gap: spacing.sm },
  label: { color: colors.text, fontSize: 16, fontWeight: '600' },
  input: {
    flex: 1,
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
  acceptBtn: {
    minHeight: 56,
    minWidth: 96,
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.brand,
    alignItems: 'center',
    justifyContent: 'center',
  },
  btnDisabled: { opacity: 0.45 },
  acceptLabel: { color: colors.text, fontSize: 18, fontWeight: '700' },
  suggestBtn: {
    minHeight: 48,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.brand,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
  },
  suggestLabel: { color: colors.text, fontSize: 16, fontWeight: '700' },
  help: { color: colors.muted, fontSize: 13 },
  error: { color: colors.error, fontSize: 13 },
})
