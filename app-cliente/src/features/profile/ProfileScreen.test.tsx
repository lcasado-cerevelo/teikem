// Mi perfil (Lote 120): mi nombre (lo único que cambio), mi correo y rol en solo lectura, los datos de mi empresa en solo lectura y el cambio de contraseña con sus validaciones.
jest.mock('../portalApi', () => ({ getProfile: jest.fn(), saveProfile: jest.fn(), changePassword: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { changePassword, getProfile, saveProfile } from '../portalApi'
import { ProfileScreen } from './ProfileScreen'

const get = getProfile as jest.Mock
const save = saveProfile as jest.Mock
const change = changePassword as jest.Mock

const PROFILE = {
  fullName: 'Ana Admin', email: 'ana@cliente.co', roleCode: 'CLIENT_ADMIN', roleLabel: 'Administrador del cliente',
  company: { name: 'Cliente Uno', code: 'CLIENTE-1', legalName: 'Cliente Uno LLC', taxId: '66-1234567', line1: 'Calle 5 #12', line2: null, city: 'Ponce', state: 'PR', postalCode: '00716' },
}

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  get.mockResolvedValue(PROFILE)
})

describe('ProfileScreen', () => {
  it('muestra mis datos, mi empresa en solo lectura y avisa quién cambia qué', async () => {
    await render(<ProfileScreen />)
    expect(await screen.findByText('Cliente Uno LLC', { exact: false })).toBeTruthy()
    expect(screen.getByText('Correo: ana@cliente.co')).toBeTruthy()
    expect(screen.getByText('Rol: Administrador del cliente')).toBeTruthy()
    expect(screen.getByText('Calle 5 #12, Ponce, PR, 00716')).toBeTruthy()
    expect(screen.getByText(/Estos datos los cambia la compañía/)).toBeTruthy()
    expect(screen.getByTestId('profile-save')).toBeDisabled()                 // sin cambios no se guarda
  })

  it('cambiar mi nombre manda solo el nombre y lo confirma', async () => {
    save.mockResolvedValue({ ...PROFILE, fullName: 'Ana M. Admin' })
    await render(<ProfileScreen />)
    await fireEvent.changeText(await screen.findByTestId('profile-name'), 'Ana M. Admin')
    await fireEvent.press(screen.getByTestId('profile-save'))
    await waitFor(() => expect(save).toHaveBeenCalledWith('Ana M. Admin'))
    expect(await screen.findByText('Su nombre quedó guardado.')).toBeTruthy()
  })

  it('la contraseña exige las tres cajas, que la nueva coincida y muestra el mensaje del servidor', async () => {
    await render(<ProfileScreen />)
    await screen.findByTestId('profile-pw-save')
    await fireEvent.press(screen.getByTestId('profile-pw-save'))
    expect(await screen.findByText('Escriba su contraseña actual y la nueva.')).toBeTruthy()
    await fireEvent.changeText(screen.getByTestId('profile-pw-current'), 'Actual-2026-xyz')
    await fireEvent.changeText(screen.getByTestId('profile-pw-new'), 'Nueva-clave-2026-A')
    await fireEvent.changeText(screen.getByTestId('profile-pw-again'), 'otra-distinta')
    await fireEvent.press(screen.getByTestId('profile-pw-save'))
    expect(await screen.findByText('Las contraseñas nuevas no coinciden.')).toBeTruthy()
    expect(change).not.toHaveBeenCalled()

    change.mockRejectedValueOnce(new ApiError(400, { title: 'Esta contraseña aparece en brechas conocidas; elija otra.' }))
    await fireEvent.changeText(screen.getByTestId('profile-pw-again'), 'Nueva-clave-2026-A')
    await fireEvent.press(screen.getByTestId('profile-pw-save'))
    expect(await screen.findByText('Esta contraseña aparece en brechas conocidas; elija otra.')).toBeTruthy()

    change.mockResolvedValueOnce(undefined)
    await fireEvent.press(screen.getByTestId('profile-pw-save'))
    await waitFor(() => expect(change).toHaveBeenLastCalledWith('Actual-2026-xyz', 'Nueva-clave-2026-A'))
    expect(await screen.findByText('Su contraseña se cambió.')).toBeTruthy()
  })
})
