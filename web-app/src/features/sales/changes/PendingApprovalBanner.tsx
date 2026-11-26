// Lote 249 — el aviso de la ficha de la orden cuando hay una modificación PENDIENTE de segunda aprobación: qué se pidió, quién la registró, el cambio del total y las
// decisiones (aprobar, rechazar con nota, retirar). Mientras esté pendiente no se aplica nada.
import { useState } from 'react'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Spinner } from '../../../kernel/ui'
import { ChangeDecisionActions } from './ChangeDecisionActions'
import { ChangeDetailModal } from './ChangeDetailModal'
import { useOrderChange } from './changesApi'
import './changes.css'

export function PendingApprovalBanner({ orderPublicId, changePublicId }: { orderPublicId: string; changePublicId: string }) {
  const t = useT()
  const f = useFormat()
  const change = useOrderChange(orderPublicId, changePublicId)
  const [open, setOpen] = useState(false)
  if (change.isPending) return <Spinner block />
  if (change.isError) return <p className="ferr" role="alert">{applyProblemDetails(change.error).title}</p>
  const item = change.data.item
  if (!item || item.isPending !== true) return null
  return (
    <section className="soc-pending" role="status" aria-label={t('salesChanges.pending.title')}>
      <b>{t('salesChanges.pending.heading', { n: item.changeNo ?? 0 })}</b>
      <span className="help">
        {t('salesChanges.pending.by', { who: item.registeredByName ?? '—', when: f.dateTime(item.registeredAtUtc), authorized: item.authorizedByName ?? '—' })}
      </span>
      <span>
        {t('salesChanges.preview.totalBefore')}: <b>{f.money(item.totalBefore ?? 0)}</b> → {t('salesChanges.preview.totalAfter')}: <b>{f.money(item.totalAfter ?? 0)}</b> ({f.money(item.totalDelta ?? 0, { signed: true })})
      </span>
      <div className="soc-pending-acts">
        <button type="button" className="btn" onClick={() => setOpen(true)}>{t('salesChanges.pending.details')}</button>
      </div>
      <ChangeDecisionActions orderPublicId={orderPublicId} item={item} />
      {open && <ChangeDetailModal orderPublicId={orderPublicId} changePublicId={changePublicId} onClose={() => setOpen(false)} />}
    </section>
  )
}
