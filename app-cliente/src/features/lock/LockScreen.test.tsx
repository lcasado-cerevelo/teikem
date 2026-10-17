// Pantalla de bloqueo con huella: pide la huella sola al abrir, deja reintentar, y tras 5 fallos (o con «Usar mi contraseña») cierra la sesión del aparato y vuelve al login.
const mockRouter = { push: jest.fn(), replace: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../../kernel/auth/biometric', () => ({ promptBiometric: jest.fn(), getBiometricSupport: jest.fn() }))
jest.mock('../../kernel/auth/portalAuth', () => ({ logout: jest.fn().mockResolvedValue(undefined) }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { promptBiometric } from '../../kernel/auth/biometric'
import { logout } from '../../kernel/auth/portalAuth'
import { setLang } from '../../kernel/i18n/i18n'
import { LockScreen } from './LockScreen'

const prompt = promptBiometric as jest.Mock
const logoutMock = logout as jest.Mock

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('LockScreen', () => {
  it('pide la huella al abrir y, si es correcta, desbloquea sin tocar nada', async () => {
    prompt.mockResolvedValue(true)
    const onUnlock = jest.fn()
    await render(<LockScreen onUnlock={onUnlock} />)
    await waitFor(() => expect(onUnlock).toHaveBeenCalledTimes(1))
    expect(prompt).toHaveBeenCalledTimes(1)
    expect(logoutMock).not.toHaveBeenCalled()
  })

  it('si la huella falla avisa cuántos intentos quedan y deja reintentar', async () => {
    prompt.mockResolvedValueOnce(false).mockResolvedValueOnce(true)
    const onUnlock = jest.fn()
    await render(<LockScreen onUnlock={onUnlock} />)
    expect(await screen.findByText(/Le quedan 4 intentos/)).toBeTruthy()
    expect(onUnlock).not.toHaveBeenCalled()
    await fireEvent.press(screen.getByRole('button', { name: 'Abrir con huella' }))
    await waitFor(() => expect(onUnlock).toHaveBeenCalledTimes(1))
  })

  it('tras 5 fallos seguidos cierra la sesión y vuelve al login', async () => {
    prompt.mockResolvedValue(false)
    await render(<LockScreen onUnlock={jest.fn()} />)
    for (let i = 0; i < 4; i++) {
      await waitFor(() => expect(screen.getByRole('button', { name: 'Abrir con huella' })).toBeTruthy())
      await fireEvent.press(screen.getByRole('button', { name: 'Abrir con huella' }))
    }
    await waitFor(() => expect(logoutMock).toHaveBeenCalledTimes(1))
    expect(mockRouter.replace).toHaveBeenCalledWith('/login')
    expect(prompt).toHaveBeenCalledTimes(5)
  })

  it('«Usar mi contraseña» cierra la sesión del aparato y va al login', async () => {
    prompt.mockResolvedValue(false)
    await render(<LockScreen onUnlock={jest.fn()} />)
    await fireEvent.press(screen.getByRole('button', { name: 'Usar mi contraseña' }))
    await waitFor(() => expect(mockRouter.replace).toHaveBeenCalledWith('/login'))
    expect(logoutMock).toHaveBeenCalledTimes(1)
  })
})
