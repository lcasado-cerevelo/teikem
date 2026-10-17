// /legal — el documento legal vigente para leer, sin sesión (también lo enlazan las invitaciones y los portales).
import { LangSelect } from '../../app/LangSelect'
import { useSession } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Spinner } from '../../kernel/ui'
import { BrandLockup } from '../../kernel/ui/Brand'
import { useLegalDocument } from './legalApi'
import { LegalDocumentView } from './LegalDocumentView'

export default function LegalPage() {
  const t = useT()
  const { lang, setLang } = useSession()
  const doc = useLegalDocument(lang)
  return (
    <div className="legal-page">
      <header className="legal-top">
        <BrandLockup />
        <LangSelect lang={lang} onChange={setLang} />
      </header>
      <main className="legal-card">
        <h1>{doc.data?.title ?? t('legal.title')}</h1>
        {doc.isError ? <p className="ferr" role="alert">{applyProblemDetails(doc.error).title}</p> : doc.isPending || !doc.data ? <Spinner block /> : <LegalDocumentView doc={doc.data} />}
      </main>
    </div>
  )
}
