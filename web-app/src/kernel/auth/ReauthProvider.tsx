import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { api, applyProblemDetails, setStepUpHandler, unwrap } from '../api/client'
import { useT } from '../i18n/useT'
import { ReauthContext, type ReauthApi } from './reauthContext'
import { updateAccessToken } from './tokens'

interface Props {
  /** Si el usuario tiene MFA activo, el modal pide también el código. */
  mfaEnabled: boolean
  children: ReactNode
}

/** Modal de reautenticación (contraseña + código MFA si aplica) y registro en el cliente para el 403 aal2_required. */
export function ReauthProvider({ mfaEnabled, children }: Props) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const pending = useRef<{ promise: Promise<boolean>; resolve: (ok: boolean) => void } | null>(null)

  const reauth = useCallback((): Promise<boolean> => {
    if (pending.current) return pending.current.promise
    let resolve: (ok: boolean) => void = () => {}
    const promise = new Promise<boolean>((r) => {
      resolve = r
    })
    pending.current = { promise, resolve }
    setPassword('')
    setCode('')
    setError(null)
    setOpen(true)
    return promise
  }, [])

  const finish = useCallback((ok: boolean) => {
    pending.current?.resolve(ok)
    pending.current = null
    setOpen(false)
    setPassword('')
    setCode('')
  }, [])

  useEffect(() => {
    setStepUpHandler(reauth)
    return () => setStepUpHandler(null)
  }, [reauth])

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const result = await unwrap(
        api.POST('/api/v1/auth/reauth', { body: { password, mfaCode: mfaEnabled ? code : null } }),
      )
      if (result.accessToken) updateAccessToken(result.accessToken, result.accessExpiresAtUtc)
      finish(true)
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(false)
    }
  }

  const value = useMemo<ReauthApi>(() => ({ reauth }), [reauth])

  return (
    <ReauthContext.Provider value={value}>
      {children}
      {open && (
        <div className="scrim on reauth" role="presentation">
          <form className="pal" role="dialog" aria-modal="true" aria-labelledby="reauth-title" onSubmit={submit}>
            <div className="pi">
              <b id="reauth-title">{t('auth.reauth.title')}</b>
            </div>
            <div className="pb">
              <p className="subtle">{t('auth.reauth.help')}</p>
              <div className="f">
                <label htmlFor="reauth-password">{t('auth.fields.password')}</label>
                <input
                  id="reauth-password"
                  type="password"
                  autoComplete="current-password"
                  autoFocus
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </div>
              {mfaEnabled && (
                <div className="f">
                  <label htmlFor="reauth-code">{t('auth.fields.mfaCode')}</label>
                  <input
                    id="reauth-code"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    required
                    value={code}
                    onChange={(e) => setCode(e.target.value.trim())}
                  />
                </div>
              )}
              {error && (
                <p className="ferr" role="alert">
                  {error}
                </p>
              )}
            </div>
            <div className="ft">
              <button type="button" className="btn" onClick={() => finish(false)} disabled={busy}>
                {t('common.cancel')}
              </button>
              <button type="submit" className="btn flow" disabled={busy || !password}>
                {t('auth.reauth.submit')}
              </button>
            </div>
          </form>
        </div>
      )}
    </ReauthContext.Provider>
  )
}
