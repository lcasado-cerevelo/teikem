// Teléfono dentro de un `<Field name="…">`, al lado de `CallingCodeInput` (prefijo). Solo deja teclear números: cualquier otro carácter se
// descarta. Con el prefijo de la compañía (o sin prefijo) se va pintando con la máscara de la compañía (Región y formatos) mientras se escribe;
// con otro prefijo (+593…) la máscara de la compañía no aplica: quedan los dígitos tal cual, hasta completar 15 con el prefijo.
// El valor del formulario es el texto que se ve ('' = vacío); al guardar, `normalizePhoneFor` deja solo los dígitos.
import { useEffect } from 'react'
import { useFormContext, useWatch } from 'react-hook-form'
import { callingCodeDigits, phoneDigits } from '../format/phone'
import { useFormat } from '../format/useFormat'
import { useFieldInfo } from './formContext'

/** Máximo de dígitos de un teléfono internacional (E.164) contando el prefijo. */
const MAX_TOTAL_DIGITS = 15

interface PhoneInputProps {
  placeholder?: string
  /** Nombre del campo del prefijo en el mismo formulario (por omisión `countryCallingCode`); sin ese campo rige la máscara de la compañía. */
  countryField?: string
}

export function PhoneInput({ placeholder, countryField = 'countryCallingCode' }: PhoneInputProps) {
  const info = useFieldInfo('PhoneInput')
  const { register, control, getValues, setValue } = useFormContext()
  const f = useFormat()
  const cc = useWatch({ control, name: countryField }) as string | undefined
  const companyMask = f.usesCompanyMaskFor(cc)
  const maxDigits = MAX_TOTAL_DIGITS - callingCodeDigits(cc).length
  const reg = register(info.name)

  const render = (text: string) => (companyMask ? f.phoneInput(text) : phoneDigits(text).slice(0, maxDigits))

  // Si cambia el prefijo con el número ya escrito, se vuelve a pintar (con máscara ↔ solo dígitos).
  useEffect(() => {
    const current = String(getValues(info.name) ?? '')
    if (!current) return
    const next = render(current)
    if (next !== current) setValue(info.name, next, { shouldDirty: true, shouldValidate: false })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [companyMask, maxDigits])

  return (
    <input
      type="tel"
      inputMode="numeric"
      autoComplete="tel-national"
      maxLength={40}
      placeholder={placeholder ?? (companyMask ? f.phonePlaceholder() : '')}
      id={info.id}
      aria-invalid={info.invalid || undefined}
      aria-required={info.required || undefined}
      aria-describedby={info.describedBy}
      {...reg}
      onChange={(e) => {
        // Se vuelve a pintar desde los dígitos: borrar un paréntesis o el guion no deja el texto atascado, y nada que no sea número entra.
        e.target.value = render(e.target.value)
        return reg.onChange(e)
      }}
    />
  )
}
