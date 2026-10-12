// Diálogos de acciones del expediente de la orden (Lote F-C2): «Cancelar la orden» (comentario opcional, máx. 500) y «Asignar chofer»
// (solo entrega especial: chofer activo, comentario opcional y, si el servidor avisa de crédito excedido, «Autorizar y asignar» para
// quien tiene `orders.credit_override`). Los errores del servidor se muestran EXACTOS dentro del diálogo, sin cerrarlo.
import { useId, useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { ComboSelect, Modal, toast, type ComboOption } from '../../kernel/ui'
import { useAssignOrderDriver, useCancelOrder, useDrivers, type OrderDetailDto } from './api'
import { COMMENT_MAX, isCreditExceeded } from './orderRules'

interface DialogProps {
  order: OrderDetailDto
  open: boolean
  onClose: () => void
}

/** Comentario opcional con contador (máx. 500, igual que el historial de estatus). */
function CommentField({ id, value, onChange }: { id: string; value: string; onChange: (v: string) => void }) {
  const t = useT()
  const tooLong = value.length > COMMENT_MAX
  return (
    <div className="f">
      <label htmlFor={id}>{t('status.comment')}</label>
      <textarea id={id} rows={3} value={value} aria-invalid={tooLong || undefined} onChange={(e) => onChange(e.target.value)} />
      <div className="stpipe-count">
        {value.length}/{COMMENT_MAX}
      </div>
      {tooLong && <p className="ferr">{t('status.commentTooLong', { max: COMMENT_MAX })}</p>}
    </div>
  )
}

/** «Cancelar la orden»: pide un comentario opcional y llama a `POST /cancel`. */
export function CancelOrderDialog({ order, open, onClose }: DialogProps) {
  const t = useT()
  const cancel = useCancelOrder()
  const commentId = useId()
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const tooLong = comment.length > COMMENT_MAX

  async function submit() {
    if (tooLong || cancel.isPending) return
    setError(null)
    try {
      await cancel.mutateAsync({ publicId: order.publicId ?? '', comment: comment.trim() || undefined, rowVersion: order.rowVersion })
      toast.success(t('orders.actions.cancel.done', { number: order.orderNumber ?? '' }))
      onClose()
    } catch (err) {
      const p = applyProblemDetails(err)
      setError(p.errors.comment?.join(' ') ?? p.title)
    }
  }

  return (
    <Modal
      open={open}
      size="sm"
      title={t('orders.actions.cancel.title', { number: order.orderNumber ?? '' })}
      onClose={onClose}
      dismissible={!cancel.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={cancel.isPending}>
            {t('orders.actions.back')}
          </button>
          <button type="button" className="btn danger solid" onClick={() => void submit()} disabled={cancel.isPending || tooLong}>
            {cancel.isPending ? t('common.loading') : t('orders.actions.cancel.confirm')}
          </button>
        </>
      }
    >
      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      <p>{t('orders.actions.cancel.body')}</p>
      <CommentField id={commentId} value={comment} onChange={setComment} />
    </Modal>
  )
}

/** «Asignar chofer» / «Reasignar chofer» de una entrega especial (`POST /orders/{id}/driver`). */
export function AssignDriverModal({ order, open, onClose }: DialogProps) {
  const t = useT()
  const canFleet = useCan('fleet.view')
  const canOverride = useCan('orders.credit_override')
  const drivers = useDrivers(canFleet && open)
  const assign = useAssignOrderDriver()
  const driverId = useId()
  const commentId = useId()
  const [driver, setDriver] = useState(order.assignedDriverPublicId ?? '')
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [driverError, setDriverError] = useState<string | null>(null)
  /** El servidor avisó de crédito excedido y el usuario puede autorizarlo: el siguiente envío lleva `overrideCredit`. */
  const [authorize, setAuthorize] = useState(false)
  const tooLong = comment.length > COMMENT_MAX
  const reassign = !!order.assignedDriverPublicId

  const options = useMemo<ComboOption[]>(
    () =>
      (drivers.data ?? [])
        .filter((d) => d.isActive !== false && d.publicId)
        .map((d) => ({ value: d.publicId ?? '', label: `${d.code ?? ''} · ${d.fullName ?? ''}`.replace(/^ · /, ''), hint: [d.zoneCode, d.status].filter(Boolean).join(' · ') })),
    [drivers.data],
  )

  async function submit() {
    if (tooLong || assign.isPending) return
    if (!driver) {
      setDriverError(t('orders.actions.assign.driverRequired'))
      return
    }
    setDriverError(null)
    setError(null)
    try {
      await assign.mutateAsync({ publicId: order.publicId ?? '', driverPublicId: driver, comment: comment.trim() || undefined, overrideCredit: authorize, rowVersion: order.rowVersion })
      toast.success(t('orders.actions.assign.done', { number: order.orderNumber ?? '' }))
      onClose()
    } catch (err) {
      const p = applyProblemDetails(err)
      if (isCreditExceeded(err) && canOverride) {
        setAuthorize(true)
        setError(`${p.title} ${t('orders.actions.assign.creditNote')}`)
      } else {
        setError(p.errors.comment?.join(' ') ?? p.errors.driverPublicId?.join(' ') ?? p.title)
      }
    }
  }

  const title = t(reassign ? 'orders.actions.assign.reassignTitle' : 'orders.actions.assign.title', { number: order.orderNumber ?? '' })
  const confirmLabel = authorize ? t('orders.actions.assign.authorize') : t(reassign ? 'orders.actions.assign.reassign' : 'orders.actions.assign.confirm')

  return (
    <Modal
      open={open}
      size="sm"
      title={title}
      onClose={onClose}
      dismissible={!assign.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={assign.isPending}>
            {t('orders.actions.back')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={assign.isPending || tooLong || !canFleet}>
            {assign.isPending ? t('common.loading') : confirmLabel}
          </button>
        </>
      }
    >
      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      <p className="d">{t('orders.actions.assign.hint')}</p>
      <div className="f">
        <label htmlFor={driverId}>{t('orders.actions.assign.driver')}</label>
        {canFleet ? (
          <ComboSelect
            id={driverId}
            options={options}
            value={driver}
            onChange={(v) => {
              setDriver(v ?? '')
              setDriverError(null)
            }}
            loading={drivers.isLoading}
            invalid={!!driverError}
            placeholder={drivers.isError ? t('orders.actions.assign.loadFailed') : options.length === 0 && !drivers.isLoading ? t('orders.actions.assign.noDrivers') : t('orders.actions.assign.chooseDriver')}
          />
        ) : (
          <p className="note" role="status">
            {t('orders.actions.assign.noFleetAccess')}
          </p>
        )}
        {driverError && <p className="ferr">{driverError}</p>}
      </div>
      <CommentField id={commentId} value={comment} onChange={setComment} />
    </Modal>
  )
}
