// Campo de fecha del kit en la WEB: el <input type="date"> del navegador (calendario propio, se escribe y se recorre con el teclado), pintado con la paleta del tema. Mismas
// propiedades que la versión del teléfono (DateInput.tsx); el valor es AAAA-MM-DD.
import type { DateInputProps } from './DateInput'
import { useColors } from './theme'

export type { DateInputProps } from './DateInput'

export function DateInput({ value, onChange, accessibilityLabel, min, max, disabled, compact, invalid, onEnter, inputRef, testID }: DateInputProps) {
  const c = useColors()
  return (
    <input
      ref={(el) => inputRef?.(el)}
      type="date"
      aria-label={accessibilityLabel}
      data-testid={testID}
      value={value}
      min={min || undefined}
      max={max || undefined}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
      onKeyDown={(e) => {
        if (e.key === 'Enter' && onEnter) {
          e.preventDefault()
          onEnter()
        }
      }}
      style={{
        minHeight: compact ? 40 : 48,
        width: '100%',
        boxSizing: 'border-box',
        borderRadius: compact ? 8 : 10,
        border: `1px solid ${invalid ? c.danger : c.line}`,
        backgroundColor: c.card,
        color: c.text,
        padding: compact ? '0 8px' : '0 12px',
        fontSize: compact ? 14 : 16,
        fontFamily: 'inherit',
        colorScheme: c.bg === '#F1F3F8' ? 'light' : 'dark',
        opacity: disabled ? 0.6 : 1,
      }}
    />
  )
}
