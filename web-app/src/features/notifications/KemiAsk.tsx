// «Pregúntame cómo usar el sistema» (Lote 124): Kemi BUSCA, sin IA. Las palabras de la pregunta se buscan en el manual y en la FAQ (índice generado de docs/manual) y
// Kemi trae las opciones sin decir de dónde salen; al abrir una muestra la respuesta y pregunta «¿Te contesté?». Si no, busca por otro lado con lo que todavía no mostró.
// Si no hay nada por encima del suelo de calidad, lo dice. El índice (~1,4 MB) se baja solo la primera vez que alguien pregunta.
import { useRef, useState, type FormEvent } from 'react'
import { useLocation } from 'react-router-dom'
import { useLang, useT } from '../../kernel/i18n'
import { buildIndex, screenTopics, type HelpEntry, type HelpIndex } from './helpSearch'

let indexPromise: Promise<HelpIndex> | null = null
/** Carga el índice una sola vez por sesión (un chunk aparte: no pesa en el arranque). */
function loadIndex(): Promise<HelpIndex> {
  indexPromise ??= import('./helpIndex.json').then((m) => buildIndex(m.default as HelpEntry[]))
  indexPromise.catch(() => (indexPromise = null))
  return indexPromise
}

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
      const idx = await loadIndex()
      const found = idx.search(q, 5, other ? seen.current : undefined, screenTopics(pathname)).hits.map((h) => h.entry)
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

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = draft.trim()
    if (!q || busy) return
    seen.current = new Set()
    setQuestion(q)
    void run(q, false)
  }
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
            <input type="text" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder={t('notifications.kemi.askPlaceholder')} aria-label={t('notifications.kemi.askLabel')} maxLength={300} autoFocus />
          </div>
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
