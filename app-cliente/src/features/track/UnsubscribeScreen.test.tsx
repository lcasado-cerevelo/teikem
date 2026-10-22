// Baja de los avisos (pública): abrir el enlace no da de baja; hay que confirmar con el botón; se explica el resultado y los errores.
const mockParams: { token?: string } = { token: 'baja-123' }
jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn(), replace: jest.fn() }), useLocalSearchParams: () => mockParams }))
jest.mock('../portalApi', () => ({ unsubscribeConsignee: jest.fn() }))

import { fireEvent, render, screen } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { unsubscribeConsignee } from '../portalApi'
import { UnsubscribeScreen } from './UnsubscribeScreen'

const unsub = unsubscribeConsignee as jest.Mock

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('UnsubscribeScreen', () => {
  it('abrir la página no da de baja: espera la confirmación', async () => {
    await render(<UnsubscribeScreen />)
    expect(screen.getByText('Darse de baja de los avisos')).toBeTruthy()
    expect(unsub).not.toHaveBeenCalled()
  })

  it('confirmar manda el token del enlace y avisa que quedó dado de baja', async () => {
    unsub.mockResolvedValue(undefined)
    await render(<UnsubscribeScreen />)
    await fireEvent.press(screen.getByTestId('unsubscribe-confirm'))
    expect(await screen.findByText('Listo: no recibirá más avisos por este medio.')).toBeTruthy()
    expect(unsub).toHaveBeenCalledWith('baja-123')
  })

  it('un enlace que no sirve muestra el mensaje del servidor', async () => {
    unsub.mockRejectedValue(new ApiError(404, { title: 'El enlace de baja no es válido.' }))
    await render(<UnsubscribeScreen />)
    await fireEvent.press(screen.getByTestId('unsubscribe-confirm'))
    expect(await screen.findByText('El enlace de baja no es válido.')).toBeTruthy()
  })
})
