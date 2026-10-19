// Mis avisos (Lote 119): la compañía manda (lo que no encendió se ve «no disponible» y no se puede encender) y la persona apaga lo que no quiere; se guarda al tocar.
jest.mock('../portalApi', () => ({ getNoticePrefs: jest.fn(), saveNoticePrefs: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { getNoticePrefs, saveNoticePrefs } from '../portalApi'
import { NoticePrefsScreen } from './NoticePrefsScreen'

const get = getNoticePrefs as jest.Mock
const save = saveNoticePrefs as jest.Mock

const PREFS = (over: object = {}) => ({
  emailConfigured: true,
  prefs: [
    { kind: 'PICKED_UP', emailAvailable: false, appAvailable: false, emailEnabled: true, appEnabled: true },
    { kind: 'OUT_FOR_DELIVERY', emailAvailable: true, appAvailable: true, emailEnabled: true, appEnabled: true },
    { kind: 'DELIVERED', emailAvailable: false, appAvailable: true, emailEnabled: true, appEnabled: true },
    { kind: 'COD_PROCESSED', emailAvailable: false, appAvailable: false, emailEnabled: true, appEnabled: true },
    { kind: 'INVOICE_ISSUED', emailAvailable: false, appAvailable: false, emailEnabled: true, appEnabled: true },
  ],
  ...over,
})

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  get.mockResolvedValue(PREFS())
})

describe('NoticePrefsScreen', () => {
  it('muestra los avisos; los que la compañía no encendió dicen que no están disponibles y el canal apagado no se puede tocar', async () => {
    await render(<NoticePrefsScreen />)
    expect(await screen.findByText('Salida a entrega')).toBeTruthy()
    expect(screen.getAllByText('La compañía no tiene este aviso encendido para su empresa.')).toHaveLength(3)   // PICKED_UP, COD_PROCESSED y INVOICE_ISSUED
    expect(screen.getByTestId('prefs-email-OUT_FOR_DELIVERY')).not.toBeDisabled()
    expect(screen.getByTestId('prefs-email-DELIVERED')).toBeDisabled()                                              // «Entregas»: la compañía solo encendió la bandeja
    expect(screen.getByTestId('prefs-app-DELIVERED')).not.toBeDisabled()
  })

  it('apagar el correo de un aviso lo guarda con lo que ya tenía en la bandeja', async () => {
    save.mockResolvedValue(PREFS({ prefs: PREFS().prefs.map((p) => (p.kind === 'OUT_FOR_DELIVERY' ? { ...p, emailEnabled: false } : p)) }))
    await render(<NoticePrefsScreen />)
    await fireEvent.press(await screen.findByTestId('prefs-email-OUT_FOR_DELIVERY'))
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1))
    expect(save).toHaveBeenCalledWith([{ kind: 'OUT_FOR_DELIVERY', emailAvailable: true, appAvailable: true, emailEnabled: false, appEnabled: true }])
  })

  it('avisa si el correo no está configurado en el servidor', async () => {
    get.mockResolvedValue(PREFS({ emailConfigured: false }))
    await render(<NoticePrefsScreen />)
    expect(await screen.findByText(/todavía no está configurado en el servidor/)).toBeTruthy()
  })
})
