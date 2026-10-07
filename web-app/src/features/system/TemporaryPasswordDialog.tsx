// Contraseña temporal de un usuario (2026-10-07): el administrador la da en persona; vale 10 minutos y, al entrar con ella, la persona debe poner la suya.
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/client'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Modal } from '../../kernel/ui'
import { formatDateTime } from '../account/format'
import { copyCode } from './EnrollCodeModal'
import { useSetTemporaryPassword } from './api'

export interface TemporaryPasswordDialogProps {
  /** Usuario al que se le pone; null = cerrado. */
  user: { id?: number; fullName?: string | null } | null
  onClose: () => void
}

export function TemporaryPasswordDialog({ user, onClose }: TemporaryPasswordDialogProps) {
  const t = useT()
  const lang = useLang()
  const set = useSetTemporaryPassword()
  const [custom, setCustom] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<{ password: string; expiresAtUtc: string; validMinutes: number } | null>(null)

  const close = () => {
    setCustom('')
    setError(null)
    setResult(null)
    onClose()
  }

  async function submit() {
    if (!user?.id) return
    setError(null)
    try {
      const res = await set.mutateAsync({ id: user.id, password: custom })
      setResult({ password: res.password ?? '', expiresAtUtc: res.expiresAtUtc ?? '', validMinutes: res.validMinutes ?? 10 })
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }

  if (result) {
    return (
      <Modal
        open={user !== null}
        title={t('system.users.tempPw.resultTitle')}
        onClose={close}
        size="sm"
        dismissible={false}
        footer={
          <button type="button" className="btn flow" onClick={close}>
            {t('common.done')}
          </button>
        }
      >
        <p className="help">{t('system.users.tempPw.resultHint', { name: user?.fullName ?? '', minutes: result.validMinutes })}</p>
        <div className="secret" style={{ fontSize: 22, fontWeight: 700, letterSpacing: '.08em', textAlign: 'center' }} data-testid="temp-password-value">
          {result.password}
        </div>
        <p className="help">{t('system.users.tempPw.expiresAt', { time: formatDateTime(result.expiresAtUtc, lang) })}</p>
        <button type="button" className="btn sm" onClick={() => void copyCode(result.password, t('system.users.users.copied'), t('system.users.users.copyFailed'))}>
          {t('common.copy')}
        </button>
      </Modal>
    )
  }

  return (
    <Modal
      open={user !== null}
      title={t('system.users.tempPw.title')}
      onClose={close}
      size="sm"
      dismissible={!set.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={set.isPending} onClick={() => void submit()}>
            {set.isPending ? t('common.loading') : t('system.users.tempPw.apply')}
          </button>
        </>
      }
    >
      <p>{t('system.users.tempPw.body', { name: user?.fullName ?? '' })}</p>
      <ul className="help" style={{ paddingLeft: 18 }}>
        <li>{t('system.users.tempPw.rule1')}</li>
        <li>{t('system.users.tempPw.rule2')}</li>
        <li>{t('system.users.tempPw.rule3')}</li>
      </ul>
      <div className="f">
        <label htmlFor="temp-pw-custom">{t('system.users.tempPw.customLabel')}</label>
        <input id="temp-pw-custom" type="text" autoComplete="off" value={custom} onChange={(e) => setCustom(e.target.value)} />
        <p className="help">{t('system.users.tempPw.customHelp')}</p>
      </div>
      {error && (
        <div className="alert" role="alert">
          {error}
        </div>
      )}
    </Modal>
  )
}
