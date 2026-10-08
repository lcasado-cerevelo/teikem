import { useState } from 'react'
import { useT } from '../../kernel/i18n/useT'
import { IconEye, IconEyeOff } from '../../kernel/ui/icons'

/** Campo de contraseña con el ojito para ver lo que se escribió (pedido del dueño 2026-10-07: al crear la contraseña por primera vez y al
 *  confirmarla, poder revisar que no hay un error). Mismo marcado que el de la pantalla de ingreso (`.pwd-field` + `.iconbtn`). */
export function PasswordInput({
  id,
  value,
  onChange,
  autoFocus,
  autoComplete = 'new-password',
}: {
  id: string
  value: string
  onChange: (value: string) => void
  autoFocus?: boolean
  autoComplete?: string
}) {
  const t = useT()
  const [shown, setShown] = useState(false)
  return (
    <div className="pwd-field">
      <input id={id} type={shown ? 'text' : 'password'} autoComplete={autoComplete} autoFocus={autoFocus} value={value} onChange={(e) => onChange(e.target.value)} />
      <button type="button" className="iconbtn" aria-label={shown ? t('auth.fields.hidePassword') : t('auth.fields.showPassword')} aria-pressed={shown} onClick={() => setShown((v) => !v)}>
        {shown ? <IconEyeOff /> : <IconEye />}
      </button>
    </div>
  )
}
