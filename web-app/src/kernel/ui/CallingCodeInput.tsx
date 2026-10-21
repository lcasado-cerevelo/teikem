// Prefijo internacional del teléfono («+1», «+593») dentro de un `<Field name="countryCallingCode">`, al lado de `PhoneInput`.
// El valor del formulario es el texto con «+»; al guardar, `callingCodeDigits` deja solo los dígitos. Va con `PhoneRow`.
import type { ReactNode } from 'react'
import { useFormContext } from 'react-hook-form'
import { useFieldInfo } from './formContext'

export function CallingCodeInput() {
  const info = useFieldInfo('CallingCodeInput')
  const { register } = useFormContext()
  const reg = register(info.name)
  return (
    <input
      type="tel"
      inputMode="tel"
      maxLength={5}
      placeholder="+1"
      id={info.id}
      aria-invalid={info.invalid || undefined}
      aria-required={info.required || undefined}
      aria-describedby={info.describedBy}
      {...reg}
      onChange={(e) => {
        // «+» al inicio y solo dígitos (hasta 4)
        const digits = e.target.value.replace(/\D/g, '').slice(0, 4)
        e.target.value = digits ? `+${digits}` : e.target.value.trim().startsWith('+') ? '+' : ''
        return reg.onChange(e)
      }}
    />
  )
}

/** Fila prefijo + número: el prefijo angosto y el número ocupa el resto (en 360 px siguen en la misma fila). */
export function PhoneRow({ children }: { children: ReactNode }) {
  return <div className="phone-row">{children}</div>
}
