// «¿Cómo va mi paquete?» pública: se abre con el enlace de un aviso (token propio de la orden), no recibe datos, solo muestra el avance (sin nombres ni direcciones) y explica los errores.
const mockParams: { token?: string } = { token: 'tok-abc' }
jest.mock('expo-router', () => ({
  useRouter: () => ({ push: jest.fn(), replace: jest.fn() }),
  useLocalSearchParams: () => mockParams,
  Link: ({ children }: { children: string }) => {
    const { Text } = jest.requireActual('react-native')
    return <Text>{children}</Text>
  },
}))
jest.mock('../portalApi', () => ({ trackByLink: jest.fn() }))

import { render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { trackByLink } from '../portalApi'
import { TrackScreen } from './TrackScreen'

const track = trackByLink as jest.Mock

const RESULT = {
  waybill: 'CND-0000415',
  companyName: 'Advance Logistics',
  step: 3,
  total: 4,
  state: 'IN_PROGRESS',
  statusLabel: 'En ruta',
  steps: [
    { key: 'SUBMITTED', label: 'Sometida', done: true, current: false },
    { key: 'RECEIVED', label: 'Recibida', done: true, current: false },
    { key: 'ON_ROUTE', label: 'En ruta', done: false, current: true },
    { key: 'DONE', label: 'Completada', done: false, current: false },
  ],
  lastUpdateUtc: '2026-10-17T15:00:00Z',
}

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  mockParams.token = 'tok-abc'
})

describe('TrackScreen (por enlace)', () => {
  it('consulta con el token del enlace, sin pedir ningún dato, y muestra la barra con las etapas', async () => {
    track.mockResolvedValue(RESULT)
    await render(<TrackScreen />)
    expect(await screen.findByTestId('track-result-waybill')).toHaveTextContent('CND-0000415')
    expect(track).toHaveBeenCalledWith('tok-abc', 'es')
    expect(screen.queryByLabelText('Número de conduce')).toBeNull()               // ya no hay búsqueda manual
    expect(screen.getByText('Advance Logistics')).toBeTruthy()
    expect(screen.getByText('Paso 3 de 4 · En ruta')).toBeTruthy()
    expect(screen.getAllByTestId('segment-done')).toHaveLength(2)
    expect(screen.getByText(/Última actualización/)).toBeTruthy()
    expect(screen.getByText(/nunca nombres, direcciones ni teléfonos/)).toBeTruthy()
  })

  it('un enlace que no existe muestra el mensaje del servidor', async () => {
    track.mockRejectedValue(new ApiError(404, { title: 'No encontramos ese enlace de seguimiento.' }))
    await render(<TrackScreen />)
    expect(await screen.findByText('No encontramos ese enlace de seguimiento.')).toBeTruthy()
    expect(screen.queryByTestId('track-result-waybill')).toBeNull()
  })

  it('demasiadas consultas (429) se explica', async () => {
    track.mockRejectedValue(new ApiError(429, { title: 'Demasiados intentos; espere un minuto e intente de nuevo.' }))
    await render(<TrackScreen />)
    expect(await screen.findByText('Demasiadas consultas seguidas. Espere un minuto e intente de nuevo.')).toBeTruthy()
  })

  it('una orden entregada se ve como tal', async () => {
    track.mockResolvedValue({ ...RESULT, step: 4, state: 'COMPLETED', statusLabel: 'Entregada' })
    await render(<TrackScreen />)
    expect((await screen.findAllByText('Entregada')).length).toBeGreaterThan(0)
    await waitFor(() => expect(screen.getAllByTestId('segment-done')).toHaveLength(4))
  })
})
