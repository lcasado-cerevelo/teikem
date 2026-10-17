// «¿Cómo va mi orden?» pública: consulta por conduce con el identificador de la compañía en el enlace; solo muestra el avance (sin nombres ni direcciones) y explica los errores.
const mockParams: { company?: string; n?: string } = { company: 'advance' }
jest.mock('expo-router', () => ({
  useLocalSearchParams: () => mockParams,
  Link: ({ children }: { children: string }) => {
    const { Text } = jest.requireActual('react-native')
    return <Text>{children}</Text>
  },
}))
jest.mock('../portalApi', () => ({ trackOrder: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { trackOrder } from '../portalApi'
import { TrackScreen } from './TrackScreen'

const track = trackOrder as jest.Mock

const RESULT = {
  waybill: 'CND-0000415',
  companyName: 'Advance Logistics',
  step: 3,
  total: 5,
  state: 'IN_PROGRESS',
  statusLabel: 'Recogido',
  steps: [
    { key: 'DRAFT', label: 'Borrador', done: true, current: false },
    { key: 'CONFIRMED', label: 'Confirmada', done: true, current: false },
    { key: 'PICKUP', label: 'Recogido', done: false, current: true },
    { key: 'IN_TRANSIT', label: 'En tránsito', done: false, current: false },
    { key: 'DELIVERED', label: 'Entregada', done: false, current: false },
  ],
  lastUpdateUtc: '2026-10-17T15:00:00Z',
}

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  delete mockParams.n
  mockParams.company = 'advance'
})

describe('TrackScreen', () => {
  it('consulta con el identificador del enlace y muestra la barra con las etapas de la compañía', async () => {
    track.mockResolvedValue(RESULT)
    await render(<TrackScreen />)
    await fireEvent.changeText(screen.getByLabelText('Número de conduce'), 'cnd-0000415')
    await fireEvent.press(screen.getByRole('button', { name: 'Consultar' }))
    expect(await screen.findByTestId('track-result-waybill')).toHaveTextContent('CND-0000415')
    expect(track).toHaveBeenCalledWith('advance', 'cnd-0000415', 'es')
    expect(screen.getByText('Advance Logistics')).toBeTruthy()
    expect(screen.getByText('Paso 3 de 5 · Recogido')).toBeTruthy()
    expect(screen.getAllByTestId('segment-done')).toHaveLength(2)
    expect(screen.getByText('En tránsito')).toBeTruthy()
    expect(screen.getByText(/Última actualización/)).toBeTruthy()
    expect(screen.getByText(/nunca nombres, direcciones ni teléfonos/)).toBeTruthy()
  })

  it('el botón no se habilita con menos de 3 caracteres', async () => {
    await render(<TrackScreen />)
    expect(screen.getByRole('button', { name: 'Consultar' })).toBeDisabled()
    await fireEvent.changeText(screen.getByLabelText('Número de conduce'), 'CN')
    expect(screen.getByRole('button', { name: 'Consultar' })).toBeDisabled()
    expect(track).not.toHaveBeenCalled()
  })

  it('con ?n=… abre ya con la consulta hecha', async () => {
    mockParams.n = 'CND-0000415'
    track.mockResolvedValue(RESULT)
    await render(<TrackScreen />)
    await waitFor(() => expect(track).toHaveBeenCalledWith('advance', 'CND-0000415', 'es'))
    expect(await screen.findByText('Paso 3 de 5 · Recogido')).toBeTruthy()
  })

  it('un número que no existe muestra el mensaje del servidor y no deja el resultado anterior', async () => {
    track.mockResolvedValueOnce(RESULT).mockRejectedValueOnce(new ApiError(404, { title: 'No encontramos ese número de conduce.' }))
    await render(<TrackScreen />)
    await fireEvent.changeText(screen.getByLabelText('Número de conduce'), 'CND-0000415')
    await fireEvent.press(screen.getByRole('button', { name: 'Consultar' }))
    await screen.findByTestId('track-result-waybill')
    await fireEvent.changeText(screen.getByLabelText('Número de conduce'), 'CND-9999999')
    await fireEvent.press(screen.getByRole('button', { name: 'Consultar' }))
    expect(await screen.findByText('No encontramos ese número de conduce.')).toBeTruthy()
    expect(screen.queryByTestId('track-result-waybill')).toBeNull()
  })

  it('demasiadas consultas (429) se explica', async () => {
    track.mockRejectedValue(new ApiError(429, { title: 'Demasiados intentos; espere un minuto e intente de nuevo.' }))
    await render(<TrackScreen />)
    await fireEvent.changeText(screen.getByLabelText('Número de conduce'), 'CND-0000415')
    await fireEvent.press(screen.getByRole('button', { name: 'Consultar' }))
    expect(await screen.findByText('Demasiadas consultas seguidas. Espere un minuto e intente de nuevo.')).toBeTruthy()
  })

  it('una orden entregada o cancelada se ve como tal', async () => {
    track.mockResolvedValue({ ...RESULT, step: 5, state: 'COMPLETED', statusLabel: 'Entregada' })
    await render(<TrackScreen />)
    await fireEvent.changeText(screen.getByLabelText('Número de conduce'), 'CND-0000415')
    await fireEvent.press(screen.getByRole('button', { name: 'Consultar' }))
    expect((await screen.findAllByText('Entregada')).length).toBeGreaterThan(0)
    expect(screen.getAllByTestId('segment-done')).toHaveLength(5)
  })
})
