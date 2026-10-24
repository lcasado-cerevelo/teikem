// Kemi flotante en el portal (Lote 218): la mascota, su panel de opciones, el globo del mensaje nuevo, dormirla y despertarla. Misma conducta que en la web del personal.
const mockPush = jest.fn()
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush }), usePathname: () => '/orders' }))
let mockKemi: Record<string, unknown>
jest.mock('./KemiProvider', () => ({ useKemi: () => mockKemi }))
jest.mock('./useKemiVoice', () => ({ useKemiVoice: () => ({ supported: false, listening: false, error: null, start: jest.fn(), stop: jest.fn() }) }))
jest.mock('./portalBrain', () => ({ portalBrain: { audience: 'portal', ask: jest.fn().mockResolvedValue([]) } }))

import { fireEvent, render, screen } from '@testing-library/react-native'

import { DEFAULT_POS } from '../../../../shared/kemi/rules'
import { setLang } from '../../kernel/i18n/i18n'
import { KemiFloating, KemiSleepButton } from './KemiFloating'

const setAsleep = jest.fn()
const setKemiOpen = jest.fn()
const setPos = jest.fn()
const dismissBubble = jest.fn()

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  mockKemi = { unread: 0, asleep: false, setAsleep, pos: DEFAULT_POS, setPos, bubble: null, dismissBubble, kemiOpen: false, setKemiOpen, ready: true }
})

describe('KemiFloating', () => {
  it('muestra a Kemi y un toque abre su panel', async () => {
    await render(<KemiFloating />)
    expect(screen.getByTestId('kemi-fab')).toBeTruthy()
    expect(screen.queryByTestId('kemi-panel')).toBeNull()
    await fireEvent.press(screen.getByTestId('kemi-fab'))
    expect(setKemiOpen).toHaveBeenCalledWith(true)
  })

  it('el panel ofrece preguntar, mis mensajes, mis avisos y dormirla; mis mensajes lleva a la pestaña con su cuenta', async () => {
    mockKemi.kemiOpen = true
    mockKemi.unread = 3
    await render(<KemiFloating />)
    expect(screen.getByText('¡Hola! Tienes 3 mensajes sin leer. ¿Qué quieres hacer?')).toBeTruthy()
    expect(screen.getByTestId('kemi-opt-ask')).toBeTruthy()
    expect(screen.getByTestId('kemi-opt-mine')).toBeTruthy()
    expect(screen.getByTestId('kemi-opt-avisos')).toBeTruthy()
    expect(screen.queryByTestId('kemi-opt-home')).toBeNull()
    await fireEvent.press(screen.getByTestId('kemi-opt-mine'))
    expect(mockPush).toHaveBeenCalledWith('/messages')
    await fireEvent.press(screen.getByTestId('kemi-opt-avisos'))
    expect(mockPush).toHaveBeenCalledWith('/notices')
  })

  it('«Pregúntame cómo usar el portal» abre la caja de pregunta', async () => {
    mockKemi.kemiOpen = true
    await render(<KemiFloating />)
    await fireEvent.press(screen.getByTestId('kemi-opt-ask'))
    expect(screen.getByTestId('kemi-ask')).toBeTruthy()
    expect(screen.getByTestId('kemi-input')).toBeTruthy()
  })

  it('movida de su esquina ofrece llevarla de vuelta', async () => {
    mockKemi.kemiOpen = true
    mockKemi.pos = { right: 200, bottom: 150 }
    await render(<KemiFloating />)
    await fireEvent.press(screen.getByTestId('kemi-opt-home'))
    expect(setPos).toHaveBeenCalledWith(DEFAULT_POS)
  })

  it('dormirla la manda a dormir', async () => {
    mockKemi.kemiOpen = true
    await render(<KemiFloating />)
    await fireEvent.press(screen.getByTestId('kemi-opt-sleep'))
    expect(setAsleep).toHaveBeenCalledWith(true)
  })

  it('el globo avisa del mensaje nuevo y «Ver» abre los mensajes', async () => {
    mockKemi.bubble = 1
    mockKemi.unread = 1
    await render(<KemiFloating />)
    expect(screen.getByText('Te llegó un mensaje')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('kemi-see'))
    expect(dismissBubble).toHaveBeenCalled()
    expect(mockPush).toHaveBeenCalledWith('/messages')
  })

  it('varios mensajes nuevos dicen cuántos; el globo se puede cerrar', async () => {
    mockKemi.bubble = 3
    await render(<KemiFloating />)
    expect(screen.getByText('Te llegaron 3 mensajes')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('kemi-dismiss'))
    expect(dismissBubble).toHaveBeenCalled()
  })

  it('con el panel abierto no se muestra el globo', async () => {
    mockKemi.bubble = 2
    mockKemi.kemiOpen = true
    await render(<KemiFloating />)
    expect(screen.queryByTestId('kemi-bubble')).toBeNull()
  })

  it('dormida no se ve flotando: se esconde en su botón, que la despierta', async () => {
    mockKemi.asleep = true
    await render(
      <>
        <KemiFloating />
        <KemiSleepButton />
      </>,
    )
    expect(screen.queryByTestId('kemi-fab')).toBeNull()
    await fireEvent.press(screen.getByTestId('kemi-wake'))
    expect(setAsleep).toHaveBeenCalledWith(false)
  })

  it('despierta, el botón de dormida no aparece', async () => {
    await render(<KemiSleepButton />)
    expect(screen.queryByTestId('kemi-wake')).toBeNull()
  })

  it('en inglés habla en inglés', async () => {
    setLang('en')
    mockKemi.kemiOpen = true
    await render(<KemiFloating />)
    expect(screen.getByText('Ask me how to use the portal')).toBeTruthy()
    expect(screen.getByText('My messages')).toBeTruthy()
  })
})
