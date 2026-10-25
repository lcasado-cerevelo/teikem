// Pantalla de login: la casilla «No confiar en esta computadora» se recuerda, y cada resultado del servidor lleva a su pantalla.
jest.mock('expo-crypto', () => ({ getRandomBytes: (n: number) => new Uint8Array(n).fill(1) }))
const mockRouter = { push: jest.fn(), replace: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../../kernel/auth/portalAuth', () => ({ login: jest.fn() }))
jest.mock('../portalApi', () => ({ getAuthConfig: jest.fn() }))
jest.mock('../../kernel/brand/publicBrand', () => ({ loadPublicBrand: jest.fn().mockResolvedValue(undefined), usePublicBrand: () => ({ companyName: null, logos: {} }), pickPublicSlot: () => null }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { login } from '../../kernel/auth/portalAuth'
import { getNoTrust, setNoTrust } from '../../kernel/auth/preferences'
import { setLang } from '../../kernel/i18n/i18n'
import { getAuthConfig } from '../portalApi'
import { LoginScreen } from './LoginScreen'

const loginMock = login as jest.Mock
const configMock = getAuthConfig as jest.Mock

async function fillAndSubmit() {
  await fireEvent.changeText(screen.getByLabelText('Correo electrónico'), 'ana@cliente.co')
  await fireEvent.changeText(screen.getByLabelText('Contraseña'), 'Clave-1234')
  await fireEvent.press(screen.getByRole('button', { name: 'Entrar' }))
}

const noTrustBox = () => screen.getByRole('checkbox', { name: 'No confiar en esta computadora' })

beforeEach(async () => {
  jest.clearAllMocks()
  setLang('es')
  await setNoTrust(false)
})

describe('LoginScreen', () => {
  it('la casilla «No confiar» sale como se dejó la última vez y se recuerda al cambiarla', async () => {
    await setNoTrust(true)
    await render(<LoginScreen />)
    await waitFor(() => expect(noTrustBox().props.accessibilityState.checked).toBe(true))
    await fireEvent.press(noTrustBox())
    await waitFor(() => expect(noTrustBox().props.accessibilityState.checked).toBe(false))
    expect(await getNoTrust()).toBe(false)
  })

  it('con credenciales entra al inicio', async () => {
    loginMock.mockResolvedValue({ status: 'ok' })
    await render(<LoginScreen />)
    await fillAndSubmit()
    await waitFor(() => expect(mockRouter.replace).toHaveBeenCalledWith('/home'))
    expect(loginMock).toHaveBeenCalledWith('ana@cliente.co', 'Clave-1234', undefined)
  })

  it('si pide el segundo paso va a /mfa', async () => {
    loginMock.mockResolvedValue({ status: 'mfa_required', enrollmentRequired: false })
    await render(<LoginScreen />)
    await fillAndSubmit()
    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith('/mfa'))
  })

  it('computadora nueva: muestra «Confirme esta computadora» con el correo enmascarado y se puede volver', async () => {
    loginMock.mockResolvedValue({ status: 'browser_confirmation_required', browser: { email: 'a***@cliente.co', sent: true } })
    await render(<LoginScreen />)
    await fillAndSubmit()
    expect(await screen.findByText('Confirme esta computadora')).toBeTruthy()
    expect(screen.getByText('Le enviamos un correo a a***@cliente.co.')).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Volver a la pantalla de entrada' }))
    expect(await screen.findByLabelText('Correo electrónico')).toBeTruthy()
  })

  it('computadora nueva y el correo no salió: lo dice', async () => {
    loginMock.mockResolvedValue({ status: 'browser_confirmation_required', browser: { email: 'a***@cliente.co', sent: false, message: 'No se pudo enviar el correo de confirmación.' } })
    await render(<LoginScreen />)
    await fillAndSubmit()
    expect(await screen.findByText('No se pudo enviar el correo de confirmación.')).toBeTruthy()
  })

  it('un error del servidor se muestra sobre el formulario', async () => {
    loginMock.mockRejectedValue(new ApiError(401, { title: 'Credenciales inválidas.' }))
    await render(<LoginScreen />)
    await fillAndSubmit()
    // el servidor responde lo mismo ante cualquier credencial mala; la pantalla le dice a la persona qué revisar (Lote 219)
    expect(await screen.findByText(/no son correctos.*mayúsculas.*ojito/)).toBeTruthy()
    expect(mockRouter.replace).not.toHaveBeenCalled()
  })

  it('demasiados intentos, servidor caído y sin red tienen su propio mensaje', async () => {
    loginMock.mockRejectedValueOnce(new ApiError(429, { title: 'Demasiados intentos; espere un minuto e intente de nuevo.' }))
    await render(<LoginScreen />)
    await fillAndSubmit()
    expect(await screen.findByText('Demasiados intentos seguidos. Espere un minuto e intente de nuevo.')).toBeTruthy()
    loginMock.mockRejectedValueOnce(new ApiError(500, { title: 'Error interno.' }))
    await fillAndSubmit()
    expect(await screen.findByText(/El servidor tuvo un problema/)).toBeTruthy()
    loginMock.mockRejectedValueOnce(new ApiError(0, null))
    await fillAndSubmit()
    expect(await screen.findByText('No hay conexión con el servidor. Revise su internet e intente de nuevo.')).toBeTruthy()
  })

  it('«¿Olvidó su contraseña?» lleva a pedir el enlace; la contraseña se puede ver con el ojito', async () => {
    await render(<LoginScreen />)
    await fireEvent.press(screen.getByTestId('login-forgot'))
    expect(mockRouter.push).toHaveBeenCalledWith('/forgot-password')
    expect(screen.getByTestId('login-password').props.secureTextEntry).toBe(true)
    await fireEvent.press(screen.getByTestId('login-password-eye'))
    expect(screen.getByTestId('login-password').props.secureTextEntry).toBe(false)
    await fireEvent.press(screen.getByTestId('login-password-eye'))
    expect(screen.getByTestId('login-password').props.secureTextEntry).toBe(true)
  })

  it('el reto anti-robot: el servidor lo pide y se muestra; en el teléfono se pide entrar desde el navegador', async () => {
    loginMock.mockResolvedValue({ status: 'captcha_required' })
    configMock.mockResolvedValue({ captchaEnabled: true, captchaSiteKey: 'site-key', captchaAfterFailures: 3 })
    await render(<LoginScreen />)
    await fillAndSubmit()
    expect(await screen.findByText('Confirme que no es un robot para continuar.')).toBeTruthy()
    expect(screen.getByText(/entre al portal desde un navegador web/)).toBeTruthy()
  })
})
