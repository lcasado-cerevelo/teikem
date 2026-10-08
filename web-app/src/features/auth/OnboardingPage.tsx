import { useEffect, useState, type FormEvent } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import {
  cancelMfa,
  getOnboarding,
  sendOnboardingEmail,
  setOnboardingPassword,
  verifyOnboardingEmail,
  type OnboardingStateDto,
} from '../../kernel/auth/auth'
import { getMfaChallenge } from '../../kernel/auth/tokens'
import { useT } from '../../kernel/i18n/useT'
import { AuthLayout } from './AuthLayout'
import { PasswordInput } from './PasswordInput'
import { safeNext } from './next'

/** Largo mínimo de la contraseña (Identity: RequiredLength = 12). */
const MIN_PASSWORD = 12

/**
 * Primer ingreso obligatorio (pedido de Luis, 2026-09-30): (1) verificar el correo con un código de 6 dígitos, (2) poner una
 * contraseña propia en lugar de la que dio el administrador y (3) configurar la verificación en dos pasos (se sigue en /mfa,
 * que enrola y entra). Todo con el challenge token del login; hasta terminar no hay sesión.
 */
export default function OnboardingPage() {
  const t = useT()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const next = safeNext(params.get('next'))
  const challenge = getMfaChallenge()
  const [state, setState] = useState<OnboardingStateDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState<{ email: string; devCode?: string | null } | null>(null)
  const [code, setCode] = useState('')
  const [pwd, setPwd] = useState('')
  const [pwd2, setPwd2] = useState('')

  useEffect(() => {
    if (!challenge) return
    getOnboarding()
      .then(setState)
      .catch((err: unknown) => setError(applyProblemDetails(err).title))
    // solo al entrar: el challenge se renueva al cambiar la contraseña sin volver a cargar
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const mfaNext = `/mfa?next=${encodeURIComponent(next)}`
  // Correo y contraseña listos: lo que falta es el MFA, que /mfa enrola y con eso entra.
  const done = state && state.emailVerified && !state.passwordChangeRequired
  useEffect(() => {
    if (done) navigate(mfaNext, { replace: true })
  }, [done, mfaNext, navigate])

  if (!challenge) return <Navigate to="/login" replace />

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    try {
      await action()
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(false)
    }
  }

  const onSend = () =>
    void run(async () => {
      const r = await sendOnboardingEmail()
      setSent({ email: r.email, devCode: r.devCode })
    })

  const onVerify = (e: FormEvent) => {
    e.preventDefault()
    void run(async () => {
      setState(await verifyOnboardingEmail(code.trim()))
      setCode('')
    })
  }

  const pwdError =
    pwd.length > 0 && pwd.length < MIN_PASSWORD
      ? t('auth.onboarding.passwordShort', { min: MIN_PASSWORD })
      : pwd2.length > 0 && pwd !== pwd2
        ? t('auth.onboarding.passwordMismatch')
        : null

  const onPassword = (e: FormEvent) => {
    e.preventDefault()
    if (pwdError || pwd.length < MIN_PASSWORD || pwd !== pwd2) return
    void run(async () => {
      setState(await setOnboardingPassword(pwd))
    })
  }

  const onCancel = () => {
    cancelMfa()
    navigate('/login', { replace: true })
  }

  const alert = error ? (
    <div className="alert" role="alert">
      {error}
    </div>
  ) : null
  const back = (
    <div className="links">
      <button type="button" className="linkbtn" onClick={onCancel}>
        {t('auth.backToLogin')}
      </button>
    </div>
  )

  if (!state || done) {
    return (
      <AuthLayout title={t('auth.onboarding.title')} subtitle={t('common.loading')}>
        {alert}
        {back}
      </AuthLayout>
    )
  }

  const stepNo = !state.emailVerified ? 1 : 2
  const progress = <p className="subtle">{t('auth.onboarding.step', { n: stepNo, total: 3 })}</p>

  if (!state.emailVerified) {
    return (
      <AuthLayout title={t('auth.onboarding.emailTitle')} subtitle={t('auth.onboarding.emailSubtitle', { email: state.email })}>
        {progress}
        {alert}
        {!sent ? (
          <button type="button" className="btn flow block" onClick={onSend} disabled={busy}>
            {busy ? t('common.loading') : t('auth.onboarding.sendCode')}
          </button>
        ) : (
          <form onSubmit={onVerify} noValidate>
            <p className="subtle">{t('auth.onboarding.codeSent', { email: sent.email })}</p>
            {sent.devCode && (
              <p className="subtle" data-testid="onboarding-dev-code">
                {t('auth.onboarding.devCode', { code: sent.devCode })}
              </p>
            )}
            <div className="f">
              <label htmlFor="onb-code">{t('auth.onboarding.codeLabel')}</label>
              <input
                id="onb-code"
                inputMode="numeric"
                autoComplete="one-time-code"
                autoFocus
                maxLength={6}
                value={code}
                onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
              />
            </div>
            <button type="submit" className="btn flow block" disabled={busy || code.trim().length !== 6}>
              {t('auth.onboarding.verify')}
            </button>
            <div className="links">
              <button type="button" className="linkbtn" onClick={onSend} disabled={busy}>
                {t('auth.onboarding.resend')}
              </button>
            </div>
          </form>
        )}
        {back}
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title={t('auth.onboarding.passwordTitle')} subtitle={t('auth.onboarding.passwordSubtitle')}>
      {progress}
      {alert}
      <form onSubmit={onPassword} noValidate>
        <div className="f">
          <label htmlFor="onb-pwd">{t('auth.onboarding.newPassword')}</label>
          <PasswordInput id="onb-pwd" autoFocus value={pwd} onChange={setPwd} />
        </div>
        <div className="f">
          <label htmlFor="onb-pwd2">{t('auth.onboarding.confirmPassword')}</label>
          <PasswordInput id="onb-pwd2" value={pwd2} onChange={setPwd2} />
          {pwdError && <p className="ferr">{pwdError}</p>}
        </div>
        <button type="submit" className="btn flow block" disabled={busy || !!pwdError || pwd.length < MIN_PASSWORD || pwd !== pwd2}>
          {t('auth.onboarding.savePassword')}
        </button>
      </form>
      {back}
    </AuthLayout>
  )
}
