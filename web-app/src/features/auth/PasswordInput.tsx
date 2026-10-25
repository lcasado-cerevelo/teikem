import { PasswordBox } from '../../kernel/ui/PasswordBox'

/** Campo de contraseña con el ojito y el aviso de mayúsculas (pedido del dueño 2026-10-07: al crear la contraseña por primera vez y al confirmarla, poder revisar que no hay un error). Es el `PasswordBox` del kit con
 *  la interfaz sencilla (valor y onChange) que usan el primer ingreso y restablecer la contraseña. */
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
  return <PasswordBox id={id} autoComplete={autoComplete} autoFocus={autoFocus} value={value} onChange={(e) => onChange(e.target.value)} />
}
