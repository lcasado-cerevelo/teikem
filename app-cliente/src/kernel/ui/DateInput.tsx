// Campo de fecha del kit (2026-10-23). Versión del TELÉFONO: se escribe AAAA-MM-DD (teclado numérico). En la web (DateInput.web.tsx) es el <input type="date"> del navegador,
// con su calendario, que se maneja con el teclado. El valor siempre viaja como AAAA-MM-DD ('' = vacío).
import { TextInput } from 'react-native'

import { useColors } from './theme'

export interface DateInputProps {
  value: string
  onChange: (value: string) => void
  accessibilityLabel: string
  /** AAAA-MM-DD mínimo y máximo (solo la web los aplica en el calendario; la validación es de cada pantalla). */
  min?: string
  max?: string
  disabled?: boolean
  compact?: boolean
  invalid?: boolean
  onEnter?: () => void
  inputRef?: (el: { focus: () => void } | null) => void
  testID?: string
}

export function DateInput({ value, onChange, accessibilityLabel, disabled, compact, invalid, onEnter, inputRef, testID }: DateInputProps) {
  const c = useColors()
  return (
    <TextInput
      ref={(el) => inputRef?.(el)}
      accessibilityLabel={accessibilityLabel}
      testID={testID}
      value={value}
      onChangeText={onChange}
      editable={!disabled}
      placeholder="AAAA-MM-DD"
      placeholderTextColor={c.muted}
      autoCapitalize="none"
      keyboardType="numbers-and-punctuation"
      onSubmitEditing={onEnter}
      blurOnSubmit={false}
      style={{ minHeight: compact ? 40 : 48, borderRadius: compact ? 8 : 10, borderWidth: 1, borderColor: invalid ? c.danger : c.line, backgroundColor: c.card, color: c.text, paddingHorizontal: compact ? 8 : 12, fontSize: compact ? 14 : 16, opacity: disabled ? 0.6 : 1 }}
    />
  )
}
