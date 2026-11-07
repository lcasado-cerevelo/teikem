// Lote 222: «Aprobar» y «Rechazar» de un cliente prospecto (el que dio de alta un vendedor), con sales.approve_client. Aprobar deja fijar límite de crédito, término y un comentario;
// rechazar exige comentario y cancela las órdenes que esperaban. Se pintan en la cabecera de la ficha solo mientras el cliente es PROSPECTO.
import { useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { useApproveClient, useRejectClient } from './clientSalesApi'
import { isProspect, parseAmount, type ClientDetail } from './clientRules'

function ApproveModal({ client, onClose }: { client: ClientDetail; onClose: () => void }) {
  const t = useT()
  const approve = useApproveClient(client.publicId ?? '')
  const { data: terms = [] } = useLookups('PaymentTerm')
  const [limit, setLimit] = useState(client.creditLimit != null ? String(client.creditLimit) : '')
  const [term, setTerm] = useState(client.paymentTerm ?? '')
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    setError(null)
    const credit = parseAmount(limit)
    if (credit !== null && Number.isNaN(credit)) return setError(t('clients.approval.limitInvalid'))
    try {
      await approve.mutateAsync({ creditLimit: credit, paymentTerm: term || null, comment: comment.trim() || null })
      toast.success(t('clients.approval.approved', { name: client.name ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('clients.approval.approveTitle', { name: client.name ?? '' })}
      onClose={onClose}
      dismissible={!approve.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={approve.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={approve.isPending}>{approve.isPending ? t('common.loading') : t('clients.approval.approve')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('clients.approval.approveHelp')}</p>
        <div className="r2">
          <div className="f">
            <label htmlFor="cl-ap-limit">{t('clients.fields.creditLimit')}</label>
            <input id="cl-ap-limit" inputMode="decimal" value={limit} onChange={(e) => setLimit(e.target.value)} />
          </div>
          <div className="f">
            <label htmlFor="cl-ap-term">{t('clients.fields.paymentTerm')}</label>
            <select id="cl-ap-term" value={term} onChange={(e) => setTerm(e.target.value)}>
              <option value="">{t('clients.fields.none')}</option>
              {terms.map((o) => (
                <option key={o.code} value={o.code}>{o.label}</option>
              ))}
            </select>
          </div>
        </div>
        <div className="f">
          <label htmlFor="cl-ap-comment">{t('clients.approval.comment')}</label>
          <textarea id="cl-ap-comment" rows={3} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

function RejectModal({ client, onClose }: { client: ClientDetail; onClose: () => void }) {
  const t = useT()
  const reject = useRejectClient(client.publicId ?? '')
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    setError(null)
    if (comment.trim() === '') return setError(t('clients.approval.commentRequired'))
    try {
      const r = await reject.mutateAsync(comment.trim())
      const n = r.cancelledOrders?.length ?? 0
      toast.success(n > 0 ? t('clients.approval.rejectedOrders', { name: client.name ?? '', n }) : t('clients.approval.rejected', { name: client.name ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('clients.approval.rejectTitle', { name: client.name ?? '' })}
      onClose={onClose}
      dismissible={!reject.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={reject.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn danger" onClick={() => void submit()} disabled={reject.isPending}>{reject.isPending ? t('common.loading') : t('clients.approval.reject')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('clients.approval.rejectHelp')}</p>
        <div className="f">
          <label htmlFor="cl-rj-comment">{t('clients.approval.rejectComment')}</label>
          <textarea id="cl-rj-comment" rows={3} maxLength={500} value={comment} aria-required onChange={(e) => setComment(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

/** Botones Aprobar / Rechazar (solo con sales.approve_client y un cliente prospecto). */
export function ClientApprovalActions({ client }: { client: ClientDetail }) {
  const t = useT()
  const [open, setOpen] = useState<'approve' | 'reject' | null>(null)
  if (!isProspect(client)) return null
  return (
    <Can perm="sales.approve_client">
      <button type="button" className="btn sm flow" onClick={() => setOpen('approve')}>{t('clients.approval.approve')}</button>
      <button type="button" className="btn sm danger" onClick={() => setOpen('reject')}>{t('clients.approval.reject')}</button>
      {open === 'approve' && <ApproveModal client={client} onClose={() => setOpen(null)} />}
      {open === 'reject' && <RejectModal client={client} onClose={() => setOpen(null)} />}
    </Can>
  )
}
