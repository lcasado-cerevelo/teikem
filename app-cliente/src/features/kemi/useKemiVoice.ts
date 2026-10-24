// Hablarle a Kemi en el portal (Lote 218): reconocimiento de voz del aparato en el teléfono y del navegador en la web, con el mismo paquete (expo-speech-recognition).
// La persona toca el micrófono, dice su pregunta y lo que se entiende llega como TEXTO (el mismo camino que si lo hubiera escrito). Teikem no recibe ni guarda el audio.
import { ExpoSpeechRecognitionModule, useSpeechRecognitionEvent } from 'expo-speech-recognition'
import { useCallback, useMemo, useState } from 'react'
import { Platform } from 'react-native'

import { cleanTranscript, speechErrorCode, speechLocale, type SpeechErrorCode } from '../../../../shared/kemi/speech'

export interface KemiVoice {
  /** ¿Este aparato o navegador sabe reconocer voz? Si no, no se ofrece el micrófono. */
  supported: boolean
  listening: boolean
  error: SpeechErrorCode | null
  start: () => Promise<void>
  stop: () => void
}

/** `onText` recibe lo que va entendiendo (final = la persona terminó de hablar). */
export function useKemiVoice(lang: string, onText: (text: string, final: boolean) => void): KemiVoice {
  const supported = useMemo(() => {
    try {
      return ExpoSpeechRecognitionModule.isRecognitionAvailable()
    } catch {
      return false
    }
  }, [])
  const [listening, setListening] = useState(false)
  const [error, setError] = useState<SpeechErrorCode | null>(null)

  useSpeechRecognitionEvent('start', () => setListening(true))
  useSpeechRecognitionEvent('end', () => setListening(false))
  useSpeechRecognitionEvent('error', (e) => {
    setListening(false)
    // «aborted» es que la propia persona cortó: no es un error que explicar
    if (e.error !== 'aborted') setError(speechErrorCode(e.error))
  })
  useSpeechRecognitionEvent('result', (e) => {
    const text = cleanTranscript(e.results.map((r) => r.transcript).join(' '))
    if (text) onText(text, e.isFinal)
  })

  const start = useCallback(async () => {
    setError(null)
    if (!supported) {
      setError('unsupported')
      return
    }
    try {
      // en el navegador el permiso lo pide el propio navegador al empezar
      if (Platform.OS !== 'web') {
        const perm = await ExpoSpeechRecognitionModule.requestPermissionsAsync()
        if (!perm.granted) {
          setError('denied')
          return
        }
      }
      ExpoSpeechRecognitionModule.start({ lang: speechLocale(lang), interimResults: true, continuous: false })
      setListening(true)   // el evento «start» confirma, pero la pantalla no espera a que llegue
    } catch {
      setError('other')
    }
  }, [lang, supported])

  const stop = useCallback(() => {
    try {
      ExpoSpeechRecognitionModule.stop()
    } catch {
      /* ya estaba detenido */
    }
  }, [])

  return { supported, listening, error, start, stop }
}
