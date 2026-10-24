// Indicadores del Inicio (Lote 120): los números del período y el cambio de período; lo que no aplica (sin fecha prometida, sin facturación o COD) no se muestra.
jest.mock('../portalApi', () => ({ getIndicators: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { getIndicators } from '../portalApi'
import { IndicatorsCard } from './IndicatorsCard'

const get = getIndicators as jest.Mock

const DATA = (over: object = {}) => ({ days: 30, created: 12, delivered: 9, deliveredWithPromise: 8, onTime: 6, onTimePct: 75, avgDeliveryHours: 26.5, openNow: 4, inTransitNow: 2, failedAttempts: 1, invoiceBalance: 80, overdueInvoices: 1, codToRemit: 45, ...over })

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('IndicatorsCard', () => {
  it('muestra los indicadores de 30 días por omisión, con facturas y COD si aplican', async () => {
    get.mockResolvedValue(DATA())
    await render(<IndicatorsCard />)
    expect(await screen.findByTestId('ind-created')).toHaveTextContent('12')
    expect(screen.getByTestId('ind-delivered')).toHaveTextContent('9')
    expect(screen.getByTestId('ind-ontime')).toHaveTextContent('75% (6/8)')
    expect(screen.getByTestId('ind-hours')).toHaveTextContent('26.5 h')
    expect(screen.getByTestId('ind-failed')).toHaveTextContent('1')
    expect(screen.getByTestId('ind-open')).toHaveTextContent('4 · 2 en tránsito')
    expect(screen.getByTestId('ind-invoices')).toHaveTextContent('$80.00 · 1 vencida(s)')
    expect(screen.getByTestId('ind-cod')).toHaveTextContent('$45.00')
    expect(get).toHaveBeenCalledWith(30)
  })

  it('cambiar el período vuelve a pedir; sin fecha prometida, sin facturación y sin COD esas filas no salen', async () => {
    get.mockResolvedValue(DATA({ days: 7, onTimePct: null, avgDeliveryHours: null, invoiceBalance: null, overdueInvoices: null, codToRemit: null }))
    await render(<IndicatorsCard />)
    await screen.findByTestId('ind-created')
    await fireEvent.press(screen.getByTestId('indicators-days-7'))
    await waitFor(() => expect(get).toHaveBeenLastCalledWith(7))
    // «Hoy» pide 0 días
    expect(screen.getByText('Hoy')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('indicators-days-0'))
    await waitFor(() => expect(get).toHaveBeenLastCalledWith(0))
    expect(screen.queryByTestId('ind-ontime')).toBeNull()
    expect(screen.queryByTestId('ind-hours')).toBeNull()
    expect(screen.queryByTestId('ind-invoices')).toBeNull()
    expect(screen.queryByTestId('ind-cod')).toBeNull()
  })
})
