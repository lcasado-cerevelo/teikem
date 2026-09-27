import { useState, type ReactNode } from 'react'
import { applyProblemDetails } from '../api/problem'
import { useT } from '../i18n/useT'
import { Modal } from './Modal'

export interface ConfirmDialogProps {
  open: boolean
  title: ReactNode
  /** Qué va a pasar y qué no se puede deshacer. */
  message: ReactNode
  /** Texto del botón de confirmar (por defecto "Confirmar"). */
  confirmLabel?: string
  /** 'danger' para bajas/cancelaciones. */
  tone?: 'flow' | 'danger'
  /** Acción; si lanza (p. ej. 422 status_rule), el mensaje del servidor se muestra en el diálogo y no se cierra. */
  onConfirm: () => Promise<unknown> | void
  onClose: () => void
}

/** Confirmación de acciones destructivas o con guarda de estatus. Se cierra solo si `onConfirm` termina bien. */
export function ConfirmDialog({ open, title, message, confirmLabel, tone = 'flow', onConfirm, onClose }: ConfirmDialogProps) {
  const t = useT()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const close = () => {
    setError(null)
    onClose()
  }

  const confirm = async () => {
    setBusy(true)
    setError(null)
    try {
      await onConfirm()
      onClose()
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      size="sm"
      title={title}
      onClose={close}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={close} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className={tone === 'danger' ? 'btn danger solid' : 'btn flow'} onClick={confirm} disabled={busy}>
            {busy ? t('common.loading') : (confirmLabel ?? t('ui.confirm.ok'))}
          </button>
        </>
      }
    >
      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      <div>{message}</div>
    </Modal>
  )
}
