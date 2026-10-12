// Diálogo de confirmación con comentario opcional (máx. 500): «Eliminar chofer» y «Cancelar viaje». Si la acción lanza (409, 422…), el
// mensaje del servidor se muestra EXACTO dentro del diálogo y no se cierra (como `ConfirmDialog`, que no tiene campo de texto).
import { useId, useState, type ReactNode } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal } from '../../kernel/ui'
import { TRANSITION_COMMENT_MAX } from '../../kernel/catalogs'

export interface CommentDialogProps {
  open: boolean
  title: string
  /** Qué va a pasar (puede ser una lista de efectos). */
  children: ReactNode
  confirmLabel: string
  /** Texto del campo (por defecto «Comentario (opcional)»). */
  commentLabel?: string
  tone?: 'flow' | 'danger'
  onConfirm: (comment: string) => Promise<unknown>
  onClose: () => void
}

export function CommentDialog({ open, title, children, confirmLabel, commentLabel, tone = 'danger', onConfirm, onClose }: CommentDialogProps) {
  const t = useT()
  const id = useId()
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const tooLong = comment.length > TRANSITION_COMMENT_MAX

  const close = () => {
    setError(null)
    setComment('')
    onClose()
  }

  const confirm = async () => {
    if (tooLong) return
    setBusy(true)
    setError(null)
    try {
      await onConfirm(comment.trim())
      setComment('')
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
            {t('drivers.back')}
          </button>
          <button type="button" className={tone === 'danger' ? 'btn danger solid' : 'btn flow'} onClick={confirm} disabled={busy || tooLong}>
            {busy ? t('common.loading') : confirmLabel}
          </button>
        </>
      }
    >
      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      <div>{children}</div>
      <div className="f" style={{ marginTop: 12 }}>
        <label htmlFor={id}>{commentLabel ?? t('drivers.commentLabel')}</label>
        <textarea id={id} rows={3} value={comment} onChange={(e) => setComment(e.target.value)} aria-invalid={tooLong || undefined} />
        {tooLong && (
          <p className="ferr" role="alert">
            {t('status.commentTooLong', { max: TRANSITION_COMMENT_MAX })}
          </p>
        )}
      </div>
    </Modal>
  )
}
