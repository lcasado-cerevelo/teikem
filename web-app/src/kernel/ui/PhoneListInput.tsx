// Lista de teléfonos en un solo texto («+17875550100, +593991234567»), para los campos que guardan varios números juntos (teléfonos extra
// de los avisos). Cada número se captura con el MISMO control que el resto de las pantallas: prefijo + número, solo dígitos y con la máscara de
// la compañía mientras se escribe (si el prefijo es el de la compañía). Se guarda cada número como «+prefijo dígitos» (E.164).
// Es un control suelto (no va dentro de un `<Field>`): `value` y `onChange` son el texto completo de la lista.
import { useState } from 'react'
import { callingCodeDigits, isValidCallingCode, phoneDigits } from '../format/phone'
import { useFormat } from '../format/useFormat'
import { useT } from '../i18n/useT'

const MAX_TOTAL_DIGITS = 15
const SEPARATOR = /[,;]\s*/

/** Los números de la lista, en orden y sin repetidos ni vacíos. */
export function parsePhoneList(value: string | null | undefined): string[] {
  const seen = new Set<string>()
  return String(value ?? '')
    .split(SEPARATOR)
    .map((p) => p.trim())
    .filter((p) => p && !seen.has(p) && seen.add(p))
}

export interface PhoneListInputProps {
  id: string
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}

export function PhoneListInput({ id, value, onChange, disabled }: PhoneListInputProps) {
  const t = useT()
  const f = useFormat()
  const items = parsePhoneList(value)
  const [cc, setCc] = useState(() => f.callingCodeInput(undefined))
  const [number, setNumber] = useState('')

  const companyMask = f.usesCompanyMaskFor(cc)
  const maxDigits = MAX_TOTAL_DIGITS - callingCodeDigits(cc).length
  const render = (text: string, mask = companyMask, max = maxDigits) => (mask ? f.phoneInput(text) : phoneDigits(text).slice(0, max))
  const ccOk = isValidCallingCode(cc) && callingCodeDigits(cc) !== ''
  const canAdd = !disabled && ccOk && number !== '' && f.isValidPhoneFor(number, cc) && phoneDigits(number).length > 0

  function show(item: string): string {
    // «+17875550100» con el prefijo de la compañía se ve con su máscara; otro prefijo, «+prefijo dígitos»
    const digits = phoneDigits(item)
    const company = callingCodeDigits(f.callingCodeInput(undefined))
    if (company && digits.startsWith(company) && f.isValidPhone(digits.slice(company.length)) && digits.length > company.length) return f.phoneFor(digits.slice(company.length), company)
    return item.startsWith('+') ? `+${digits}` : f.phone(item)
  }

  function add() {
    if (!canAdd) return
    const item = `+${callingCodeDigits(cc)}${f.normalizePhoneFor(number, cc)}`
    onChange([...items.filter((i) => i !== item), item].join(', '))
    setNumber('')
  }

  return (
    <div className="phone-list">
      {items.length > 0 && (
        <ul className="phone-list-items" aria-label={t('ui.phone.listLabel')}>
          {items.map((item) => (
            <li key={item} className="chip">
              <span className="mono">{show(item)}</span>
              <button type="button" className="chip-x" disabled={disabled} aria-label={t('ui.phone.remove', { value: show(item) })} onClick={() => onChange(items.filter((i) => i !== item).join(', '))}>
                ×
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="phone-row phone-list-add">
        <input
          type="tel"
          inputMode="numeric"
          maxLength={5}
          placeholder="+1"
          aria-label={t('ui.phone.countryCode')}
          aria-invalid={!ccOk || undefined}
          disabled={disabled}
          value={cc}
          onChange={(e) => {
            // «+» al inicio y solo dígitos (hasta 4)
            const d = e.target.value.replace(/\D/g, '').slice(0, 4)
            const next = d ? `+${d}` : e.target.value.trim().startsWith('+') ? '+' : ''
            setCc(next)
            const mask = f.usesCompanyMaskFor(next)
            setNumber((n) => render(n, mask, MAX_TOTAL_DIGITS - callingCodeDigits(next).length))
          }}
        />
        <input
          id={id}
          type="tel"
          inputMode="numeric"
          autoComplete="off"
          maxLength={40}
          placeholder={companyMask ? f.phonePlaceholder() : ''}
          aria-label={t('ui.phone.number')}
          disabled={disabled}
          value={number}
          onChange={(e) => setNumber(render(e.target.value))}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              add()
            }
          }}
        />
      </div>
      <button type="button" className="btn sm" disabled={!canAdd} onClick={add}>
        {t('ui.phone.add')}
      </button>
    </div>
  )
}
