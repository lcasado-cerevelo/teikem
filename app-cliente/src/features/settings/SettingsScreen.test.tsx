// Ajustes: «Abrir con huella» solo se ofrece con lector y una huella registrada y pide identificarse una vez al activarla; mis computadoras de confianza se listan y se quitan; cambiar de cliente solo con varios.
jest.mock('expo-crypto', () => ({ getRandomBytes: (n: number) => new Uint8Array(n).fill(1) }))
const mockRouter = { push: jest.fn(), replace: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../../kernel/auth/biometric', () => ({ promptBiometric: jest.fn(), getBiometricSupport: jest.fn() }))
jest.mock('../../kernel/auth/portalAuth', () => ({ logout: jest.fn().mockResolvedValue(undefined), switchClient: jest.fn().mockResolvedValue(undefined) }))
jest.mock('../portalApi', () => ({ getMe: jest.fn(), listTrustedBrowsers: jest.fn(), revokeTrustedBrowser: jest.fn().mockResolvedValue(undefined) }))
const mockLock = { enabled: false, locked: false, setEnabled: jest.fn().mockResolvedValue(undefined), unlock: jest.fn() }
jest.mock('../../kernel/auth/AppLock', () => ({ useAppLock: () => mockLock }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { getBiometricSupport, promptBiometric } from '../../kernel/auth/biometric'
import { logout, switchClient } from '../../kernel/auth/portalAuth'
import { setLang } from '../../kernel/i18n/i18n'
import { getMe, listTrustedBrowsers, revokeTrustedBrowser } from '../portalApi'
import { SettingsScreen } from './SettingsScreen'

const support = getBiometricSupport as jest.Mock
const prompt = promptBiometric as jest.Mock
const me = getMe as jest.Mock
const computers = listTrustedBrowsers as jest.Mock

const ME = {
  fullName: 'Ana Cliente',
  email: 'ana@cliente.co',
  mfaEnabled: true,
  currentClient: { clientPublicId: 'c1', name: 'Cliente Uno', tenantName: 'Compañía', isCurrent: true },
  clients: [{ clientPublicId: 'c1', name: 'Cliente Uno', tenantName: 'Compañía', isCurrent: true }],
}
const COMPUTERS = [
  { publicId: 'b1', label: 'Chrome en Windows', expiresAtUtc: '2026-11-16T10:00:00Z', isCurrent: true },
  { publicId: 'b2', label: 'Safari en iPhone', expiresAtUtc: '2026-11-09T10:00:00Z', isCurrent: false },
]

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  mockLock.enabled = false
  support.mockResolvedValue('ready')
  me.mockResolvedValue(ME)
  computers.mockResolvedValue(COMPUTERS)
})

describe('SettingsScreen', () => {
  it('activar «Abrir con huella» pide identificarse una vez y solo entonces lo guarda', async () => {
    prompt.mockResolvedValue(true)
    await render(<SettingsScreen />)
    await fireEvent.press(await screen.findByRole('checkbox', { name: 'Abrir con huella' }))
    await waitFor(() => expect(mockLock.setEnabled).toHaveBeenCalledWith(true))
    expect(prompt).toHaveBeenCalledTimes(1)
  })

  it('si no se identifica, no se activa', async () => {
    prompt.mockResolvedValue(false)
    await render(<SettingsScreen />)
    await fireEvent.press(await screen.findByRole('checkbox', { name: 'Abrir con huella' }))
    await waitFor(() => expect(prompt).toHaveBeenCalled())
    expect(mockLock.setEnabled).not.toHaveBeenCalled()
  })

  it('apagarla no pide la huella', async () => {
    mockLock.enabled = true
    await render(<SettingsScreen />)
    await fireEvent.press(await screen.findByRole('checkbox', { name: 'Abrir con huella' }))
    await waitFor(() => expect(mockLock.setEnabled).toHaveBeenCalledWith(false))
    expect(prompt).not.toHaveBeenCalled()
  })

  it('sin huella registrada, explica que la registre en el teléfono y no ofrece la opción', async () => {
    support.mockResolvedValue('not_enrolled')
    await render(<SettingsScreen />)
    expect(await screen.findByText(/no tiene una huella registrada/)).toBeTruthy()
    expect(screen.queryByRole('checkbox', { name: 'Abrir con huella' })).toBeNull()
  })

  it('sin lector (o en la web), dice que no está disponible', async () => {
    support.mockResolvedValue('unavailable')
    await render(<SettingsScreen />)
    expect(await screen.findByText(/no está disponible en este aparato/)).toBeTruthy()
  })

  it('lista mis computadoras de confianza, marca la actual y quita una', async () => {
    await render(<SettingsScreen />)
    expect(await screen.findByText('Safari en iPhone')).toBeTruthy()
    expect(screen.getByText('Esta computadora')).toBeTruthy()
    await fireEvent.press(screen.getAllByRole('button', { name: 'Quitar' })[1])
    await waitFor(() => expect(revokeTrustedBrowser).toHaveBeenCalledWith('b2'))
  })

  it('cambiar de cliente solo aparece con acceso a varios y cambia a otro', async () => {
    await render(<SettingsScreen />)
    await screen.findByText('Ana Cliente')
    expect(screen.queryByText('Cambiar de cliente')).toBeNull()
  })

  it('con varios clientes ofrece cambiar', async () => {
    me.mockResolvedValue({ ...ME, clients: [...ME.clients, { clientPublicId: 'c2', name: 'Cliente Dos', tenantName: 'Compañía', isCurrent: false }] })
    await render(<SettingsScreen />)
    expect(await screen.findByText('Cambiar de cliente')).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Cliente Dos' }))
    await waitFor(() => expect(switchClient).toHaveBeenCalledWith('c2'))
    expect(mockRouter.replace).toHaveBeenCalledWith('/home')
  })

  it('cerrar sesión borra la sesión y vuelve al login', async () => {
    await render(<SettingsScreen />)
    await fireEvent.press(await screen.findByRole('button', { name: 'Cerrar sesión' }))
    await waitFor(() => expect(mockRouter.replace).toHaveBeenCalledWith('/login'))
    expect(logout).toHaveBeenCalledTimes(1)
  })

  it('Lote 116: «Configurar avisos» lleva a la pantalla de avisos a los consignatarios', async () => {
    await render(<SettingsScreen />)
    await fireEvent.press(await screen.findByTestId('settings-consignee-notices'))
    expect(mockRouter.push).toHaveBeenCalledWith('/consignee-notices')
  })
})
