// «Pregúntame cómo usar el sistema» (Lote 124): Kemi BUSCA, sin IA. Las palabras de la pregunta se buscan en el manual y en la FAQ (índice generado de docs/manual) y
// Kemi trae las opciones sin decir de dónde salen; al abrir una muestra la respuesta y pregunta «¿Te contesté?». Si no, busca por otro lado con lo que todavía no mostró.
// Si no hay nada por encima del suelo de calidad, lo dice. El índice (~1,4 MB) se baja solo la primera vez que alguien pregunta.
import { useRef, useState, type FormEvent } from 'react'
import { useLocation } from 'react-router-dom'
import { useLang, useT } from '../../kernel/i18n'
import { createSearchBrain } from '../../../../shared/kemi/brain'
import { screenTopics, type HelpEntry } from './helpSearch'
import { useKemiVoice } from './useKemiVoice'

/** El cerebro de Kemi para el personal (Lote 218): busca en el índice de TODO el manual y la FAQ. Es la misma interfaz que usa el portal del cliente con su propio índice; la IA futura entra aquí. El índice se baja la primera vez que alguien pregunta (un chunk aparte). */
const brain = createSearchBrain('staff', async () => (await import('./helpIndex.json')).default as HelpEntry[])

type Phase = 'ask' | 'results' | 'answer' | 'none' | 'thanks' | 'error'

export function KemiAsk({ onBack }: { onBack: () => void }) {
  const t = useT()
  const lang = useLang()
  const { pathname } = useLocation()
  const [phase, setPhase] = useState<Phase>('ask')
  const [draft, setDraft] = useState('')
  const [question, setQuestion] = useState('')
  const [hits, setHits] = useState<HelpEntry[]>([])
  const [open, setOpen] = useState<HelpEntry | null>(null)
  const [round, setRound] = useState(0)
  const [busy, setBusy] = useState(false)
  const seen = useRef(new Set<HelpEntry>())

  const run = async (q: string, other: boolean) => {
    setBusy(true)
    try {
      const found = await brain.ask({ text: q, lang, topics: screenTopics(pathname), exclude: other ? seen.current : undefined })
      for (const e of found) seen.current.add(e)
      setHits(found)
      setRound(other ? round + 1 : 0)
      setPhase(found.length ? 'results' : 'none')
    } catch {
      setPhase('error')
    } finally {
      setBusy(false)
    }
  }

  const ask = (text: string) => {
    const q = text.trim()
    if (!q || busy) return
    seen.current = new Set()
    setQuestion(q)
    void run(q, false)
  }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    ask(draft)
  }
  // hablarle: lo dicho llena el campo mientras la persona habla y, al terminar, pregunta solo
  const voice = useKemiVoice(lang, (text, final) => {
    setDraft(text)
    if (final) ask(text)
  })
  const reset = () => {
    seen.current = new Set()
    setDraft('')
    setQuestion('')
    setHits([])
    setOpen(null)
    setRound(0)
    setPhase('ask')
  }

  return (
    <div className="kemi-ask">
      {phase === 'ask' && (
        <form onSubmit={submit}>
          <p className="kemi-msg">{t('notifications.kemi.askIntro')}</p>
          {lang === 'en' && <p className="kemi-msg kemi-note">{t('notifications.kemi.askSpanishNote')}</p>}
          <div className="kemi-askrow">
            <input type="text" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder={voice.listening ? t('notifications.kemi.listening') : t('notifications.kemi.askPlaceholder')} aria-label={t('notifications.kemi.askLabel')} maxLength={300} autoFocus />
            {voice.supported && (
              <button
                type="button"
                className={`kemi-mic${voice.listening ? ' on' : ''}`}
                data-testid="kemi-mic"
                aria-label={voice.listening ? t('notifications.kemi.micStop') : t('notifications.kemi.micStart')}
                aria-pressed={voice.listening}
                title={voice.listening ? t('notifications.kemi.micStop') : t('notifications.kemi.micStart')}
                onClick={() => (voice.listening ? voice.stop() : voice.start())}
              >
                <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false">
                  <path fill="currentColor" d="M12 15a3 3 0 0 0 3-3V6a3 3 0 0 0-6 0v6a3 3 0 0 0 3 3Zm5-3a5 5 0 0 1-10 0H5a7 7 0 0 0 6 6.92V22h2v-3.08A7 7 0 0 0 19 12h-2Z" />
                </svg>
              </button>
            )}
          </div>
          {voice.listening && (
            <p className="kemi-msg kemi-note" role="status" data-testid="kemi-listening">
              {t('notifications.kemi.listening')}
            </p>
          )}
          {voice.error && (
            <p className="kemi-msg kemi-note" role="alert" data-testid="kemi-voice-error">
              {t(`notifications.kemi.voice.${voice.error}`)}
            </p>
          )}
          {/* 2026-10-22: el campo en su línea y los dos botones en la de abajo, con su separación */}
          <div className="kemi-askacts">
            <button type="submit" className="kemi-opt" disabled={draft.trim() === '' || busy}>
              {busy ? t('notifications.kemi.askLoading') : t('notifications.kemi.askSend')}
            </button>
            <button type="button" className="kemi-opt" onClick={onBack}>
              {t('notifications.kemi.askBack')}
            </button>
          </div>
        </form>
      )}

      {phase !== 'ask' && <p className="kemi-you">{question}</p>}

      {phase === 'results' && (
        <>
          <p className="kemi-msg" role="status">
            {round === 0 ? t('notifications.kemi.askResults') : t('notifications.kemi.askOther')}
          </p>
          <div className="kemi-hits">
            {hits.map((h, i) => (
              <button
                key={i}
                type="button"
                className="kemi-opt kemi-hit"
                onClick={() => {
                  setOpen(h)
                  setPhase('answer')
                }}
              >
                <span>
                  {h.t}
                  <small>{h.c}</small>
                </span>
              </button>
            ))}
          </div>
          <button type="button" className="kemi-opt" onClick={() => setPhase('ask')}>
            {t('notifications.kemi.askChange')}
          </button>
        </>
      )}

      {phase === 'answer' && open && (
        <>
          <div className="kemi-answer" role="article" aria-label={open.t}>
            <b>{open.t}</b>
            <p>{open.x}</p>
          </div>
          <p className="kemi-msg">{t('notifications.kemi.askAnswered')}</p>
          <div className="kemi-askrow">
            <button type="button" className="kemi-opt" onClick={() => setPhase('thanks')}>
              {t('notifications.kemi.askYes')}
            </button>
            <button type="button" className="kemi-opt" onClick={() => void run(question, true)} disabled={busy}>
              {t('notifications.kemi.askNo')}
            </button>
          </div>
          <button type="button" className="kemi-opt" onClick={() => setPhase('results')}>
            {t('notifications.kemi.askToList')}
          </button>
        </>
      )}

      {phase === 'none' && (
        <>
          <p className="kemi-msg" role="status">
            {round === 0 ? t('notifications.kemi.askNone') : t('notifications.kemi.askNoMore')}
          </p>
          <button type="button" className="kemi-opt" onClick={() => setPhase('ask')}>
            {t('notifications.kemi.askChange')}
          </button>
        </>
      )}

      {phase === 'thanks' && (
        <>
          <p className="kemi-msg" role="status">
            {t('notifications.kemi.askThanks')}
          </p>
          <button type="button" className="kemi-opt" onClick={reset}>
            {t('notifications.kemi.askAgain')}
          </button>
        </>
      )}

      {phase === 'error' && (
        <>
          <p className="kemi-msg" role="alert">
            {t('notifications.kemi.askError')}
          </p>
          <button type="button" className="kemi-opt" onClick={() => setPhase('ask')}>
            {t('notifications.kemi.askChange')}
          </button>
        </>
      )}

      {phase !== 'ask' && (
        <button type="button" className="kemi-opt" onClick={onBack}>
          {t('notifications.kemi.askBack')}
        </button>
      )}
    </div>
  )
}
