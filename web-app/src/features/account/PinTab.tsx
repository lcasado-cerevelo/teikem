// Pestaña "PIN de la app" de Mi cuenta (Lote 8A): el PIN corto para entrar en Teikem Almacén sin contraseña. Solo
// se monta con el módulo WMS_LOTSERIAL encendido (lo decide `AccountPage`). `GET/PUT/DELETE /api/v1/me/pin`.
import { useState } from 'react'
import { applyProblemDetails, ApiError } from '../../kernel/api/client'
import { useLang, useT } from '../../kernel/i18n/useT'
import { ConfirmDialog, EmptyState, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { formatDateTime } from './format'
import { useMyPin, useRemoveMyPin, useSetMyPin } from './api'
import { IconKey } from '../../kernel/ui/actionIcons'

function SetPinModal({ open, hasPin, onClose }: { open: boolean; hasPin: boolean; onClose: () => void }) {
  const t = useT()
  const setPin = useSetMyPin()
  const [currentPassword, setCurrentPassword] = useState('')
  const [pin, setPin1] = useState('')
  const [confirmPin, setConfirmPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const close = () => {
    setCurrentPassword('')
    setPin1('')
    setConfirmPin('')
    setError(null)
    setPasswordError(null)
    onClose()
  }

  const submit = async () => {
    setError(null)
    setPasswordError(null)
    if (pin !== confirmPin) {
      setError(t('account.pin.mismatch'))
      return
    }
    setBusy(true)
    try {
      await setPin.mutateAsync({ currentPassword, pin })
      toast.success(t('account.pin.saved'))
      close()
    } catch (err) {
      const problem = applyProblemDetails(err)
      setPasswordError(problem.errors.currentPassword?.[0] ?? null)
      setError(problem.errors.pin?.[0] ?? (problem.errors.currentPassword ? null : problem.title))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      title={hasPin ? t('account.pin.modalChangeTitle') : t('account.pin.modalSetTitle')}
      onClose={close}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={close} disabled={busy}>
            {t('account.pin.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={busy}>
            {busy ? t('common.loading') : t('account.pin.save')}
          </button>
        </>
      }
    >
      <div className="f">
        <label htmlFor="pintab-current">{t('account.pin.currentPassword')}</label>
        <input
          id="pintab-current"
          type="password"
          autoComplete="current-password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
        />
        {passwordError && (
          <p className="ferr" role="alert">
            {passwordError}
          </p>
        )}
      </div>
      <div className="f">
        <label htmlFor="pintab-pin">{t('account.pin.pin')}</label>
        <input
          id="pintab-pin"
          type="password"
          autoComplete="new-password"
          inputMode="numeric"
          maxLength={6}
          value={pin}
          onChange={(e) => setPin1(e.target.value)}
        />
      </div>
      <div className="f">
        <label htmlFor="pintab-confirm">{t('account.pin.confirmPin')}</label>
        <input
          id="pintab-confirm"
          type="password"
          autoComplete="new-password"
          inputMode="numeric"
          maxLength={6}
          value={confirmPin}
          onChange={(e) => setConfirmPin(e.target.value)}
        />
      </div>
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}

export function PinTab() {
  const t = useT()
  const lang = useLang()
  const { data, isLoading, error } = useMyPin()
  const removePin = useRemoveMyPin()
  const [setting, setSetting] = useState(false)
  const [removing, setRemoving] = useState(false)

  if (error) return <EmptyState title={error instanceof ApiError ? error.title : t('errors.generic')} />
  if (isLoading || !data) return <Spinner block />

  return (
    <Panel icon={<IconKey />} title={t('account.pin.title')} subtitle={t('account.pin.subtitle')} className="acct-narrow">
      <p>{data.hasPin ? t('account.pin.hasPinYes') : t('account.pin.hasPinNo')}</p>
      {data.hasPin && <p className="subtle">{t('account.pin.updated', { date: formatDateTime(data.updatedAtUtc, lang) })}</p>}
      <p className="subtle">
        {data.lockedUntilUtc ? t('account.pin.lockedUntil', { date: formatDateTime(data.lockedUntilUtc, lang) }) : t('account.pin.notLocked')}
      </p>
      <div className="form-acts">
        <button type="button" className="btn" onClick={() => setSetting(true)}>
          {data.hasPin ? t('account.pin.change') : t('account.pin.set')}
        </button>
        {data.hasPin && (
          <button type="button" className="btn danger" onClick={() => setRemoving(true)}>
            {t('account.pin.remove')}
          </button>
        )}
      </div>

      <SetPinModal open={setting} hasPin={data.hasPin ?? false} onClose={() => setSetting(false)} />

      <ConfirmDialog
        open={removing}
        tone="danger"
        title={t('account.pin.removeTitle')}
        message={t('account.pin.removeBody')}
        confirmLabel={t('account.pin.remove')}
        onConfirm={async () => {
          await removePin.mutateAsync()
          toast.success(t('account.pin.removed'))
        }}
        onClose={() => setRemoving(false)}
      />
    </Panel>
  )
}
