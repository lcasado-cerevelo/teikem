// Asignar/restablecer/quitar el PIN de otro usuario (Lote 8A, sección 3.2 del manual 08). Solo `PUT /api/v1/users/
// {id}/pin` lleva [RequireAal2] (`DELETE` no): el cliente del API pide reautenticación sola si el servidor responde
// 403 `aal2_required`, y reintenta — no hace falta pedirla aquí (ni para el PUT ni, sobre todo, para el DELETE, que
// el servidor nunca la exige).
// El formato y "secuencia trivial" los valida el servidor (mensajes exactos del manual 08); aquí solo se valida en
// cliente que las dos capturas del PIN coincidan.
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { ConfirmDialog, Modal, toast } from '../../kernel/ui'
import { useRemoveUserPin, useSetUserPin, type UserSummaryDto } from './api'

export function PinModal({ open, user, onClose }: { open: boolean; user: UserSummaryDto | null; onClose: () => void }) {
  const t = useT()
  const setPin = useSetUserPin()
  const removePin = useRemoveUserPin()
  const [pin, setPinValue] = useState('')
  const [confirmPin, setConfirmPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [removing, setRemoving] = useState(false)

  const close = () => {
    setPinValue('')
    setConfirmPin('')
    setError(null)
    onClose()
  }

  if (!user) return null
  const title = user.hasPin ? t('system.users.pin.resetTitle', { name: user.fullName ?? '' }) : t('system.users.pin.assignTitle', { name: user.fullName ?? '' })

  const submit = async () => {
    setError(null)
    if (pin !== confirmPin) {
      setError(t('system.users.pin.mismatch'))
      return
    }
    setBusy(true)
    try {
      await setPin.mutateAsync({ id: user.id ?? 0, pin })
      toast.success(t('system.users.pin.saved'))
      close()
    } catch (err) {
      const problem = applyProblemDetails(err)
      setError(problem.errors.pin?.[0] ?? problem.title)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Modal
        open={open}
        title={title}
        onClose={close}
        dismissible={!busy}
        footer={
          <>
            <button type="button" className="btn" onClick={close} disabled={busy}>
              {t('system.users.pin.cancel')}
            </button>
            <button type="button" className="btn flow" onClick={() => void submit()} disabled={busy}>
              {busy ? t('common.loading') : t('system.users.pin.save')}
            </button>
          </>
        }
      >
        <div className="f">
          <label htmlFor="pinmodal-pin">{t('system.users.pin.pin')}</label>
          <input
            id="pinmodal-pin"
            type="password"
            // new-password: el navegador no rellena ni propone la contraseña guardada en un campo que NO es una contraseña (es el PIN de otra persona)
            autoComplete="new-password"
            autoFocus
            inputMode="numeric"
            maxLength={6}
            value={pin}
            onChange={(e) => setPinValue(e.target.value)}
          />
        </div>
        <div className="f">
          <label htmlFor="pinmodal-confirm">{t('system.users.pin.confirmPin')}</label>
          <input
            id="pinmodal-confirm"
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
        {user.hasPin && (
          <p style={{ marginTop: 12 }}>
            <button type="button" className="btn danger sm" onClick={() => setRemoving(true)}>
              {t('system.users.users.removePin')}
            </button>
          </p>
        )}
      </Modal>

      <ConfirmDialog
        open={removing}
        tone="danger"
        title={t('system.users.pin.removeTitle')}
        message={t('system.users.pin.removeBody', { name: user.fullName ?? '' })}
        onConfirm={async () => {
          await removePin.mutateAsync(user.id ?? 0)
          toast.success(t('system.users.pin.removed'))
          setRemoving(false)
          close()
        }}
        onClose={() => setRemoving(false)}
      />
    </>
  )
}
