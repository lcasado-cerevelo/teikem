// Acceso del consignatario (Lote 117, lado del cliente): quién tiene acceso, invitar a un correo (o al principal del consignatario), reenviar y quitar; quien solo consulta solo ve.
jest.mock('../portalApi', () => ({
  listConsigneeAccess: jest.fn(),
  inviteConsigneeAccess: jest.fn(),
  resendConsigneeAccess: jest.fn(),
  removeConsigneeAccess: jest.fn(),
}))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { inviteConsigneeAccess, listConsigneeAccess, removeConsigneeAccess, resendConsigneeAccess } from '../portalApi'
import { ConsigneeAccessPanel } from './ConsigneeAccessPanel'

const list = listConsigneeAccess as jest.Mock
const invite = inviteConsigneeAccess as jest.Mock
const resend = resendConsigneeAccess as jest.Mock
const remove = removeConsigneeAccess as jest.Mock

const ACCESS = [
  { id: 4, email: 'tienda@norte.co', fullName: 'Doña Tienda', status: 'ACTIVE', statusLabel: 'Activo', lastLoginUtc: '2026-10-18T12:00:00Z' },
  { id: 5, email: 'nuevo@norte.co', fullName: null, status: 'INVITED', statusLabel: 'Invitado', lastLoginUtc: null },
]

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  list.mockResolvedValue(ACCESS)
})

describe('ConsigneeAccessPanel', () => {
  it('lista a quien tiene acceso con su estado', async () => {
    await render(<ConsigneeAccessPanel locationId="loc-1" canEdit />)
    expect(await screen.findByText('Doña Tienda · tienda@norte.co')).toBeTruthy()
    expect(screen.getByText('Con acceso')).toBeTruthy()
    expect(screen.getByText('Invitación pendiente')).toBeTruthy()
    expect(list).toHaveBeenCalledWith('loc-1')
  })

  it('sin nadie lo dice', async () => {
    list.mockResolvedValue([])
    await render(<ConsigneeAccessPanel locationId="loc-1" canEdit />)
    expect(await screen.findByText('Nadie tiene acceso todavía.')).toBeTruthy()
  })

  it('invitar manda el correo escrito (o vacío para usar el del consignatario) y avisa a quién se invitó', async () => {
    invite.mockResolvedValue({ access: { id: 6, email: 'otra@norte.co' } })
    await render(<ConsigneeAccessPanel locationId="loc-1" canEdit defaultEmail="tienda@norte.co" />)
    await fireEvent.changeText(await screen.findByTestId('access-email'), '  otra@norte.co ')
    await fireEvent.changeText(screen.getByTestId('access-name'), 'Otra Persona')
    await fireEvent.press(screen.getByTestId('access-invite'))
    await waitFor(() => expect(invite).toHaveBeenCalledWith('loc-1', 'otra@norte.co', 'Otra Persona'))
    expect(await screen.findByText('Invitación enviada a otra@norte.co.')).toBeTruthy()

    await fireEvent.press(screen.getByTestId('access-invite'))
    await waitFor(() => expect(invite).toHaveBeenLastCalledWith('loc-1', '', ''))
  })

  it('reenviar solo se ofrece al invitado que no aceptó, y quitar a cualquiera', async () => {
    resend.mockResolvedValue({})
    remove.mockResolvedValue(undefined)
    await render(<ConsigneeAccessPanel locationId="loc-1" canEdit />)
    await screen.findByText('Doña Tienda · tienda@norte.co')
    expect(screen.queryByTestId('access-resend-4')).toBeNull()
    await fireEvent.press(screen.getByTestId('access-resend-5'))
    await waitFor(() => expect(resend).toHaveBeenCalledWith('loc-1', 5))
    expect(await screen.findByText('Invitación reenviada.')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('access-remove-4'))
    await waitFor(() => expect(remove).toHaveBeenCalledWith('loc-1', 4))
    expect(await screen.findByText('Acceso quitado.')).toBeTruthy()
  })

  it('un error del servidor se muestra', async () => {
    invite.mockRejectedValue(new ApiError(409, { title: 'Ese correo no está disponible para el portal de esta compañía.' }))
    await render(<ConsigneeAccessPanel locationId="loc-1" canEdit />)
    await fireEvent.changeText(await screen.findByTestId('access-email'), 'x@y.co')
    await fireEvent.press(screen.getByTestId('access-invite'))
    expect(await screen.findByText('Ese correo no está disponible para el portal de esta compañía.')).toBeTruthy()
  })

  it('quien solo consulta ve la lista sin poder invitar, reenviar ni quitar', async () => {
    await render(<ConsigneeAccessPanel locationId="loc-1" canEdit={false} />)
    expect(await screen.findByText('Doña Tienda · tienda@norte.co')).toBeTruthy()
    expect(screen.queryByTestId('access-invite')).toBeNull()
    expect(screen.queryByTestId('access-resend-5')).toBeNull()
    expect(screen.queryByTestId('access-remove-4')).toBeNull()
  })
})
