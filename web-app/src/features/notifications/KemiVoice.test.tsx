// Hablarle a Kemi en la web (Lote 218): el micrófono solo se ofrece donde el navegador sabe reconocer voz; lo dicho llena el campo y, al terminar, Kemi pregunta sola; los errores se explican.
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import type { SpeechHandlers } from '../../../../shared/kemi/speech'

const voice = vi.hoisted(() => ({ supported: true, handlers: null as null | SpeechHandlers, stop: vi.fn(), lang: '' }))
const ask = vi.hoisted(() => vi.fn())

vi.mock('../../../../shared/kemi/speech', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../shared/kemi/speech')>()
  return {
    ...actual,
    browserSpeechSupported: () => voice.supported,
    startBrowserListening: (lang: string, h: SpeechHandlers) => {
      voice.handlers = h
      voice.lang = lang
      return { stop: voice.stop }
    },
  }
})
vi.mock('../../../../shared/kemi/brain', () => ({ createSearchBrain: () => ({ audience: 'staff', ask }) }))

import { KemiAsk } from './KemiAsk'

const hit = { k: 'f' as const, t: '¿Cómo cambio mi contraseña?', c: 'Acceso seguro', x: 'En Ajustes → Contraseña.' }

function wrap() {
  return render(
    <MemoryRouter>
      <KemiAsk onBack={() => undefined} />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  voice.supported = true
  voice.handlers = null
  setLang('es')
})

describe('Kemi: hablarle (web)', () => {
  it('sin reconocimiento de voz en el navegador no se ofrece el micrófono', () => {
    voice.supported = false
    wrap()
    expect(screen.queryByTestId('kemi-mic')).not.toBeInTheDocument()
  })

  it('el micrófono escucha en el idioma de la pantalla; lo dicho llena el campo y al terminar pregunta solo', async () => {
    ask.mockResolvedValue([hit])
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByTestId('kemi-mic'))
    expect(voice.lang).toBe('es')
    expect(await screen.findByTestId('kemi-listening')).toHaveTextContent('Te escucho')
    act(() => voice.handlers!.onText('cómo cambio mi', false))
    expect(screen.getByRole('textbox', { name: 'Tu pregunta para Kemi' })).toHaveValue('cómo cambio mi')
    expect(ask).not.toHaveBeenCalled()
    act(() => voice.handlers!.onText('cómo cambio mi contraseña', true))
    expect(await screen.findByText('Esto encontré:')).toBeInTheDocument()
    expect(ask).toHaveBeenCalledWith(expect.objectContaining({ text: 'cómo cambio mi contraseña', lang: 'es' }))
    act(() => voice.handlers!.onEnd())
  })

  it('tocar el micrófono otra vez deja de escuchar', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByTestId('kemi-mic'))
    const mic = await screen.findByRole('button', { name: 'Dejar de escuchar' })
    await user.click(mic)
    expect(voice.stop).toHaveBeenCalledTimes(1)
  })

  it('si el navegador no deja usar el micrófono se lo explica', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByTestId('kemi-mic'))
    act(() => voice.handlers!.onError('denied'))
    expect(await screen.findByTestId('kemi-voice-error')).toHaveTextContent('permiso para usar el micrófono')
    expect(screen.queryByTestId('kemi-listening')).not.toBeInTheDocument()
  })

  it('en inglés escucha en inglés y habla en inglés', async () => {
    setLang('en')
    const user = userEvent.setup()
    wrap()
    const panel = screen.getByRole('textbox', { name: 'Your question for Kemi' }).closest('form')!
    await user.click(within(panel).getByRole('button', { name: 'Talk to Kemi' }))
    expect(voice.lang).toBe('en')
  })
})
