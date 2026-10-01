import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import {
  cancelMfa,
  confirmMfaWithChallenge,
  enrollMfaWithChallenge,
  verifyMfa,
  type MfaEnrollResultDto,
} from '../../kernel/auth/auth'
import { getMfaChallenge } from '../../kernel/auth/tokens'
import { useT } from '../../kernel/i18n/useT'
import { QrCode } from '../../kernel/ui/QrCode'
import { RecoveryCodes } from '../../kernel/ui/RecoveryCodes'
import { AuthLayout } from './AuthLayout'
import { safeNext } from './next'

type Step = 'verify' | 'enroll-start' | 'enroll-confirm' | 'recovery'

/**
 * Paso 2 del login con el challenge token: código TOTP o de recuperación (POST /api/v1/auth/mfa/verify).
 * Si el tenant exige MFA y el usuario no lo tiene, primero enrola (enroll → confirm → códigos de recuperación).
 */
export default function MfaPage() {
  const t = useT()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const challenge = getMfaChallenge()
  const next = safeNext(params.get('next'))
  const [step, setStep] = useState<Step>(challenge?.enrollmentRequired ? 'enroll-start' : 'verify')
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [enroll, setEnroll] = useState<MfaEnrollResultDto | null>(null)
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([])
  const [saved, setSaved] = useState(false)

  if (!challenge && step !== 'recovery') return <Navigate to="/login" replace />

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

  const onVerify = (e: FormEvent) => {
    e.preventDefault()
    void run(async () => {
      const outcome = await verifyMfa(code.trim())
      if (outcome.status === 'ok') navigate(next, { replace: true })
    })
  }

  const onStartEnroll = () =>
    void run(async () => {
      setEnroll(await enrollMfaWithChallenge())
      setStep('enroll-confirm')
    })

  const onConfirm = (e: FormEvent) => {
    e.preventDefault()
    void run(async () => {
      setRecoveryCodes(await confirmMfaWithChallenge(code.trim()))
      setCode('')
      setStep('recovery')
    })
  }

  const onCancel = () => {
    cancelMfa()
    navigate('/login', { replace: true })
  }

  const alert = error && (
    <div className="alert" role="alert">
      {error}
    </div>
  )

  const codeField = (
    <div className="f">
      <label htmlFor="mfa-code">{t('auth.fields.mfaCode')}</label>
      <input
        id="mfa-code"
        inputMode="numeric"
        autoComplete="one-time-code"
        autoFocus
        maxLength={32}
        value={code}
        onChange={(e) => setCode(e.target.value)}
      />
    </div>
  )

  const cancelLink = (
    <div className="links">
      <button type="button" className="linkbtn" onClick={onCancel}>
        {t('auth.backToLogin')}
      </button>
    </div>
  )

  if (step === 'enroll-start') {
    return (
      <AuthLayout title={t('auth.mfa.enrollTitle')} subtitle={t('auth.mfa.enrollSubtitle')}>
        {alert}
        <button type="button" className="btn flow block" disabled={busy} onClick={onStartEnroll}>
          {t('auth.mfa.enrollStart')}
        </button>
        {cancelLink}
      </AuthLayout>
    )
  }

  if (step === 'enroll-confirm' && enroll) {
    return (
      <AuthLayout title={t('auth.mfa.enrollTitle')} subtitle={t('auth.mfa.enrollScan')}>
        {alert}
        {enroll.otpAuthUri && (
          <div className="qr-box">
            <QrCode value={enroll.otpAuthUri} label={t('auth.mfa.qrAlt')} />
            <p className="subtle">{t('auth.mfa.qrHelp')}</p>
          </div>
        )}
        <p className="subtle">{t('auth.mfa.secretLabel')}</p>
        <div className="secret" data-testid="mfa-secret">
          {enroll.secret}
        </div>
        {enroll.otpAuthUri && (
          <p className="subtle">
            <a href={enroll.otpAuthUri}>{t('auth.mfa.openInApp')}</a>
          </p>
        )}
        <form onSubmit={onConfirm} noValidate>
          {codeField}
          <button type="submit" className="btn flow block" disabled={busy || !code.trim()}>
            {t('auth.mfa.confirm')}
          </button>
        </form>
        {cancelLink}
      </AuthLayout>
    )
  }

  if (step === 'recovery') {
    return (
      <AuthLayout title={t('auth.mfa.recoveryTitle')} subtitle={t('auth.mfa.recoverySubtitle')}>
        <RecoveryCodes codes={recoveryCodes} saved={saved} onSavedChange={setSaved} />
        <p className="subtle" style={{ marginTop: 14 }}>
          {t('auth.mfa.recoveryNext')}
        </p>
        <form onSubmit={onVerify} noValidate>
          {alert}
          {codeField}
          <button type="submit" className="btn flow block" disabled={busy || !code.trim() || !saved}>
            {t('auth.mfa.verify')}
          </button>
        </form>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title={t('auth.mfa.title')} subtitle={t('auth.mfa.subtitle')}>
      <form onSubmit={onVerify} noValidate>
        {alert}
        {codeField}
        <button type="submit" className="btn flow block" disabled={busy || !code.trim()}>
          {t('auth.mfa.verify')}
        </button>
      </form>
      {cancelLink}
    </AuthLayout>
  )
}
