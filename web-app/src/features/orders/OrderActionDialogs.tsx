// Diálogos de acciones del expediente de la orden (Lote F-C2): «Cancelar la orden» y «Reabrir» (razón obligatoria, máx. 500) y «Asignar chofer»
// (solo entrega especial: chofer activo, comentario opcional y, si el servidor avisa de crédito excedido, «Autorizar y asignar» para
// quien tiene `orders.credit_override`). Los errores del servidor se muestran EXACTOS dentro del diálogo, sin cerrarlo.
import { useId, useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { ComboSelect, Modal, toast, type ComboOption } from '../../kernel/ui'
import { useDrivers } from '../drivers/api'
import { useAssignOrderDriver, useCancelOrder, useReopenOrder, useReturnOrder, type OrderDetailDto } from './api'
import { COMMENT_MAX, isCreditExceeded } from './orderRules'

interface DialogProps {
  order: OrderDetailDto
  open: boolean
  onClose: () => void
}

/** Comentario opcional con contador (máx. 500, igual que el historial de estatus). */
function CommentField({
  id,
  value,
  onChange,
  label,
  placeholder,
  error,
}: {
  id: string
  value: string
  onChange: (v: string) => void
  /** Rótulo propio (razón obligatoria); sin él, «Comentario». */
  label?: string
  placeholder?: string
  /** Error de validación a mostrar (razón vacía). */
  error?: string | null
}) {
  const t = useT()
  const tooLong = value.length > COMMENT_MAX
  return (
    <div className="f">
      <label htmlFor={id}>{label ?? t('status.comment')}</label>
      <textarea
        id={id}
        rows={3}
        value={value}
        placeholder={placeholder}
        aria-invalid={tooLong || !!error || undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      <div className="stpipe-count">
        {value.length}/{COMMENT_MAX}
      </div>
      {tooLong && <p className="ferr">{t('status.commentTooLong', { max: COMMENT_MAX })}</p>}
      {error && !tooLong && <p className="ferr">{error}</p>}
    </div>
  )
}

/** «Cancelar la orden»: pide la razón (obligatoria, máx. 500) y llama a `POST /cancel` con `comment`. */
export function CancelOrderDialog({ order, open, onClose }: DialogProps) {
  const t = useT()
  const cancel = useCancelOrder()
  const commentId = useId()
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [reasonError, setReasonError] = useState<string | null>(null)
  const tooLong = comment.length > COMMENT_MAX

  async function submit() {
    if (tooLong || cancel.isPending) return
    if (!comment.trim()) {
      setReasonError(t('orders.actions.cancel.reasonRequired'))
      return
    }
    setReasonError(null)
    setError(null)
    try {
      await cancel.mutateAsync({ publicId: order.publicId ?? '', comment: comment.trim(), rowVersion: order.rowVersion })
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
      <CommentField
        id={commentId}
        value={comment}
        onChange={(v) => {
          setComment(v)
          setReasonError(null)
        }}
        label={t('orders.actions.cancel.reason')}
        placeholder={t('orders.actions.cancel.reasonPlaceholder')}
        error={reasonError}
      />
    </Modal>
  )
}

/** «Reabrir»: razón obligatoria (máx. 500) y, opcional, estatus destino (por omisión, el último en que estuvo). `POST /reopen`. */
export function ReopenOrderDialog({ order, open, onClose }: DialogProps) {
  const t = useT()
  const reopen = useReopenOrder()
  const { data: statuses = [] } = useStatuses('OrderStatus')
  const commentId = useId()
  const toId = useId()
  const [comment, setComment] = useState('')
  const [toCode, setToCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [reasonError, setReasonError] = useState<string | null>(null)
  const tooLong = comment.length > COMMENT_MAX
  const options = statuses.filter((s) => s.isEnabled !== false && s.stageKind !== 'TERMINAL' && s.code.toLowerCase() !== (order.status ?? '').toLowerCase())

  async function submit() {
    if (tooLong || reopen.isPending) return
    if (!comment.trim()) {
      setReasonError(t('orders.actions.reopen.reasonRequired'))
      return
    }
    setReasonError(null)
    setError(null)
    try {
      await reopen.mutateAsync({ publicId: order.publicId ?? '', comment: comment.trim(), toCode: toCode || null, rowVersion: order.rowVersion })
      toast.success(t('orders.actions.reopen.done', { number: order.orderNumber ?? '' }))
      onClose()
    } catch (err) {
      const p = applyProblemDetails(err)
      setError(p.errors.comment?.join(' ') ?? p.errors.toCode?.join(' ') ?? p.title)
    }
  }

  return (
    <Modal
      open={open}
      size="sm"
      title={t('orders.actions.reopen.title', { number: order.orderNumber ?? '' })}
      onClose={onClose}
      dismissible={!reopen.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={reopen.isPending}>
            {t('orders.actions.back')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={reopen.isPending || tooLong}>
            {reopen.isPending ? t('common.loading') : t('orders.actions.reopen.confirm')}
          </button>
        </>
      }
    >
      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      <p>{t('orders.actions.reopen.body')}</p>
      <div className="f">
        <label htmlFor={toId}>{t('orders.actions.reopen.toStatus')}</label>
        <select id={toId} value={toCode} onChange={(e) => setToCode(e.target.value)}>
          <option value="">{t('orders.actions.reopen.lastStatus')}</option>
          {options.map((s) => (
            <option key={s.code} value={s.code}>
              {s.label}
            </option>
          ))}
        </select>
      </div>
      <CommentField
        id={commentId}
        value={comment}
        onChange={(v) => {
          setComment(v)
          setReasonError(null)
        }}
        label={t('orders.actions.reopen.reason')}
        placeholder={t('orders.actions.reopen.reasonPlaceholder')}
        error={reasonError}
      />
    </Modal>
  )
}

/** «Devolver» (Lote 112, `orders.return`): elige las piezas que viajan en la orden de vuelta (por omisión todas) y una nota. La orden queda «Devuelta» y se crea la orden de vuelta (nace Sometida). `POST /return`. */
export function ReturnOrderDialog({ order, open, onClose }: DialogProps) {
  const t = useT()
  const returnOrder = useReturnOrder()
  const notesId = useId()
  const pieces = (order.packages ?? []).flatMap((p) => p.pieceNumbers ?? [])
  const [chosen, setChosen] = useState<string[]>(pieces)
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const toggle = (n: string) => setChosen((c) => (c.includes(n) ? c.filter((x) => x !== n) : [...c, n]))

  async function submit() {
    if (returnOrder.isPending) return
    if (chosen.length === 0) {
      setError(t('orders.actions.return.noPieces'))
      return
    }
    setError(null)
    try {
      const r = await returnOrder.mutateAsync({ publicId: order.publicId ?? '', pieceNumbers: chosen.length === pieces.length ? undefined : chosen, notes: notes.trim() || null })
      toast.success(r.message ?? t('orders.actions.return.done', { number: order.orderNumber ?? '' }))
      onClose()
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }

  return (
    <Modal
      open={open}
      size="sm"
      title={t('orders.actions.return.title', { number: order.orderNumber ?? '' })}
      onClose={onClose}
      dismissible={!returnOrder.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={returnOrder.isPending}>
            {t('orders.actions.back')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={returnOrder.isPending}>
            {returnOrder.isPending ? t('common.loading') : t('orders.actions.return.confirm')}
          </button>
        </>
      }
    >
      {error && (
        <div className="form-alert" role="alert">
          {error}
        </div>
      )}
      <p>{t('orders.actions.return.body')}</p>
      <fieldset className="f">
        <legend>{t('orders.actions.return.pieces')}</legend>
        {pieces.map((n) => (
          <label key={n} style={{ display: 'block' }}>
            <input type="checkbox" checked={chosen.includes(n)} onChange={() => toggle(n)} /> {n}
          </label>
        ))}
      </fieldset>
      <div className="f">
        <label htmlFor={notesId}>{t('orders.actions.return.notes')}</label>
        <input id={notesId} value={notes} maxLength={300} autoComplete="off" onChange={(e) => setNotes(e.target.value)} />
      </div>
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
