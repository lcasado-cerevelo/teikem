// Pantalla de aceptación del documento legal (Lote 99): ocupa toda la pantalla mientras falte aceptar la versión vigente. El botón «Acepto» se habilita solo al llegar al final del texto.
import { useState } from 'react'
import { LangSelect } from '../../app/LangSelect'
import { useSession } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Spinner } from '../../kernel/ui'
import { BrandLockup } from '../../kernel/ui/Brand'
import { useAcceptLegal, useLegalDocument } from './legalApi'
import { LegalDocumentView } from './LegalDocumentView'

export function LegalAcceptPage() {
  const t = useT()
  const { lang, setLang, logout } = useSession()
  const doc = useLegalDocument(lang)
  const accept = useAcceptLegal()
  const [read, setRead] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    if (!doc.data?.version) return
    setError(null)
    try {
      await accept.mutateAsync({ version: doc.data.version, lang })
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <div className="legal-page">
      <header className="legal-top">
        <BrandLockup />
        <LangSelect lang={lang} onChange={setLang} />
      </header>
      <main className="legal-card">
        <h1>{doc.data?.title ?? t('legal.title')}</h1>
        <p className="subtle">{t('legal.accept.lead')}</p>
        {doc.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(doc.error).title}</p>
        ) : doc.isPending || !doc.data ? (
          <Spinner block />
        ) : (
          <LegalDocumentView key={`${doc.data.version}-${doc.data.lang}`} doc={doc.data} height="box" onReachedEnd={() => setRead(true)} />
        )}
        {!read && !doc.isPending && <p className="legal-hint" role="status">{t('legal.accept.scrollHint')}</p>}
        {error && <p className="ferr" role="alert">{error}</p>}
        <div className="legal-actions">
          <button type="button" className="btn flow" disabled={!read || accept.isPending || !doc.data} onClick={() => void submit()}>
            {accept.isPending ? t('common.loading') : t('legal.accept.button')}
          </button>
          <button type="button" className="btn" disabled={accept.isPending} onClick={() => void logout()}>{t('legal.accept.decline')}</button>
        </div>
        <p className="legal-fine">{t('legal.accept.fine')}</p>
      </main>
    </div>
  )
}
