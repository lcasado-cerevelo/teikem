// Hablarle a Kemi (Lote 218): reconocimiento de voz del NAVEGADOR (Web Speech API: Chrome, Edge y Safari; Firefox no lo trae y ahí no se ofrece el micrófono).
// El audio lo procesa el servicio de voz del navegador/sistema; Teikem solo recibe el TEXTO que sale y lo trata como si la persona lo hubiera escrito.
// En el teléfono (app del cliente) se usa el reconocimiento del aparato por otra vía (expo-speech-recognition); aquí vive lo del navegador y las reglas comunes.

/** Idioma de reconocimiento para el idioma de la pantalla (el español de Puerto Rico por omisión, como la compañía). */
export function speechLocale(lang: string): string {
  return lang.toLowerCase().startsWith('en') ? 'en-US' : 'es-PR'
}

export type SpeechErrorCode = 'denied' | 'no-speech' | 'unsupported' | 'network' | 'other'

/** Traduce el error del reconocedor (navegador o aparato) a los pocos casos que se le explican a la persona. */
export function speechErrorCode(raw: string | undefined): SpeechErrorCode {
  switch (raw) {
    case 'not-allowed':
    case 'service-not-allowed':
      return 'denied'
    case 'no-speech':
    case 'speech-timeout':
      return 'no-speech'
    case 'network':
      return 'network'
    case 'language-not-supported':
    case 'unsupported':
      return 'unsupported'
    default:
      return 'other'
  }
}

/** Lo dicho, listo para el campo de pregunta: sin espacios de más y sin punto final (se dicta una pregunta, no un párrafo). */
export function cleanTranscript(text: string): string {
  return text.replace(/\s+/g, ' ').trim().replace(/[.。]+$/, '')
}

export interface SpeechHandlers {
  /** Lo que lleva entendido (final = ya terminó de hablar). */
  onText(text: string, final: boolean): void
  onEnd(): void
  onError(code: SpeechErrorCode): void
}

export interface SpeechSession {
  stop(): void
}

// Tipos mínimos del reconocedor del navegador (no están en la librería DOM de todos los proyectos).
interface RecognitionResultLike {
  isFinal: boolean
  0: { transcript: string }
}
interface RecognitionEventLike {
  resultIndex: number
  results: ArrayLike<RecognitionResultLike>
}
interface RecognitionLike {
  lang: string
  interimResults: boolean
  continuous: boolean
  maxAlternatives: number
  onresult: ((e: RecognitionEventLike) => void) | null
  onerror: ((e: { error?: string }) => void) | null
  onend: (() => void) | null
  start(): void
  stop(): void
  abort(): void
}
type RecognitionCtor = new () => RecognitionLike

function recognitionCtor(): RecognitionCtor | null {
  const g = globalThis as unknown as { SpeechRecognition?: RecognitionCtor; webkitSpeechRecognition?: RecognitionCtor }
  return g.SpeechRecognition ?? g.webkitSpeechRecognition ?? null
}

/** ¿Este navegador sabe reconocer voz? */
export function browserSpeechSupported(): boolean {
  return recognitionCtor() !== null
}

/** Empieza a escuchar una pregunta (una frase: termina sola cuando la persona calla). Null si el navegador no sabe. */
export function startBrowserListening(lang: string, h: SpeechHandlers): SpeechSession | null {
  const Ctor = recognitionCtor()
  if (!Ctor) return null
  const rec = new Ctor()
  rec.lang = speechLocale(lang)
  rec.interimResults = true
  rec.continuous = false
  rec.maxAlternatives = 1
  rec.onresult = (e) => {
    let text = ''
    let final = false
    for (let i = 0; i < e.results.length; i++) {
      text += e.results[i][0].transcript
      if (e.results[i].isFinal) final = true
    }
    h.onText(cleanTranscript(text), final)
  }
  rec.onerror = (e) => h.onError(speechErrorCode(e.error))
  rec.onend = () => h.onEnd()
  try {
    rec.start()
  } catch {
    h.onError('other')
    return null
  }
  return { stop: () => rec.stop() }
}
