// El documento legal único («Política de privacidad y términos de uso») en una sola columna que se lee desplazando: primero la Parte 1 (privacidad), luego la Parte 2 (términos).
// Con `onReachedEnd` avisa cuando el lector llegó al final (la aceptación se habilita ahí).
import { useCallback, useEffect, useRef } from 'react'
import type { LegalDocumentDto } from './legalApi'
import { reachedEnd } from './legalRules'
import './legal.css'

export function LegalDocumentView({ doc, onReachedEnd, height }: { doc: LegalDocumentDto; onReachedEnd?: () => void; height?: 'full' | 'box' }) {
  const ref = useRef<HTMLDivElement>(null)
  const done = useRef(false)
  const check = useCallback(() => {
    const el = ref.current
    if (!el || done.current || !onReachedEnd) return
    if (reachedEnd(el)) {
      done.current = true
      onReachedEnd()
    }
  }, [onReachedEnd])

  // un texto que cabe sin scroll (pantalla muy alta) cuenta como leído
  useEffect(() => {
    done.current = false
    const id = window.setTimeout(check, 50)
    return () => window.clearTimeout(id)
  }, [doc.version, doc.lang, check])

  // enlace a una parte (`/legal#legal-t1`): al pintarse el documento se baja a esa sección
  useEffect(() => {
    const id = window.location.hash.slice(1)
    if (id) document.getElementById(id)?.scrollIntoView?.()
  }, [doc.version, doc.lang])

  const sections = doc.sections ?? []
  let lastPart = ''
  return (
    <div ref={ref} className={`legal-doc ${height === 'box' ? 'box' : ''}`} tabIndex={0} role="region" aria-label={doc.title} onScroll={check} data-testid="legal-scroll">
      <p className="legal-intro">{doc.intro}</p>
      <p className="legal-version">{doc.version}</p>
      {sections.map((s) => {
        const heading = s.part !== lastPart ? (s.part === 'privacy' ? doc.partPrivacy : doc.partTerms) : null
        lastPart = s.part ?? ''
        return (
          <section key={s.id} id={`legal-${s.id}`}>
            {heading && <h2 className="legal-part">{heading}</h2>}
            <h3>{s.heading}</h3>
            {(s.paragraphs ?? []).map((p, i) => (
              <p key={i}>{p}</p>
            ))}
          </section>
        )
      })}
    </div>
  )
}
