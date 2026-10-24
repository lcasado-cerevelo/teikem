// Kemi en el portal (Lote 218): la caja de pregunta con el cerebro y el micrófono simulados. Misma conversación que en la web: preguntar, escoger una opción, «¿Te contesté?» y «busca por otro lado».
const mockAsk = jest.fn()
jest.mock('./portalBrain', () => ({ portalBrain: { audience: 'portal', ask: (...a: unknown[]) => mockAsk(...a) } }))
jest.mock('expo-router', () => ({ usePathname: () => '/warehouse', useRouter: () => ({ push: jest.fn() }) }))
let mockVoice: { supported: boolean; listening: boolean; error: string | null; start: jest.Mock; stop: jest.Mock }
let mockOnText: (text: string, final: boolean) => void = () => undefined
jest.mock('./useKemiVoice', () => ({
  useKemiVoice: (_lang: string, onText: (text: string, final: boolean) => void) => {
    mockOnText = onText
    return mockVoice
  },
}))

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import type { HelpEntry } from '../../../../shared/kemi/helpSearch'
import { setLang } from '../../kernel/i18n/i18n'
import { KemiAsk } from './KemiAsk'

const entry = (t: string, x: string): HelpEntry => ({ k: 'f', t, c: 'Portal del cliente de almacén', x })
const A = entry('¿Cómo cancelo un aviso de llegada?', 'Con Cancelar aviso, mientras siga esperado.')
const B = entry('¿Puedo cambiar un aviso de llegada?', 'Sí, con Editar, mientras siga esperado.')

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  mockVoice = { supported: false, listening: false, error: null, start: jest.fn().mockResolvedValue(undefined), stop: jest.fn() }
})

async function ask(text: string) {
  await fireEvent.changeText(screen.getByTestId('kemi-input'), text)
  await fireEvent.press(screen.getByTestId('kemi-send'))
}

describe('KemiAsk del portal', () => {
  it('pregunta, muestra las opciones y la respuesta, y agradece si sirvió', async () => {
    mockAsk.mockResolvedValue([A, B])
    await render(<KemiAsk onBack={jest.fn()} />)
    await ask('cómo cancelo un aviso')
    expect(await screen.findByText('Esto encontré:')).toBeTruthy()
    // la pregunta va con el contexto de la pantalla del portal donde está la persona
    expect(mockAsk).toHaveBeenCalledWith(expect.objectContaining({ text: 'cómo cancelo un aviso', lang: 'es', topics: expect.arrayContaining(['portal del cliente de almacen']) }))
    await fireEvent.press(screen.getByTestId('kemi-hit-0'))
    expect(screen.getByTestId('kemi-answer')).toBeTruthy()
    expect(screen.getByText('Con Cancelar aviso, mientras siga esperado.')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('kemi-yes'))
    expect(await screen.findByText('¡Qué bueno que te sirvió!')).toBeTruthy()
  })

  it('«No» busca por otro lado sin repetir lo ya mostrado, y si ya no hay dice que no encontró más', async () => {
    mockAsk.mockResolvedValueOnce([A]).mockResolvedValueOnce([B]).mockResolvedValueOnce([])
    await render(<KemiAsk onBack={jest.fn()} />)
    await ask('aviso')
    await fireEvent.press(await screen.findByTestId('kemi-hit-0'))
    await fireEvent.press(screen.getByTestId('kemi-no'))
    expect(await screen.findByText('Busquemos por otro lado:')).toBeTruthy()
    const second = mockAsk.mock.calls[1][0] as { exclude: Set<HelpEntry> }
    expect(second.exclude.has(A)).toBe(true)
    await fireEvent.press(screen.getByTestId('kemi-hit-0'))
    await fireEvent.press(screen.getByTestId('kemi-no'))
    expect(await screen.findByText(/No encontré más/)).toBeTruthy()
  })

  it('sin nada por encima del suelo lo dice, y si la ayuda no carga avisa', async () => {
    mockAsk.mockResolvedValueOnce([])
    await render(<KemiAsk onBack={jest.fn()} />)
    await ask('xyzzy')
    expect(await screen.findByText(/No encontré nada sobre eso/)).toBeTruthy()
    await fireEvent.press(screen.getByText('Cambiar la pregunta'))
    mockAsk.mockRejectedValueOnce(new Error('sin red'))
    await ask('algo')
    expect(await screen.findByText('No pude cargar la ayuda. Intenta de nuevo.')).toBeTruthy()
  })

  it('en inglés avisa que las respuestas salen en español', async () => {
    setLang('en')
    await render(<KemiAsk onBack={jest.fn()} />)
    expect(screen.getByText(/written in Spanish/)).toBeTruthy()
  })

  it('el micrófono solo se ofrece donde se sabe reconocer voz', async () => {
    await render(<KemiAsk onBack={jest.fn()} />)
    expect(screen.queryByTestId('kemi-mic')).toBeNull()
  })

  it('hablarle: el micrófono empieza a escuchar, lo dicho llena el campo y al terminar pregunta solo', async () => {
    mockVoice.supported = true
    mockAsk.mockResolvedValue([A])
    await render(<KemiAsk onBack={jest.fn()} />)
    await fireEvent.press(screen.getByTestId('kemi-mic'))
    expect(mockVoice.start).toHaveBeenCalledTimes(1)
    await act(async () => mockOnText('cómo cancelo', false))
    expect(screen.getByTestId('kemi-input').props.value).toBe('cómo cancelo')
    expect(mockAsk).not.toHaveBeenCalled()
    await act(async () => mockOnText('cómo cancelo un aviso de llegada', true))
    await waitFor(() => expect(mockAsk).toHaveBeenCalledWith(expect.objectContaining({ text: 'cómo cancelo un aviso de llegada' })))
    expect(await screen.findByText('Esto encontré:')).toBeTruthy()
  })

  it('mientras escucha el micrófono sirve para dejar de escuchar', async () => {
    mockVoice.supported = true
    mockVoice.listening = true
    await render(<KemiAsk onBack={jest.fn()} />)
    expect(screen.getByTestId('kemi-listening')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('kemi-mic'))
    expect(mockVoice.stop).toHaveBeenCalledTimes(1)
    expect(mockVoice.start).not.toHaveBeenCalled()
  })

  it('si el micrófono falla le explica a la persona qué pasó', async () => {
    mockVoice.supported = true
    mockVoice.error = 'denied'
    await render(<KemiAsk onBack={jest.fn()} />)
    expect(screen.getByTestId('kemi-voice-error').props.children).toMatch(/permiso para usar el micrófono/)
  })
})
