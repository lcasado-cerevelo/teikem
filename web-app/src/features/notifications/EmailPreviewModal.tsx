// Vista previa del correo de una regla de aviso (Lote 243): arma el correo con el texto del formulario (aunque la regla no esté
// guardada) vía POST /notifications/preview, sin enviar nada ni escribir en el registro. Con un número de orden usa la orden real
// (exige sales.view y el módulo de ventas: el API contesta 403 o 404 y se muestra tal cual); sin él, un ejemplo.
// El correo se pinta en un iframe sin scripts (sandbox="allow-same-origin") y con fondo claro propio: así llega, también con el tema oscuro.
import { useCallback, useEffect, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal, Spinner } from '../../kernel/ui'
import { usePreview, type NotificationEventDto, type NotificationPreviewDto } from './notificationsApi'

export interface PreviewTemplate {
  subjectEs: string
  subjectEn: string
  bodyEs: string
  bodyEn: string
}

type PreviewLang = 'es' | 'en'
type PreviewAudience = 'STAFF' | 'CUSTOMER'

/** Tamaño legible de un adjunto: 1.2 KB, 340 KB. */
function attachmentSize(bytes: number | undefined): string {
  const n = bytes ?? 0
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(1)} MB`
}

export function EmailPreviewModal({
  event,
  template,
  initialLang = 'es',
  onClose,
}: {
  event: NotificationEventDto
  template: PreviewTemplate
  initialLang?: PreviewLang
  onClose: () => void
}) {
  const t = useT()
  const { mutateAsync, isPending: busy } = usePreview()
  const [lang, setLang] = useState<PreviewLang>(initialLang)
  const [audience, setAudience] = useState<PreviewAudience>('STAFF')
  const [reference, setReference] = useState('')
  const [result, setResult] = useState<NotificationPreviewDto | null>(null)
  const [shownRef, setShownRef] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [height, setHeight] = useState(240)
  const frame = useRef<HTMLIFrameElement>(null)
  const latest = useRef({ template, reference })
  useEffect(() => {
    latest.current = { template, reference }
  })
  const hasCustomer = (event.contextRecipients ?? []).some((c) => c.audience === 'CUSTOMER')

  const run = useCallback(
    async (nextLang: PreviewLang, nextAudience: PreviewAudience) => {
      const { template: tpl, reference: ref } = latest.current
      setError(null)
      try {
        const r = await mutateAsync({
          eventCode: event.code ?? '',
          channel: 'EMAIL',
          subject: nextLang === 'en' ? tpl.subjectEn : tpl.subjectEs,
          body: nextLang === 'en' ? tpl.bodyEn : tpl.bodyEs,
          lang: nextLang,
          audience: nextAudience,
          reference: ref.trim() || null,
        })
        setResult(r)
        setShownRef(ref.trim())
      } catch (e) {
        setResult(null)
        setError(applyProblemDetails(e).title)
      }
    },
    [event.code, mutateAsync],
  )

  // al abrir y cada vez que cambia el idioma o «Personal / Cliente»; el número de orden se aplica con Enter o «Actualizar»
  useEffect(() => {
    void run(lang, audience)
  }, [run, lang, audience])

  const fit = () => {
    const doc = frame.current?.contentDocument
    // el alto del contenido (no scrollHeight: con el marco más alto que el contenido nunca encogería)
    if (doc?.documentElement) setHeight(Math.max(120, Math.ceil(doc.documentElement.getBoundingClientRect().height) + 2))
  }

  return (
    <Modal
      open
      size="lg"
      title={t('notifications.rules.preview.title')}
      onClose={onClose}
      footer={
        <button type="button" className="btn" onClick={onClose}>
          {t('notifications.rules.preview.close')}
        </button>
      }
    >
      <div className="pb nr-prev">
        <div className="nr-prev-fields">
          <div className="f">
            <label htmlFor="nr-prev-lang">{t('notifications.rules.preview.lang')}</label>
            <select id="nr-prev-lang" value={lang} onChange={(e) => setLang(e.target.value as PreviewLang)}>
              <option value="es">{t('notifications.rules.preview.langEs')}</option>
              <option value="en">{t('notifications.rules.preview.langEn')}</option>
            </select>
          </div>
          {hasCustomer && (
            <div className="f">
              <label htmlFor="nr-prev-aud">{t('notifications.rules.preview.audience')}</label>
              <select id="nr-prev-aud" value={audience} onChange={(e) => setAudience(e.target.value as PreviewAudience)}>
                <option value="STAFF">{t('notifications.rules.preview.audienceSTAFF')}</option>
                <option value="CUSTOMER">{t('notifications.rules.preview.audienceCUSTOMER')}</option>
              </select>
            </div>
          )}
          <div className="f nr-prev-ref">
            <label htmlFor="nr-prev-ref">{t('notifications.rules.preview.reference')}</label>
            <div className="nr-row">
              <input
                id="nr-prev-ref"
                value={reference}
                placeholder={t('notifications.rules.preview.referencePh')}
                autoComplete="off"
                maxLength={60}
                onChange={(e) => setReference(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault()
                    void run(lang, audience)
                  }
                }}
              />
              <button type="button" className="btn" disabled={busy} onClick={() => void run(lang, audience)}>
                {t('notifications.rules.preview.refresh')}
              </button>
            </div>
          </div>
        </div>

        {error && (
          <p className="ferr" role="alert">
            {error}
          </p>
        )}
        {busy && !result && <Spinner block />}

        {result && (
          <div className="nr-prev-result" aria-busy={busy}>
            <p className="help nr-prev-source">{result.usedSample ? t('notifications.rules.preview.sample') : t('notifications.rules.preview.real', { ref: shownRef })}</p>
            <dl className="nr-prev-meta">
              <dt>{t('notifications.rules.preview.subject')}</dt>
              <dd>{result.subject}</dd>
              <dt>{t('notifications.rules.preview.attachments')}</dt>
              <dd>
                {(result.attachments ?? []).length === 0 ? (
                  t('notifications.rules.preview.noAttachments')
                ) : (
                  <ul className="nr-prev-files">
                    {(result.attachments ?? []).map((a) => (
                      <li key={a.fileName}>
                        {a.fileName} <span className="nr-prev-size">({attachmentSize(a.sizeBytes)})</span>
                      </li>
                    ))}
                  </ul>
                )}
              </dd>
            </dl>
            <iframe
              ref={frame}
              className="nr-prev-frame"
              title={t('notifications.rules.preview.frame')}
              sandbox="allow-same-origin"
              srcDoc={result.bodyHtml ?? ''}
              style={{ height }}
              onLoad={fit}
            />
          </div>
        )}
      </div>
    </Modal>
  )
}
