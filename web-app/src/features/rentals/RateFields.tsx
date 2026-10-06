// Lote F17 (Rentas F-R1) — campos de una tarifa de renta (controles sueltos, fuera de un <Form>): frecuencia (Diaria, Semanal,
// Mensual o Fija; vacía = sin tarifa), monto y moneda (la de la compañía si no se elige). La tarifa es solo un dato (D3): no
// se calcula ni se cobra. La usan el selector de equipos, el modal de tarifa de un equipo y la extensión.
import { useId } from 'react'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { useFrequencyOptions } from './useFrequencyOptions'
import type { RateDraft } from './rentalRules'

export interface RateFieldsProps {
  value: RateDraft
  onChange: (next: RateDraft) => void
  /** Error de un campo (del esquema o del servidor). */
  error?: { field: 'frequency' | 'amount'; message: string } | null
  /** Moneda de la compañía (texto de la opción vacía). */
  defaultCurrency?: string
  /** true = la frecuencia no ofrece "Sin tarifa" (el modal de la tarifa de un equipo). */
  required?: boolean
  disabled?: boolean
}

export function RateFields({ value, onChange, error, defaultCurrency = '', required = false, disabled }: RateFieldsProps) {
  const t = useT()
  const id = useId()
  const frequencies = useFrequencyOptions()
  const { data: currencies = [] } = useLookups('Currency')
  const errFreq = error?.field === 'frequency' ? error.message : null
  const errAmount = error?.field === 'amount' ? error.message : null

  return (
    <div className="r3 ren-rate-fields">
      <div className="f">
        <label htmlFor={`${id}-freq`}>
          {t('rentals.rate.frequency')}
          {required && <span className="req" aria-hidden="true"> *</span>}
        </label>
        <select
          id={`${id}-freq`}
          value={value.frequency}
          disabled={disabled}
          aria-invalid={errFreq ? true : undefined}
          aria-describedby={errFreq ? `${id}-freq-err` : undefined}
          onChange={(e) => onChange({ ...value, frequency: e.target.value })}
        >
          <option value="">{required ? t('rentals.rate.pickFrequency') : t('rentals.rate.none')}</option>
          {frequencies.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        {errFreq && (
          <p className="ferr" id={`${id}-freq-err`} role="alert">
            {errFreq}
          </p>
        )}
      </div>
      <div className="f">
        <label htmlFor={`${id}-amount`}>{t('rentals.rate.amount')}</label>
        <input
          id={`${id}-amount`}
          type="number"
          inputMode="decimal"
          step="0.01"
          min={0}
          disabled={disabled}
          value={value.amount ?? ''}
          aria-invalid={errAmount ? true : undefined}
          aria-describedby={errAmount ? `${id}-amount-err` : undefined}
          onChange={(e) => onChange({ ...value, amount: e.target.value === '' ? null : Number(e.target.value) })}
        />
        {errAmount && (
          <p className="ferr" id={`${id}-amount-err`} role="alert">
            {errAmount}
          </p>
        )}
      </div>
      <div className="f">
        <label htmlFor={`${id}-cur`}>{t('rentals.rate.currency')}</label>
        <select id={`${id}-cur`} value={value.currency} disabled={disabled} onChange={(e) => onChange({ ...value, currency: e.target.value })}>
          <option value="">{defaultCurrency ? t('rentals.rate.companyCurrencyCode', { code: defaultCurrency }) : t('rentals.rate.companyCurrency')}</option>
          {currencies.map((c) => (
            <option key={c.code} value={c.code}>
              {c.code} · {c.label}
            </option>
          ))}
        </select>
      </div>
    </div>
  )
}
