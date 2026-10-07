import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { resetPassword } from '../../kernel/auth/auth'
import { useT } from '../../kernel/i18n/useT'
import { AuthLayout } from './AuthLayout'

/** Largo mínimo de la contraseña (Identity: RequiredLength = 12). */
const MIN_PASSWORD = 12

/** Enlace del correo de «Olvidé mi contraseña» (/reset-password?email=…&token=…): elegir la contraseña nueva. */
export default function ResetPasswordPage() {
  const t = useT()
  const [params] = useSearchParams()
  const email = params.get('email') ?? ''
  const token = params.get('token') ?? ''
  const [pwd, setPwd] = useState('')
  const [pwd2, setPwd2] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (pwd.length < MIN_PASSWORD) return setError(t('auth.onboarding.passwordShort', { min: MIN_PASSWORD }))
    if (pwd !== pwd2) return setError(t('auth.onboarding.passwordMismatch'))
    setBusy(true)
    setError(null)
    try {
      await resetPassword(email, token, pwd)
      setDone(true)
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(false)
    }
  }

  if (!email || !token) {
    return (
      <AuthLayout title={t('auth.reset.title')}>
        <div className="alert" role="alert">
          {t('auth.reset.badLink')}
        </div>
        <p style={{ textAlign: 'center' }}>
          <Link to="/forgot-password">{t('auth.reset.askAgain')}</Link>
        </p>
      </AuthLayout>
    )
  }

  if (done) {
    return (
      <AuthLayout title={t('auth.reset.doneTitle')}>
        <p role="status">{t('auth.reset.done')}</p>
        <Link className="btn flow block" to="/login">
          {t('auth.reset.goLogin')}
        </Link>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title={t('auth.reset.title')} subtitle={t('auth.reset.subtitle', { email })}>
      <form onSubmit={submit} noValidate>
        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}
        <div className="f">
          <label htmlFor="reset-pwd">{t('auth.onboarding.newPassword')}</label>
          <input id="reset-pwd" type="password" autoComplete="new-password" autoFocus value={pwd} onChange={(e) => setPwd(e.target.value)} />
        </div>
        <div className="f">
          <label htmlFor="reset-pwd2">{t('auth.onboarding.confirmPassword')}</label>
          <input id="reset-pwd2" type="password" autoComplete="new-password" value={pwd2} onChange={(e) => setPwd2(e.target.value)} />
        </div>
        <button type="submit" className="btn flow block" disabled={busy}>
          {busy ? t('common.loading') : t('auth.reset.submit')}
        </button>
      </form>
      <p style={{ textAlign: 'center', marginTop: 12 }}>
        <Link to="/forgot-password">{t('auth.reset.askAgain')}</Link>
      </p>
    </AuthLayout>
  )
}
