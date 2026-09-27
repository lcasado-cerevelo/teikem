import { useState } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { getPendingTenantSelection, selectTenant } from '../../kernel/auth/auth'
import { useT } from '../../kernel/i18n/useT'
import { AuthLayout } from './AuthLayout'
import { safeNext } from './next'

/** Selección de compañía cuando el login responde tenant_selection: repite el login con el TenantId elegido. */
export default function SelectTenantPage() {
  const t = useT()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<number | null>(null)
  const tenants = getPendingTenantSelection()
  const next = safeNext(params.get('next'))

  if (!tenants) return <Navigate to="/login" replace />

  async function choose(tenantId: number) {
    setBusy(tenantId)
    setError(null)
    try {
      const outcome = await selectTenant(tenantId)
      if (outcome.status === 'ok') navigate(next, { replace: true })
      else if (outcome.status === 'mfa_required') navigate(`/mfa?next=${encodeURIComponent(next)}`, { replace: true })
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(null)
    }
  }

  return (
    <AuthLayout title={t('auth.selectTenant.title')} subtitle={t('auth.selectTenant.subtitle')}>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
      <div className="tenant-list">
        {tenants.map((tn) => (
          <button key={tn.tenantId} type="button" disabled={busy !== null} onClick={() => void choose(tn.tenantId ?? 0)}>
            <span>{tn.name}</span>
            {tn.isDefault && <span className="tag">{t('auth.selectTenant.default')}</span>}
          </button>
        ))}
      </div>
      <div className="links">
        <button type="button" className="linkbtn" onClick={() => navigate('/login', { replace: true })}>
          {t('auth.backToLogin')}
        </button>
      </div>
    </AuthLayout>
  )
}
