// Desplegable del kit en la WEB: un <select> nativo del navegador, pintado con la paleta del tema. Se maneja completo con el teclado (Tab llega, flechas cambian, Enter
// opcionalmente «guarda» en la rejilla de captura). Mismas propiedades que la versión del teléfono (Select.tsx).
import { useT } from '../i18n/useT'
import type { SelectProps } from './Select'
import { useColors } from './theme'

export type { SelectOption, SelectProps } from './Select'

export function Select({ value, options, onChange, placeholder, accessibilityLabel, disabled, compact, onEnter, inputRef, testID, invalid }: SelectProps) {
  const c = useColors()
  const { t } = useT()
  return (
    <select
      ref={(el) => inputRef?.(el)}
      aria-label={accessibilityLabel}
      data-testid={testID}
      value={value}
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
        borderRadius: compact ? 8 : 10,
        border: `1px solid ${invalid ? c.danger : c.line}`,
        backgroundColor: c.card,
        color: value ? c.text : c.muted,
        padding: compact ? '0 8px' : '0 12px',
        fontSize: compact ? 14 : 16,
        fontFamily: 'inherit',
        opacity: disabled ? 0.6 : 1,
        cursor: disabled ? 'default' : 'pointer',
      }}
    >
      {(!value || placeholder !== undefined) && <option value="">{placeholder ?? t('select.choose')}</option>}
      {options.map((o) => (
        <option key={o.value} value={o.value} style={{ color: c.text }}>{o.label}</option>
      ))}
    </select>
  )
}
