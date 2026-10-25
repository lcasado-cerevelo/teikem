// «¿Olvidó su contraseña?» del portal (Lote 219): pedir el enlace (misma respuesta exista o no el correo) y elegir la contraseña nueva; con la verificación en dos pasos el código es obligatorio.
jest.mock('expo-crypto', () => ({ getRandomBytes: (n: number) => new Uint8Array(n).fill(1) }))
let mockCanGoBack = false
const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: () => mockCanGoBack }
let mockParams: Record<string, string | undefined> = {}
jest.mock('expo-router', () => ({ useRouter: () => mockRouter, useLocalSearchParams: () => mockParams, Link: ({ children }: { children: React.ReactNode }) => require('react').createElement(require('react-native').Text, null, children) }))
jest.mock('../portalApi', () => ({ forgotPassword: jest.fn(), resetPassword: jest.fn() }))
jest.mock('../../kernel/brand/publicBrand', () => ({ loadPublicBrand: jest.fn().mockResolvedValue(undefined), usePublicBrand: () => ({ companyName: 'Acme Co', logos: {} }), pickPublicSlot: () => null }))

import { fireEvent, render, screen } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { forgotPassword, resetPassword } from '../portalApi'
import { ForgotPasswordScreen } from './ForgotPasswordScreen'
import { ResetPasswordScreen } from './ResetPasswordScreen'

const forgot = forgotPassword as jest.Mock
const reset = resetPassword as jest.Mock

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  mockParams = {}
})

describe('ForgotPasswordScreen', () => {
  it('pide el correo, manda el enlace y responde lo mismo exista o no', async () => {
    forgot.mockResolvedValue({ message: 'Si el correo está registrado, le enviamos un enlace para restablecer su contraseña.', link: null })
    await render(<ForgotPasswordScreen />)
    expect(screen.getByText('Acme Co')).toBeTruthy()   // la marca de la compañía arriba
    expect(screen.getByTestId('forgot-submit').props.accessibilityState.disabled).toBe(true)
    await fireEvent.changeText(screen.getByTestId('forgot-email'), '  ana@cliente.co ')
    await fireEvent.press(screen.getByTestId('forgot-submit'))
    expect(forgot).toHaveBeenCalledWith('ana@cliente.co')
    expect(await screen.findByText(/Si el correo está registrado/)).toBeTruthy()
    expect(screen.getByText(/Si usa la verificación en dos pasos, tenga a la mano su código/)).toBeTruthy()
  })

  it('si el servidor falla lo explica y deja volver a intentar', async () => {
    forgot.mockRejectedValue(new ApiError(429, { title: 'Demasiados intentos; espere un minuto e intente de nuevo.' }))
    await render(<ForgotPasswordScreen />)
    await fireEvent.changeText(screen.getByTestId('forgot-email'), 'ana@cliente.co')
    await fireEvent.press(screen.getByTestId('forgot-submit'))
    expect(await screen.findByText('Demasiados intentos seguidos. Espere un minuto e intente de nuevo.')).toBeTruthy()
    expect(screen.getByTestId('forgot-submit')).toBeTruthy()
  })

  it('«Volver» regresa a la pantalla de entrada (sin apilar otra si venía de ella)', async () => {
    await render(<ForgotPasswordScreen />)
    await fireEvent.press(screen.getByTestId('forgot-back'))
    expect(mockRouter.replace).toHaveBeenCalledWith('/login')   // entró directo por un enlace: no hay a dónde volver
    mockCanGoBack = true
    await fireEvent.press(screen.getByTestId('forgot-back'))
    expect(mockRouter.back).toHaveBeenCalledTimes(1)
    mockCanGoBack = false
  })
})

describe('ResetPasswordScreen', () => {
  const type = async (pw: string, again = pw) => {
    await fireEvent.changeText(screen.getByTestId('reset-password'), pw)
    await fireEvent.changeText(screen.getByTestId('reset-confirm'), again)
  }

  it('un enlace sin correo o sin código no es válido', async () => {
    await render(<ResetPasswordScreen />)
    expect(screen.getByText('El enlace no es válido o venció. Pida uno nuevo.')).toBeTruthy()
  })

  it('valida la contraseña antes de ir al servidor', async () => {
    mockParams = { email: 'ana@cliente.co', token: 'tok' }
    await render(<ResetPasswordScreen />)
    await type('corta')
    await fireEvent.press(screen.getByTestId('reset-submit'))
    expect(await screen.findByText(/al menos 12 caracteres/)).toBeTruthy()
    await type('Una-Clave-Larga-1', 'Otra-Clave-Larga-2')
    await fireEvent.press(screen.getByTestId('reset-submit'))
    expect(await screen.findByText('Las dos contraseñas no coinciden.')).toBeTruthy()
    expect(reset).not.toHaveBeenCalled()
  })

  it('cambia la contraseña y manda a iniciar sesión', async () => {
    mockParams = { email: 'ana@cliente.co', token: 'tok' }
    reset.mockResolvedValue(undefined)
    await render(<ResetPasswordScreen />)
    await type('Una-Clave-Larga-1')
    await fireEvent.press(screen.getByTestId('reset-submit'))
    expect(reset).toHaveBeenCalledWith('ana@cliente.co', 'tok', 'Una-Clave-Larga-1', undefined)
    expect(await screen.findByText('Contraseña cambiada')).toBeTruthy()
  })

  it('con la verificación en dos pasos el enlace NO basta: pide el código y lo manda en el mismo intento', async () => {
    mockParams = { email: 'ana@cliente.co', token: 'tok' }
    reset.mockRejectedValueOnce(new ApiError(400, { title: 'Escriba el código de verificación en dos pasos para cambiar la contraseña.', code: 'mfa_required' })).mockResolvedValueOnce(undefined)
    await render(<ResetPasswordScreen />)
    await type('Una-Clave-Larga-1')
    expect(screen.queryByTestId('reset-mfa')).toBeNull()
    await fireEvent.press(screen.getByTestId('reset-submit'))
    expect(await screen.findByTestId('reset-mfa')).toBeTruthy()
    expect(screen.getByText('Escriba el código de verificación en dos pasos para cambiar la contraseña.')).toBeTruthy()
    expect(screen.getByTestId('reset-submit').props.accessibilityState.disabled).toBe(true)   // sin código no se manda
    await fireEvent.changeText(screen.getByTestId('reset-mfa'), ' 123456 ')
    await fireEvent.press(screen.getByTestId('reset-submit'))
    expect(reset).toHaveBeenLastCalledWith('ana@cliente.co', 'tok', 'Una-Clave-Larga-1', '123456')
    expect(await screen.findByText('Contraseña cambiada')).toBeTruthy()
  })

  it('un código malo se explica y el enlace sigue sirviendo', async () => {
    mockParams = { email: 'ana@cliente.co', token: 'tok' }
    reset.mockRejectedValueOnce(new ApiError(400, { title: 'Escriba el código de verificación en dos pasos para cambiar la contraseña.', code: 'mfa_required' }))
      .mockRejectedValueOnce(new ApiError(400, { title: 'Datos inválidos.', code: 'validation', errors: { mfaCode: ['Código MFA inválido.'] } }))
    await render(<ResetPasswordScreen />)
    await type('Una-Clave-Larga-1')
    await fireEvent.press(screen.getByTestId('reset-submit'))
    await fireEvent.changeText(await screen.findByTestId('reset-mfa'), '000000')
    await fireEvent.press(screen.getByTestId('reset-submit'))
    expect(await screen.findByText('Código MFA inválido.')).toBeTruthy()
    expect(screen.getByTestId('reset-mfa')).toBeTruthy()
  })
})
