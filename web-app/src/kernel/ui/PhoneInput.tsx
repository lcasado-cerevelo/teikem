// Teléfono con máscara `(xxx)xxx-xxxx` dentro de un `<Field name="…">`. Valor del formulario: el texto con máscara ('' = vacío).
import { useFormContext } from 'react-hook-form'
import { useFieldInfo } from './formContext'
import { formatPhone } from './phone'

export function PhoneInput({ placeholder = '(787)555-1234' }: { placeholder?: string }) {
  const info = useFieldInfo('PhoneInput')
  const { register } = useFormContext()
  const reg = register(info.name)
  return (
    <input
      type="tel"
      inputMode="tel"
      autoComplete="tel"
      maxLength={40}
      placeholder={placeholder}
      id={info.id}
      aria-invalid={info.invalid || undefined}
      aria-required={info.required || undefined}
      aria-describedby={info.describedBy}
      {...reg}
      onChange={(e) => {
        // Se reformatea desde los dígitos: borrar un paréntesis o el guion no deja el texto atascado.
        e.target.value = formatPhone(e.target.value)
        return reg.onChange(e)
      }}
    />
  )
}
