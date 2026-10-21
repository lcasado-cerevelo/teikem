// Pie discreto del menú lateral (2026-10-21, pedido de Luis): enlaces a la política de privacidad y los términos de uso para el usuario con sesión, los derechos
// reservados del dueño y «Powered by Teikem». Chico y apagado a propósito (que no llame la atención de los clientes); se oculta con el menú contraído.
import { Link } from 'react-router-dom'
import { useT } from '../kernel/i18n'

/** Quién tiene los derechos de la plataforma y cómo se llama el producto. */
export const OWNER_NAME = 'Cerevelo LLC'
export const PRODUCT_NAME = 'Teikem'

/** Anclas del documento legal único (`/legal`): la Parte 1 (privacidad) empieza en la sección p1 y la Parte 2 (términos) en la t1. */
export const LEGAL_PRIVACY_PATH = '/legal#legal-p1'
export const LEGAL_TERMS_PATH = '/legal#legal-t1'

export function AppLegalFooter({ year = new Date().getFullYear() }: { year?: number }) {
  const t = useT()
  return (
    <footer className="rlegal" aria-label={t('shell.legal.label')}>
      <div className="rlegal-links">
        <Link to={LEGAL_PRIVACY_PATH} target="_blank" rel="noopener noreferrer">
          {t('shell.legal.privacy')}
        </Link>
        <span aria-hidden="true">·</span>
        <Link to={LEGAL_TERMS_PATH} target="_blank" rel="noopener noreferrer">
          {t('shell.legal.terms')}
        </Link>
      </div>
      <div className="rlegal-copy">{t('shell.legal.rights', { year: String(year), owner: OWNER_NAME })}</div>
      <div className="rlegal-powered">{t('shell.legal.powered', { product: PRODUCT_NAME })}</div>
    </footer>
  )
}
