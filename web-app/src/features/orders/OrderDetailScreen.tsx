// Expediente de la orden (Lote F-C2; maqueta `record()`). `/orders/:publicId`. Lectura: orders.view (aplicado por la ruta).
// Cabecera con las acciones (cada una con su permiso Y la capacidad del DTO): Confirmar, Re-cotizar, Cancelar la orden, Eliminar y
// Asignar chofer (entrega especial). Debajo, el `StatusPipeline` solo con los desvíos laterales y el regreso exacto (avanzar,
// confirmar y cancelar son botones propios). A la izquierda «Detalle» (se edita ahí mismo) e «Historial»; a la derecha COD,
// Cotización (solo con orders.view_price), Chofer asignado, Campos personalizados y Contactos. Sin pestañas.
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Can, useCan, useModule } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, StatusHistory, StatusPipeline, useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, EmptyState, IconChevronRight, Panel, Spinner, toast } from '../../kernel/ui'
import { IconClock } from '../../kernel/ui/screenIcons'
import { useConfirmOrder, useDeleteOrder, useOrder, useOrderStatusChange, useRepriceOrder } from './api'
import { AssignDriverModal, CancelOrderDialog } from './OrderActionDialogs'
import { CreditOverrideDialog } from './CreditOverrideDialog'
import { OrderDetailPanel } from './OrderDetailPanel'
import { CodPanel, DriverPanel, ORDER_ENTITY_TYPE, OrderContactsPanel, OrderCustomFieldsPanel, QuotePanel } from './OrderSidePanels'
import { isAssignableOrder, isCreditExceeded, orderActionVisibility } from './orderRules'
import './orders.css'

const STATUS_DOMAIN = 'OrderStatus'

type Dialog = 'confirm' | 'reprice' | 'cancel' | 'delete' | 'assign' | null

export default function OrderDetailScreen() {
  const t = useT()
  const navigate = useNavigate()
  const { publicId = '' } = useParams()
  const { data: order, isLoading, error } = useOrder(publicId)
  const { data: statuses = [] } = useStatuses(STATUS_DOMAIN, { includeDisabled: true })
  const confirm = useConfirmOrder()
  const reprice = useRepriceOrder()
  const remove = useDeleteOrder()
  const statusChange = useOrderStatusChange()
  const edit = useCan('orders.edit')
  const cancel = useCan('orders.cancel')
  const creditOverride = useCan('orders.credit_override')
  const dispatch = useCan('trips.dispatch')
  const viewPrice = useCan('orders.view_price')
  const catalogOn = useModule('CATALOG')
  const [dialog, setDialog] = useState<Dialog>(null)
  /** Texto del servidor cuando el crédito se excede al confirmar y el usuario puede autorizarlo. */
  const [credit, setCredit] = useState<string | null>(null)

  if (isLoading) return <Spinner block />
  if (error || !order) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('orders.detail.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/orders">
            {t('orders.detail.back')}
          </Link>
        }
      />
    )
  }

  const number = order.orderNumber ?? ''
  const vis = orderActionVisibility(order, { edit, cancel, creditOverride, dispatch, viewPrice, catalogOn }, isAssignableOrder(order, statuses))
  const label = (code: string) => statuses.find((s) => s.code.toLowerCase() === code.toLowerCase())?.label ?? code
  const lockReason = order.capabilities?.canEditCargo && !edit ? 'permission' : 'advanced'

  async function doConfirm(overrideCredit: boolean) {
    await confirm.mutateAsync({ publicId, rowVersion: order?.rowVersion, overrideCredit })
    toast.success(t('orders.actions.confirm.done', { number }))
  }

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{number}</span> · {order.delivery?.name || order.clientName}
          </h1>
          <p>
            {[order.delivery?.city, order.clientName].filter(Boolean).join(' · ')} <StatusChip domain={STATUS_DOMAIN} code={order.status} label={order.statusLabel} />
          </p>
        </div>
        <div className="act">
          <Link className="btn" to="/orders">
            <span className="od-back" aria-hidden="true">
              <IconChevronRight />
            </span>
            {t('orders.detail.back')}
          </Link>
          {vis.confirm && (
            <button type="button" className="btn flow" onClick={() => setDialog('confirm')}>
              {t('orders.actions.confirm.button')}
            </button>
          )}
          {vis.reprice && (
            <button type="button" className="btn" onClick={() => setDialog('reprice')}>
              {t('orders.actions.reprice.button')}
            </button>
          )}
          {vis.assignDriver && (
            <button type="button" className="btn flow" onClick={() => setDialog('assign')}>
              {t(order.assignedDriverPublicId ? 'orders.actions.assign.reassign' : 'orders.actions.assign.button')}
            </button>
          )}
          {vis.cancel && (
            <button type="button" className="btn danger" onClick={() => setDialog('cancel')}>
              {t('orders.actions.cancel.button')}
            </button>
          )}
          {vis.delete && (
            <button type="button" className="btn danger" onClick={() => setDialog('delete')}>
              {t('orders.actions.delete.button')}
            </button>
          )}
        </div>
      </div>

      <div className="od-pipe">
        <StatusPipeline
          domain={STATUS_DOMAIN}
          entityType={ORDER_ENTITY_TYPE}
          entityId={order.id}
          currentCode={order.status}
          disabled={!vis.lateral}
          lateralOnly
          onTransition={async (toCode, comment) => {
            await statusChange.mutateAsync({ publicId, toCode, comment })
            toast.success(t('orders.actions.statusChanged', { number, status: label(toCode) }))
          }}
        />
        <Can perm="orders.edit">
          <p className="note od-pipenote">{t('orders.detail.pipelineNote')}</p>
        </Can>
      </div>

      <div className="od-cols">
        <div className="od-main">
          <OrderDetailPanel order={order} editable={vis.edit} lockReason={lockReason} />
          <Panel icon={<IconClock />} title={t('orders.detail.history')}>
            <StatusHistory entityType={ORDER_ENTITY_TYPE} entityId={order.id ?? 0} domain={STATUS_DOMAIN} />
          </Panel>
        </div>
        <div className="od-side">
          <CodPanel order={order} />
          {vis.quote && <QuotePanel order={order} />}
          <DriverPanel order={order} />
          <OrderCustomFieldsPanel order={order} />
          <OrderContactsPanel order={order} />
        </div>
      </div>

      <ConfirmDialog
        open={dialog === 'confirm'}
        title={t('orders.actions.confirm.title', { number })}
        message={<p>{t(vis.confirm === 'overrideOnly' ? 'orders.actions.confirm.bodyOverrideOnly' : 'orders.actions.confirm.body')}</p>}
        confirmLabel={t(vis.confirm === 'overrideOnly' ? 'orders.entry.credit.authorize' : 'orders.actions.confirm.button')}
        onConfirm={async () => {
          try {
            await doConfirm(vis.confirm === 'overrideOnly')
          } catch (err) {
            // crédito excedido: quien puede autorizarlo ve el diálogo «Autorizar y confirmar»; los demás, el mensaje del servidor
            if (isCreditExceeded(err) && creditOverride) {
              setCredit(err.title)
              return
            }
            throw err
          }
        }}
        onClose={() => setDialog(null)}
      />
      <CreditOverrideDialog
        open={credit !== null}
        message={credit}
        note={t('orders.actions.confirm.creditNote')}
        onAuthorize={() => doConfirm(true)}
        onClose={() => setCredit(null)}
      />
      <ConfirmDialog
        open={dialog === 'reprice'}
        title={t('orders.actions.reprice.title', { number })}
        message={<p>{t('orders.actions.reprice.body')}</p>}
        confirmLabel={t('orders.actions.reprice.button')}
        onConfirm={async () => {
          await reprice.mutateAsync(publicId)
          toast.success(t('orders.actions.reprice.done', { number }))
        }}
        onClose={() => setDialog(null)}
      />
      {dialog === 'cancel' && <CancelOrderDialog order={order} open onClose={() => setDialog(null)} />}
      {dialog === 'assign' && <AssignDriverModal order={order} open onClose={() => setDialog(null)} />}
      <ConfirmDialog
        open={dialog === 'delete'}
        tone="danger"
        title={t('orders.list.deleteTitle', { number })}
        message={t('orders.list.deleteMessage', { number })}
        confirmLabel={t('orders.list.deleteConfirm')}
        onConfirm={async () => {
          await remove.mutateAsync(publicId)
          toast.success(t('orders.list.deleted', { number }))
          navigate('/orders')
        }}
        onClose={() => setDialog(null)}
      />
    </div>
  )
}
