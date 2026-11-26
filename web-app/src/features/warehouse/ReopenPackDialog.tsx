// Lote 249 — «Reabrir el empaque» (warehouse.reopen-pack): el envío de la recolección se da de baja y la recolección vuelve a «Recolectada», para poder cambiar la orden de venta
// o sumarle una adición. Exige el motivo. Sirve para una recolección o para su maestra (`group`). Lo que ya salió o ya se facturó no se reabre (el servidor lo dice).
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { useReopenPack } from './api'

export function ReopenPackDialog({ publicId, number, group, rowVersion, onClose }: { publicId: string; number: string; group?: boolean; rowVersion?: string | null; onClose: () => void }) {
  const t = useT()
  const reopen = useReopenPack()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const submit = async () => {
    setError(null)
    if (reason.trim() === '') return setError(t('warehouse.reopenPack.reasonRequired'))
    try {
      await reopen.mutateAsync({ publicId, group, body: { reason: reason.trim(), rowVersion: rowVersion ?? undefined } })
      toast.success(t('warehouse.reopenPack.done', { number }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="sm"
      title={t('warehouse.reopenPack.title', { number })}
      onClose={onClose}
      dismissible={!reopen.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={reopen.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={reopen.isPending}>{reopen.isPending ? t('common.loading') : t('warehouse.reopenPack.confirm')}</button>
        </>
      }
    >
      <div className="pb">
        <p>{t(group ? 'warehouse.reopenPack.bodyGroup' : 'warehouse.reopenPack.body')}</p>
        <div className="f">
          <label htmlFor="rp-reason">{t('warehouse.reopenPack.reason')}</label>
          <textarea id="rp-reason" rows={3} maxLength={500} value={reason} aria-required="true" onChange={(e) => setReason(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
