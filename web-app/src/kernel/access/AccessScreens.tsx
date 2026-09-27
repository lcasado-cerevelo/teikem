import { Link } from 'react-router-dom'
import { useT } from '../i18n/useT'

function LockIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
      <rect x="4" y="11" width="16" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
    </svg>
  )
}

function PowerIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
      <path d="M12 2v10" />
      <path d="M18.4 6.6a9 9 0 1 1-12.8 0" />
    </svg>
  )
}

/** Pantalla 'Sin permiso'. */
export function ForbiddenScreen() {
  const t = useT()
  return (
    <div className="empty" role="alert" data-testid="forbidden-screen">
      <div>
        <div className="ic">
          <LockIcon />
        </div>
        <h2>{t('access.forbidden.title')}</h2>
        <p>{t('access.forbidden.body')}</p>
        <Link className="btn" to="/">
          {t('access.backHome')}
        </Link>
      </div>
    </div>
  )
}

/** Pantalla 'Módulo apagado'. */
export function ModuleOffScreen({ module }: { module?: string }) {
  const t = useT()
  return (
    <div className="empty" role="alert" data-testid="module-off-screen">
      <div>
        <div className="ic">
          <PowerIcon />
        </div>
        <h2>{t('access.moduleOff.title')}</h2>
        <p>{module ? t('access.moduleOff.bodyNamed', { module }) : t('access.moduleOff.body')}</p>
        <Link className="btn" to="/">
          {t('access.backHome')}
        </Link>
      </div>
    </div>
  )
}
