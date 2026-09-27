import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { applyProblemDetails } from '../kernel/api/problem'
import { useT } from '../kernel/i18n/useT'
import { useSession } from './session'
import { Splash } from './Splash'

/** Rutas internas: sin sesión → /login?next=…; mientras llega `me` → cargando. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const t = useT()
  const { isAuthenticated, isLoading, me, error, reloadMe, logout } = useSession()
  const location = useLocation()

  if (!isAuthenticated) {
    const next = location.pathname + location.search
    return <Navigate to={next === '/' ? '/login' : `/login?next=${encodeURIComponent(next)}`} replace />
  }
  if (isLoading) return <Splash full />
  if (!me) {
    return (
      <div className="auth">
        <div className="auth-card" role="alert">
          <h1>{t('shell.sessionError')}</h1>
          <p className="subtle">{applyProblemDetails(error).title}</p>
          <div className="links">
            <button type="button" className="btn flow" onClick={() => void reloadMe()}>
              {t('common.retry')}
            </button>
            <button type="button" className="btn" onClick={() => void logout()}>
              {t('shell.logout')}
            </button>
          </div>
        </div>
      </div>
    )
  }
  return <>{children}</>
}
