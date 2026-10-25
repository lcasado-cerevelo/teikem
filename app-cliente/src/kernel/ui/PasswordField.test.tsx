// El campo de contraseña de todo el portal (Lote 219): ojito y aviso de mayúsculas (web).
import { fireEvent, render, screen } from '@testing-library/react-native'

import { setLang } from '../i18n/i18n'
import { capsLockFrom, PasswordField } from './PasswordField'

beforeEach(() => setLang('es'))

describe('capsLockFrom', () => {
  it('lee las mayúsculas del evento del navegador, también dentro de nativeEvent', () => {
    expect(capsLockFrom({ getModifierState: (k: string) => k === 'CapsLock' })).toBe(true)
    expect(capsLockFrom({ nativeEvent: { getModifierState: () => true } })).toBe(true)
    expect(capsLockFrom({ getModifierState: () => false })).toBe(false)
  })
  it('sin esa información (el teléfono) no avisa', () => {
    expect(capsLockFrom({})).toBe(false)
    expect(capsLockFrom(null)).toBe(false)
    expect(capsLockFrom({ getModifierState: () => { throw new Error('x') } })).toBe(false)
  })
})

describe('PasswordField', () => {
  it('oculta lo escrito y el ojito lo muestra y lo vuelve a ocultar', async () => {
    await render(<PasswordField label="Contraseña" value="Secreta" onChangeText={() => undefined} testID="pw" />)
    expect(screen.getByTestId('pw').props.secureTextEntry).toBe(true)
    await fireEvent.press(screen.getByTestId('pw-eye'))
    expect(screen.getByTestId('pw').props.secureTextEntry).toBe(false)
    expect(screen.getByLabelText('Ocultar contraseña')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('pw-eye'))
    expect(screen.getByTestId('pw').props.secureTextEntry).toBe(true)
  })
  it('muestra el error del campo', async () => {
    await render(<PasswordField label="Contraseña" value="" onChangeText={() => undefined} error="Muy corta" />)
    expect(screen.getByText('Muy corta')).toBeTruthy()
  })
})
