// Hablarle a Kemi en la web (Lote 218): reconocimiento de voz del navegador (shared/kemi/speech.ts). La persona toca el micrófono, dice su pregunta y lo que se entiende llega como TEXTO, el mismo camino
// que si lo hubiera escrito; Teikem no recibe ni guarda el audio. Donde el navegador no sabe (Firefox) no se ofrece el micrófono.
import { useCallback, useEffect, useRef, useState } from 'react'
import { browserSpeechSupported, startBrowserListening, type SpeechErrorCode, type SpeechSession } from '../../../../shared/kemi/speech'

export interface KemiVoice {
  supported: boolean
  listening: boolean
  error: SpeechErrorCode | null
  start: () => void
  stop: () => void
}

/** `onText` recibe lo que va entendiendo (final = la persona terminó de hablar). */
export function useKemiVoice(lang: string, onText: (text: string, final: boolean) => void): KemiVoice {
  const [supported] = useState(() => browserSpeechSupported())
  const [listening, setListening] = useState(false)
  const [error, setError] = useState<SpeechErrorCode | null>(null)
  const session = useRef<SpeechSession | null>(null)
  const handler = useRef(onText)
  useEffect(() => {
    handler.current = onText
  }, [onText])
  // al cerrar la pantalla se deja de escuchar
  useEffect(() => () => session.current?.stop(), [])

  const start = useCallback(() => {
    setError(null)
    session.current?.stop()
    session.current = startBrowserListening(lang, {
      onText: (text, final) => handler.current(text, final),
      onEnd: () => setListening(false),
      onError: (code) => {
        setListening(false)
        setError(code)
      },
    })
    if (session.current) setListening(true)
    else setError('unsupported')
  }, [lang])

  const stop = useCallback(() => session.current?.stop(), [])
  return { supported, listening, error, start, stop }
}
