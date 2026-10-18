// Diálogo «El cliente excede su límite de crédito» con «Autorizar…»: lo comparten el alta (Guardar, F-C1) y el expediente
// (Marcar como recogida, F-C2). Solo se abre para quien tiene `orders.credit_override`; reenvía la misma acción con `overrideCredit=true`.
import { type ReactNode } from 'react'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog } from '../../kernel/ui'

export interface CreditOverrideDialogProps {
  open: boolean
  /** Texto del servidor (`title` del 422 `credit_exceeded`; con `orders.view_price` trae las cifras). */
  message: ReactNode
  /** Qué pasa si se autoriza (cambia según la acción: crear y confirmar, confirmar, asignar chofer). */
  note: string
  /** Botón de autorizar (por defecto «Autorizar y confirmar»). */
  confirmLabel?: string
  onAuthorize: () => Promise<unknown> | void
  onClose: () => void
}

export function CreditOverrideDialog({ open, message, note, confirmLabel, onAuthorize, onClose }: CreditOverrideDialogProps) {
  const t = useT()
  return (
    <ConfirmDialog
      open={open}
      title={t('orders.entry.credit.title')}
      message={
        <>
          <p>{message}</p>
          <p className="help">{note}</p>
        </>
      }
      confirmLabel={confirmLabel ?? t('orders.entry.credit.authorize')}
      onConfirm={onAuthorize}
      onClose={onClose}
    />
  )
}
