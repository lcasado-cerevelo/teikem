import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { useSession } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/problem'
import { login } from '../../kernel/auth/auth'
import { useT } from '../../kernel/i18n/useT'
import { IconEye, IconEyeOff } from '../../kernel/ui/icons'
import { AuthLayout } from './AuthLayout'
import { safeNext } from './next'

/** Paso 1 del login (POST /api/v1/auth/login): correo y contraseña. */
export default function LoginPage() {
  const t = useT()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const { isAuthenticated } = useSession()
  const [formError, setFormError] = useState<string | null>(null)
  const [showPassword, setShowPassword] = useState(false)
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
      if (outcome.status === 'ok') navigate(next, { replace: true })
      else navigate(`/mfa?next=${encodeURIComponent(next)}`)
    } catch (err) {
      setFormError(applyProblemDetails(err, form).title)
    }
  })

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
          <div className="pwd-field">
            <input
              id="login-password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              aria-invalid={errors.password ? true : undefined}
              {...form.register('password')}
            />
            <button
              type="button"
              className="iconbtn"
              aria-label={showPassword ? t('auth.fields.hidePassword') : t('auth.fields.showPassword')}
              onClick={() => setShowPassword((v) => !v)}
            >
              {showPassword ? <IconEyeOff /> : <IconEye />}
            </button>
          </div>
          {errors.password && <p className="ferr">{errors.password.message}</p>}
        </div>
        <button type="submit" className="btn flow block" disabled={isSubmitting}>
          {isSubmitting ? t('common.loading') : t('auth.login.submit')}
        </button>
      </form>
    </AuthLayout>
  )
}
