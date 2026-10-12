// Lote F-E1 — Voz de la estación de escaneo (`speak`/`toggleVoice` de la maqueta): una palabra por resultado con
// `window.speechSynthesis` («Sí» / «No» / «Ya»). El interruptor se recuerda en localStorage (con try/catch: puede estar bloqueado).
import { useCallback, useState } from 'react'
import type { VoiceKind } from './scanRules'

export const VOICE_STORAGE_KEY = 'teikem.scan.voice'

/** ¿El navegador sabe hablar? */
export function voiceSupported(): boolean {
  return typeof window !== 'undefined' && 'speechSynthesis' in window && typeof window.SpeechSynthesisUtterance === 'function'
}

/** Preferencia guardada: activa por omisión; solo un '0' guardado la apaga. */
export function readVoicePreference(): boolean {
  try {
    return window.localStorage.getItem(VOICE_STORAGE_KEY) !== '0'
  } catch {
    return true
  }
}

export function saveVoicePreference(on: boolean): void {
  try {
    window.localStorage.setItem(VOICE_STORAGE_KEY, on ? '1' : '0')
  } catch {
    // sin almacenamiento: la preferencia vale solo mientras la pantalla está abierta
  }
}

/** Pronuncia la palabra. Cancela la anterior: en ráfaga no se encimen ni se acumulen. Nunca lanza. */
export function speakWord(word: string, lang: 'es' | 'en'): void {
  if (!voiceSupported() || !word) return
  try {
    window.speechSynthesis.cancel()
    const utterance = new window.SpeechSynthesisUtterance(word)
    utterance.lang = lang === 'es' ? 'es-PR' : 'en-US'
    utterance.rate = 1.05
    utterance.volume = 1
    window.speechSynthesis.speak(utterance)
  } catch {
    // la voz es un apoyo: si falla, el resultado en pantalla sigue ahí
  }
}

export function stopSpeaking(): void {
  try {
    if (voiceSupported()) window.speechSynthesis.cancel()
  } catch {
    // nada que detener
  }
}

/** Interruptor «Voz»: `supported`, `on` (siempre false sin soporte) y `toggle`. */
export function useScanVoice() {
  const supported = voiceSupported()
  const [preferred, setPreferred] = useState<boolean>(() => readVoicePreference())
  const toggle = useCallback(() => {
    const next = !preferred
    setPreferred(next)
    saveVoicePreference(next)
    if (!next) stopSpeaking()
  }, [preferred])
  return { supported, on: supported && preferred, toggle }
}

export type { VoiceKind }
