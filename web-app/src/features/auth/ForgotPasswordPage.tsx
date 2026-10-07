import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { requestPasswordReset } from '../../kernel/auth/auth'
import { useT } from '../../kernel/i18n/useT'
import { AuthLayout } from './AuthLayout'

/** «Olvidé mi contraseña» (2026-10-07): pide el correo y manda un enlace de un solo uso. Responde igual exista o no el correo. */
export default function ForgotPasswordPage() {
  const t = useT()
  const [email, setEmail] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState<{ message: string; link?: string | null } | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!email.trim()) {
      setError(t('auth.errors.emailInvalid'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      const res = await requestPasswordReset(email.trim())
      setSent({ message: res.message ?? t('auth.forgot.sent'), link: res.link })
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthLayout title={t('auth.forgot.title')} subtitle={sent ? undefined : t('auth.forgot.subtitle')}>
      {sent ? (
        <>
          <p role="status">{sent.message}</p>
          {sent.link && (
            <p className="help">
              {t('auth.forgot.devLink')} <a href={sent.link}>{sent.link}</a>
            </p>
          )}
        </>
      ) : (
        <form onSubmit={submit} noValidate>
          {error && (
            <div className="alert" role="alert">
              {error}
            </div>
          )}
          <div className="f">
            <label htmlFor="forgot-email">{t('auth.fields.email')}</label>
            <input id="forgot-email" type="email" autoComplete="username" inputMode="email" autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
          <button type="submit" className="btn flow block" disabled={busy}>
            {busy ? t('common.loading') : t('auth.forgot.submit')}
          </button>
        </form>
      )}
      <p style={{ textAlign: 'center', marginTop: 12 }}>
        <Link to="/login">{t('auth.backToLogin')}</Link>
      </p>
    </AuthLayout>
  )
}
