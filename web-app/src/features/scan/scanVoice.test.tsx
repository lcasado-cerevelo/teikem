// Lote F-E1 — Voz de la estación: soporte, preferencia en localStorage (con almacenamiento roto) y una palabra a la vez.
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readVoicePreference, saveVoicePreference, speakWord, useScanVoice, VOICE_STORAGE_KEY, voiceSupported } from './scanVoice'

class FakeUtterance {
  lang = ''
  rate = 1
  volume = 1
  text: string
  constructor(text: string) {
    this.text = text
  }
}

const synth = { cancel: vi.fn(), speak: vi.fn() }

function install() {
  vi.stubGlobal('speechSynthesis', synth)
  vi.stubGlobal('SpeechSynthesisUtterance', FakeUtterance)
}

beforeEach(() => {
  synth.cancel.mockReset()
  synth.speak.mockReset()
  window.localStorage.clear()
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('voz de la estación', () => {
  it('sin speechSynthesis no hay voz y hablar no hace nada ni lanza', () => {
    expect(voiceSupported()).toBe(false)
    expect(() => speakWord('Sí', 'es')).not.toThrow()
    const { result } = renderHook(() => useScanVoice())
    expect(result.current).toMatchObject({ supported: false, on: false })
  })

  it('activa por omisión; se apaga y se recuerda; solo un «0» guardado la apaga', () => {
    install()
    expect(readVoicePreference()).toBe(true)
    const { result, unmount } = renderHook(() => useScanVoice())
    expect(result.current).toMatchObject({ supported: true, on: true })
    act(() => result.current.toggle())
    expect(result.current.on).toBe(false)
    expect(window.localStorage.getItem(VOICE_STORAGE_KEY)).toBe('0')
    expect(synth.cancel).toHaveBeenCalled() // al silenciar corta lo que estuviera hablando
    unmount()
    expect(renderHook(() => useScanVoice()).result.current.on).toBe(false) // recordada
    act(() => saveVoicePreference(true))
    expect(readVoicePreference()).toBe(true)
  })

  it('con el almacenamiento bloqueado no se rompe: vale lo de la pantalla abierta', () => {
    install()
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('bloqueado')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('bloqueado')
    })
    expect(readVoicePreference()).toBe(true)
    const { result } = renderHook(() => useScanVoice())
    act(() => result.current.toggle())
    expect(result.current.on).toBe(false)
  })

  it('dice la palabra en el idioma (es-PR / en-US) y cancela la anterior: en ráfaga no se encima', () => {
    install()
    speakWord('Sí', 'es')
    speakWord('Already', 'en')
    expect(synth.cancel).toHaveBeenCalledTimes(2)
    const spoken = synth.speak.mock.calls.map((c) => c[0] as FakeUtterance)
    expect(spoken.map((u) => [u.text, u.lang])).toEqual([
      ['Sí', 'es-PR'],
      ['Already', 'en-US'],
    ])
    // cancel va antes de cada speak
    expect(synth.cancel.mock.invocationCallOrder[0]).toBeLessThan(synth.speak.mock.invocationCallOrder[0])
  })

  it('si el navegador falla al hablar, no se propaga', () => {
    install()
    synth.speak.mockImplementation(() => {
      throw new Error('sin voces')
    })
    expect(() => speakWord('No', 'es')).not.toThrow()
  })
})
