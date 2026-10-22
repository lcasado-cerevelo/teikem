// Pantalla de la invitación: sin enlace válido avisa, valida la contraseña antes de ir al servidor y, al guardar, manda correo + token + contraseña y ofrece ir a entrar.
const mockParams: { email?: string; token?: string } = {}
jest.mock('expo-router', () => ({
  useRouter: () => ({ push: jest.fn(), replace: jest.fn() }),
  useLocalSearchParams: () => mockParams,
  Link: ({ children }: { children: string }) => {
    const { Text } = require('react-native')
    return <Text>{children}</Text>
  },
}))
jest.mock('../portalApi', () => ({ acceptInvite: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { acceptInvite } from '../portalApi'
import { AcceptInviteScreen } from './AcceptInviteScreen'

const acceptMock = acceptInvite as jest.Mock

async function fill(password: string, confirm: string) {
  await fireEvent.changeText(screen.getByLabelText('Contraseña nueva'), password)
  await fireEvent.changeText(screen.getByLabelText('Repita la contraseña'), confirm)
  await fireEvent.press(screen.getByRole('button', { name: 'Guardar mi contraseña' }))
}

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  mockParams.email = 'ana@cliente.co'
  mockParams.token = 'tok/en=='
})

describe('AcceptInviteScreen', () => {
  it('sin correo o sin token en el enlace avisa que no es válido y no pide contraseña', async () => {
    mockParams.token = undefined
    await render(<AcceptInviteScreen />)
    expect(screen.getByText('El enlace no es válido. Pida que le envíen la invitación otra vez.')).toBeTruthy()
    expect(screen.queryByLabelText('Contraseña nueva')).toBeNull()
  })

  it('una contraseña corta o que no coincide no llega al servidor', async () => {
    await render(<AcceptInviteScreen />)
    await fill('corta', 'corta')
    expect(screen.getByText('La contraseña debe tener al menos 12 caracteres.')).toBeTruthy()
    await fill('una-clave-larga-123', 'otra-clave-larga-123')
    expect(screen.getByText('Las dos contraseñas no coinciden.')).toBeTruthy()
    expect(acceptMock).not.toHaveBeenCalled()
  })

  it('al guardar manda el correo, el token y la contraseña, y ofrece ir a entrar', async () => {
    acceptMock.mockResolvedValue(undefined)
    await render(<AcceptInviteScreen />)
    await fill('una-clave-larga-123', 'una-clave-larga-123')
    await waitFor(() => expect(acceptMock).toHaveBeenCalledWith('ana@cliente.co', 'tok/en==', 'una-clave-larga-123'))
    expect(await screen.findByText('Listo: su contraseña quedó guardada.')).toBeTruthy()
    expect(screen.getByText('Ir a la pantalla de entrada')).toBeTruthy()
  })

  it('si el servidor rechaza el enlace muestra su mensaje y deja intentar otra vez', async () => {
    acceptMock.mockRejectedValue(new ApiError(400, { title: 'Invitación inválida o vencida.' }))
    await render(<AcceptInviteScreen />)
    await fill('una-clave-larga-123', 'una-clave-larga-123')
    expect(await screen.findByText('Invitación inválida o vencida.')).toBeTruthy()
    expect(screen.getByLabelText('Contraseña nueva')).toBeTruthy()
  })
})
