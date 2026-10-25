// Campo de contraseña de TODA la web (Lote 219, pedido de Luis): con el ojito para ver lo que se escribió (por si se cree que se está tecleando mal) y el aviso «Las mayúsculas están activadas».
// Es el único lugar donde se dibuja un campo de contraseña o de PIN: el kit (`TextInput type="password"`), el login, el primer ingreso, restablecer, cambiar contraseña, PIN y reautenticación lo usan.
// El navegador solo dice si las mayúsculas están activadas cuando se oprime una tecla (no hay forma de saberlo antes), así que el aviso sale al teclear y se quita al soltar el campo.
import { useState, type InputHTMLAttributes, type KeyboardEvent, type Ref } from 'react'
import { useT } from '../i18n/useT'
import { IconEye, IconEyeOff } from './icons'

export type PasswordBoxProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & {
  ref?: Ref<HTMLInputElement>
  /** Un PIN (solo números) no avisa de mayúsculas: no tiene letras. */
  pin?: boolean
}

/** ¿Las mayúsculas están activadas según esta tecla? Solo las teclas con letras o símbolos lo dicen con certeza; Mayús y Bloq Mayús mismas sí. */
export function capsLockOn(e: Pick<KeyboardEvent, 'getModifierState'>): boolean {
  return typeof e.getModifierState === 'function' && e.getModifierState('CapsLock')
}

export function PasswordBox({ pin = false, onKeyDown, onKeyUp, onBlur, id, ...rest }: PasswordBoxProps) {
  const t = useT()
  const [shown, setShown] = useState(false)
  const [caps, setCaps] = useState(false)
  const check = (e: KeyboardEvent<HTMLInputElement>) => {
    if (!pin) setCaps(capsLockOn(e))
  }
  return (
    <>
      <div className="pwd-field">
        <input
          {...rest}
          id={id}
          type={shown ? 'text' : 'password'}
          onKeyDown={(e) => {
            check(e)
            onKeyDown?.(e)
          }}
          onKeyUp={(e) => {
            check(e)
            onKeyUp?.(e)
          }}
          onBlur={(e) => {
            setCaps(false)
            onBlur?.(e)
          }}
        />
        <button
          type="button"
          className="iconbtn"
          aria-label={shown ? t('auth.fields.hidePassword') : t('auth.fields.showPassword')}
          aria-pressed={shown}
          title={shown ? t('auth.fields.hidePassword') : t('auth.fields.showPassword')}
          onClick={() => setShown((v) => !v)}
        >
          {shown ? <IconEyeOff /> : <IconEye />}
        </button>
      </div>
      {caps && (
        <p className="caps-warn" role="status" data-testid="caps-warning">
          {t('auth.fields.capsLock')}
        </p>
      )}
    </>
  )
}
