// /confirm-browser?token=… — el enlace del correo confirma la computadora desde la que se intentó entrar (Lote 100). El enlace es de un solo uso; después se vuelve a la pantalla de entrada.
import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api, unwrap } from '../../kernel/api/client'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { Spinner } from '../../kernel/ui'
import { AuthLayout } from './AuthLayout'

export default function ConfirmBrowserPage() {
  const t = useT()
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const [state, setState] = useState<{ kind: 'working' } | { kind: 'done' } | { kind: 'error'; message: string }>({ kind: 'working' })
  const sent = useRef(false)

  useEffect(() => {
    if (sent.current) return
    sent.current = true
    if (!token) {
      setState({ kind: 'error', message: t('auth.browser.confirmInvalid') })
      return
    }
    unwrap(api.POST('/api/v1/auth/confirm-browser', { body: { token } }))
      .then(() => setState({ kind: 'done' }))
      .catch((e) => setState({ kind: 'error', message: applyProblemDetails(e).title }))
  }, [token, t])

  return (
    <AuthLayout title={t('auth.browser.confirmTitle')}>
      {state.kind === 'working' && <Spinner block />}
      {state.kind === 'done' && (
        <>
          <p role="status">{t('auth.browser.confirmDone')}</p>
          <p className="subtle">{t('auth.browser.confirmNext')}</p>
          <Link className="btn flow block" to="/login">{t('auth.browser.goLogin')}</Link>
        </>
      )}
      {state.kind === 'error' && (
        <>
          <div className="alert" role="alert">{state.message}</div>
          <Link className="btn block" to="/login">{t('auth.browser.goLogin')}</Link>
        </>
      )}
    </AuthLayout>
  )
}
