import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { useSession } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/problem'
import { login, type BrowserConfirmationDto } from '../../kernel/auth/auth'
import { useT } from '../../kernel/i18n/useT'
import { PasswordBox } from '../../kernel/ui/PasswordBox'
import { AuthLayout } from './AuthLayout'
import { loginErrorText } from './loginErrors'
import { safeNext } from './next'

/** Paso 1 del login (POST /api/v1/auth/login): correo y contraseña. */
export default function LoginPage() {
  const t = useT()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const { isAuthenticated } = useSession()
  const [formError, setFormError] = useState<string | null>(null)
  // Lote 100: la computadora no se reconoce; se muestra «revise su correo» en lugar del formulario
  const [confirm, setConfirm] = useState<BrowserConfirmationDto | null>(null)
  const next = safeNext(params.get('next'))

  const schema = useMemo(
    () =>
      z.object({
        email: z.string().trim().pipe(z.email(t('auth.errors.emailInvalid'))),
        password: z.string().min(1, t('auth.errors.passwordRequired')),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { email: '', password: '' } })
  const { errors, isSubmitting } = form.formState

  if (isAuthenticated) return <Navigate to={next} replace />

  const onSubmit = form.handleSubmit(async ({ email, password }) => {
    setFormError(null)
    try {
      const outcome = await login(email, password)
      if (outcome.status === 'browser_confirmation_required') setConfirm(outcome.browser)
      else if (outcome.status === 'ok') navigate(next, { replace: true })
      else if (outcome.status === 'onboarding_required') navigate(`/onboarding?next=${encodeURIComponent(next)}`)
      else navigate(`/mfa?next=${encodeURIComponent(next)}`)
    } catch (err) {
      setFormError(loginErrorText(applyProblemDetails(err, form), t))
    }
  })

  if (confirm) {
    return (
      <AuthLayout title={t('auth.browser.title')} subtitle={confirm.sent ? t('auth.browser.sentTo', { email: confirm.email ?? '' }) : undefined}>
        {confirm.sent ? <p>{t('auth.browser.sentHelp')}</p> : <div className="alert" role="alert">{confirm.message ?? t('auth.browser.notSent')}</div>}
        {confirm.devLink && <p className="subtle"><a href={confirm.devLink}>{t('auth.browser.devLink')}</a></p>}
        <p className="subtle">{t('auth.browser.afterConfirm')}</p>
        <button type="button" className="btn flow block" onClick={() => setConfirm(null)}>{t('auth.browser.back')}</button>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title={t('auth.login.title')} subtitle={t('auth.login.subtitle')}>
      <form onSubmit={onSubmit} noValidate>
        {formError && (
          <div className="alert" role="alert">
            {formError}
          </div>
        )}
        <div className="f">
          <label htmlFor="login-email">{t('auth.fields.email')}</label>
          <input
            id="login-email"
            type="email"
            autoComplete="username"
            inputMode="email"
            autoFocus
            aria-invalid={errors.email ? true : undefined}
            {...form.register('email')}
          />
          {errors.email && <p className="ferr">{errors.email.message}</p>}
        </div>
        <div className="f">
          <label htmlFor="login-password">{t('auth.fields.password')}</label>
          <PasswordBox id="login-password" autoComplete="current-password" aria-invalid={errors.password ? true : undefined} {...form.register('password')} />
          {errors.password && <p className="ferr">{errors.password.message}</p>}
        </div>
        <button type="submit" className="btn flow block" disabled={isSubmitting}>
          {isSubmitting ? t('common.loading') : t('auth.login.submit')}
        </button>
        <p style={{ textAlign: 'center', marginTop: 12 }}>
          <Link to="/forgot-password">{t('auth.login.forgot')}</Link>
        </p>
      </form>
    </AuthLayout>
  )
}
