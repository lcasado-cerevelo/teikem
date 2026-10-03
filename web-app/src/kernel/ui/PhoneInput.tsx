// Teléfono con la máscara de la compañía (Región y formatos) dentro de un `<Field name="…">`. Mientras se escribe se pone la
// máscara; el valor del formulario es el texto con máscara ('' = vacío). Al guardar, `normalizePhone` deja solo los dígitos.
import { useFormContext } from 'react-hook-form'
import { useFormat } from '../format/useFormat'
import { useFieldInfo } from './formContext'

export function PhoneInput({ placeholder }: { placeholder?: string }) {
  const info = useFieldInfo('PhoneInput')
  const { register } = useFormContext()
  const f = useFormat()
  const reg = register(info.name)
  return (
    <input
      type="tel"
      inputMode="tel"
      autoComplete="tel"
      maxLength={40}
      placeholder={placeholder ?? f.phonePlaceholder()}
      id={info.id}
      aria-invalid={info.invalid || undefined}
      aria-required={info.required || undefined}
      aria-describedby={info.describedBy}
      {...reg}
      onChange={(e) => {
        // Se reformatea desde los dígitos: borrar un paréntesis o el guion no deja el texto atascado.
        e.target.value = f.phoneInput(e.target.value)
        return reg.onChange(e)
      }}
    />
  )
}
