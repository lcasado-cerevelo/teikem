// Pantalla E (Lote F6) — Cruce de muelle (demo): ficha del plan. `/warehouse/cross-dock-plans/:id`. Lectura: inventory.view
// + CROSSDOCK. Asignar/cancelar asignación/mover/completar: warehouse.crossdock.
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { StatusChip, StatusPipeline } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, EmptyState, Field, Form, Modal, NumberInput, Panel, Spinner, TextInput, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { useCrossDockAction, useCrossDockCandidates, useCrossDockPlan, useOrderLookup, type CrossDockCandidateDto, type OrderListItemDto } from './api'

type CrossDockAllocationDto = components['schemas']['CrossDockAllocationDto']

const STATUS_DOMAIN = 'CrossDockStatus'
const ENTITY_TYPE = 'CROSSDOCK_PLAN'
const ALLOCATION_STATUS_DOMAIN = 'AllocationStatus'

// ---------------------------------------------------------------------------------------------------------------------
// Asignar (elegir orden por número/factura/lote de empaque vía /orders/lookup)
// ---------------------------------------------------------------------------------------------------------------------
interface AllocateFormValues {
  quantity: number | null
}

function AllocateModal({ planId, candidate, open, onClose }: { planId: number; candidate: CrossDockCandidateDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const action = useCrossDockAction()
  const [code, setCode] = useState('')
  const [search, setSearch] = useState('')
  const [order, setOrder] = useState<OrderListItemDto | null>(null)
  const [orderError, setOrderError] = useState<string | null>(null)
  const lookup = useOrderLookup(search || null, { enabled: Boolean(search) })
  const form = useForm<AllocateFormValues>({ values: { quantity: candidate?.allocatable ?? null } })
  const formId = 'cross-dock-allocate'

  useEffect(() => {
    const h = setTimeout(() => setSearch(code.trim()), 250)
    return () => clearTimeout(h)
  }, [code])

  if (!candidate) return null
  const matches = lookup.data?.matches ?? []

  return (
    <Modal
      open={open}
      title={t('warehouse.crossDockPlans.allocate.title')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <p className="note">
        {candidate.sku} · {candidate.lotNumber ?? ''} — {t('warehouse.crossDockPlans.allocate.available', { qty: candidate.allocatable ?? 0 })}
      </p>
      <div className="f">
        <label htmlFor="cross-dock-order-search">{t('warehouse.crossDockPlans.allocate.order')}</label>
        <input
          id="cross-dock-order-search"
          type="text"
          value={code}
          placeholder={t('warehouse.crossDockPlans.allocate.orderPlaceholder')}
          onChange={(e) => {
            setCode(e.target.value)
            setOrder(null)
            setOrderError(null)
          }}
        />
      </div>
      {search && !order && (
        <div className="milist" role="listbox" style={{ marginBottom: 10 }}>
          {lookup.isLoading && <div className="mnone">{t('common.loading')}</div>}
          {!lookup.isLoading && matches.length === 0 && <div className="mnone">{t('warehouse.crossDockPlans.allocate.noMatches')}</div>}
          {matches.map((o) => (
            <div
              key={o.publicId}
              role="option"
              aria-selected={false}
              className="mi"
              onClick={() => {
                setOrder(o)
                setOrderError(null)
              }}
            >
              <span className="code">{o.orderNumber}</span> · {o.clientName} {o.packBatchNumber ? `· ${o.packBatchNumber}` : ''}
            </div>
          ))}
        </div>
      )}
      {order && (
        <p className="note">
          {t('warehouse.crossDockPlans.allocate.selected')} <strong>{order.orderNumber}</strong> · {order.clientName}
        </p>
      )}
      {orderError && (
        <p className="ferr" role="alert">
          {orderError}
        </p>
      )}
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          if (!order?.publicId) {
            setOrderError(t('warehouse.crossDockPlans.allocate.orderRequired'))
            return
          }
          await action.mutateAsync({
            action: 'allocate',
            id: planId,
            body: { receiptLineId: candidate.receiptLineId, orderPublicId: order.publicId, quantity: v.quantity },
          })
          toast.success(t('warehouse.crossDockPlans.allocate.saved'))
          onClose()
        }}
      >
        <Field name="quantity" label={t('warehouse.crossDockPlans.allocate.quantity')} required>
          <NumberInput step="0.001" />
        </Field>
      </Form>
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Mover (con comentario opcional)
// ---------------------------------------------------------------------------------------------------------------------
function MoveModal({ planId, allocation, open, onClose }: { planId: number; allocation: CrossDockAllocationDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const action = useCrossDockAction()
  const form = useForm({ defaultValues: { comment: '' } })
  const formId = 'cross-dock-move'
  if (!allocation) return null
  return (
    <Modal
      open={open}
      title={t('warehouse.crossDockPlans.move.title')}
      onClose={onClose}
      size="sm"
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('warehouse.crossDockPlans.move.confirm')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          await action.mutateAsync({ action: 'move', id: planId, allocationId: allocation.id ?? 0, body: { comment: v.comment || null } })
          toast.success(t('warehouse.crossDockPlans.move.saved'))
          onClose()
        }}
      >
        <Field name="comment" label={t('warehouse.crossDockPlans.move.comment')}>
          <TextInput />
        </Field>
      </Form>
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Pantalla
// ---------------------------------------------------------------------------------------------------------------------
export default function CrossDockPlanDetailScreen() {
  const t = useT()
  const { id } = useParams()
  const planId = Number(id)
  const canManage = useCan('warehouse.crossdock')
  const { data: plan, isLoading, error } = useCrossDockPlan(Number.isFinite(planId) ? planId : null)
  const { data: candidates = [], isLoading: candidatesLoading } = useCrossDockCandidates(Number.isFinite(planId) ? planId : null)
  const action = useCrossDockAction()

  const [allocating, setAllocating] = useState<CrossDockCandidateDto | null>(null)
  const [moving, setMoving] = useState<CrossDockAllocationDto | null>(null)
  const [cancelling, setCancelling] = useState<CrossDockAllocationDto | null>(null)
  const [completing, setCompleting] = useState(false)

  const candidateColumns = useMemo<DataColumn<CrossDockCandidateDto>[]>(
    () => [
      { id: 'receipt', header: t('warehouse.crossDockPlans.candidates.receipt'), cell: (c) => c.receiptNumber, card: 'title' },
      { id: 'sku', header: t('warehouse.crossDockPlans.candidates.sku'), cell: (c) => c.sku },
      { id: 'lot', header: t('warehouse.crossDockPlans.candidates.lot'), cell: (c) => c.lotNumber ?? '—' },
      { id: 'bin', header: t('warehouse.crossDockPlans.candidates.bin'), cell: (c) => c.stagingBinCode ?? '—' },
      { id: 'base', header: t('warehouse.crossDockPlans.candidates.base'), cell: (c) => c.baseQty, align: 'end' },
      { id: 'allocated', header: t('warehouse.crossDockPlans.candidates.allocated'), cell: (c) => c.allocatedQty, align: 'end' },
      { id: 'available', header: t('warehouse.crossDockPlans.candidates.available'), cell: (c) => c.allocatable, align: 'end' },
    ],
    [t],
  )

  const candidateActions = useMemo<RowAction<CrossDockCandidateDto>[]>(
    () => [
      {
        key: 'allocate',
        label: t('warehouse.crossDockPlans.allocate.title'),
        perm: 'warehouse.crossdock',
        visible: (c) => (c.allocatable ?? 0) > 0,
        onClick: (c) => setAllocating(c),
      },
    ],
    [t],
  )

  const allocationColumns = useMemo<DataColumn<CrossDockAllocationDto>[]>(
    () => [
      { id: 'receipt', header: t('warehouse.crossDockPlans.allocations.receipt'), cell: (a) => a.receiptNumber, card: 'title' },
      { id: 'sku', header: t('warehouse.crossDockPlans.allocations.sku'), cell: (a) => a.sku },
      { id: 'order', header: t('warehouse.crossDockPlans.allocations.order'), cell: (a) => a.packBatchNumber ?? a.clientName },
      { id: 'quantity', header: t('warehouse.crossDockPlans.allocations.quantity'), cell: (a) => a.quantity, align: 'end' },
      { id: 'confirmed', header: t('warehouse.crossDockPlans.allocations.confirmed'), cell: (a) => a.confirmedQty ?? '—', align: 'end' },
      { id: 'short', header: t('warehouse.crossDockPlans.allocations.short'), cell: (a) => a.shortQty, align: 'end' },
      {
        id: 'status',
        header: t('warehouse.crossDockPlans.allocations.status'),
        cell: (a) => <StatusChip domain={ALLOCATION_STATUS_DOMAIN} code={a.statusCode} label={a.status} />,
      },
    ],
    [t],
  )

  const allocationActions = useMemo<RowAction<CrossDockAllocationDto>[]>(
    () => [
      {
        key: 'move',
        label: t('warehouse.crossDockPlans.move.title'),
        perm: 'warehouse.crossdock',
        visible: (a) => a.statusCode === 'PLANNED',
        onClick: (a) => setMoving(a),
      },
      {
        key: 'cancel',
        label: t('warehouse.crossDockPlans.allocations.cancel'),
        perm: 'warehouse.crossdock',
        visible: (a) => a.statusCode === 'PLANNED',
        onClick: (a) => setCancelling(a),
        tone: 'danger',
      },
    ],
    [t],
  )

  if (isLoading) return <Spinner block />
  if (error || !plan) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.crossDockPlans.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/cross-dock-plans">
            {t('warehouse.crossDockPlans.back')}
          </Link>
        }
      />
    )
  }

  const hasOpenAllocations = (plan.allocations ?? []).some((a) => a.statusCode === 'PLANNED')

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>
            <span className="ref">{plan.number}</span> · {plan.warehouseCode}
          </h1>
          <p>{t('warehouse.crossDockPlans.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.crossdock">
            {plan.statusCode !== 'COMPLETED' && (
              <button type="button" className="btn flow" onClick={() => setCompleting(true)}>
                {t('warehouse.crossDockPlans.complete')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <StatusPipeline domain={STATUS_DOMAIN} entityType={ENTITY_TYPE} entityId={plan.id} currentCode={plan.statusCode} disabled />
      </div>

      <Panel title={t('warehouse.crossDockPlans.candidates.title')} flush>
        <DataTable
          label={t('warehouse.crossDockPlans.candidates.title')}
          columns={candidateColumns}
          rows={candidates}
          rowKey={(c) => c.receiptLineId ?? 0}
          loading={candidatesLoading}
          rowActions={canManage ? candidateActions : []}
        />
      </Panel>

      <div style={{ marginTop: 14 }}>
        <Panel title={t('warehouse.crossDockPlans.allocations.title')} flush>
          <DataTable
            label={t('warehouse.crossDockPlans.allocations.title')}
            columns={allocationColumns}
            rows={plan.allocations ?? []}
            rowKey={(a) => a.id ?? 0}
            rowActions={canManage ? allocationActions : []}
          />
        </Panel>
      </div>

      <AllocateModal
        key={allocating?.receiptLineId ?? 'none'}
        planId={plan.id ?? 0}
        candidate={allocating}
        open={allocating !== null}
        onClose={() => setAllocating(null)}
      />
      <MoveModal planId={plan.id ?? 0} allocation={moving} open={moving !== null} onClose={() => setMoving(null)} />

      <ConfirmDialog
        open={cancelling !== null}
        tone="danger"
        title={t('warehouse.crossDockPlans.allocations.cancelTitle')}
        message={t('warehouse.crossDockPlans.allocations.cancelBody')}
        confirmLabel={t('warehouse.crossDockPlans.allocations.cancel')}
        onConfirm={async () => {
          if (!cancelling) return
          await action.mutateAsync({ action: 'cancelAllocation', id: plan.id ?? 0, allocationId: cancelling.id ?? 0 })
          toast.success(t('warehouse.crossDockPlans.allocations.cancelled'))
        }}
        onClose={() => setCancelling(null)}
      />

      <ConfirmDialog
        open={completing}
        tone={hasOpenAllocations ? 'danger' : 'flow'}
        title={t('warehouse.crossDockPlans.completeTitle')}
        message={t('warehouse.crossDockPlans.completeBody')}
        confirmLabel={t('warehouse.crossDockPlans.complete')}
        onConfirm={async () => {
          await action.mutateAsync({ action: 'complete', id: plan.id ?? 0 })
          toast.success(t('warehouse.crossDockPlans.completed'))
        }}
        onClose={() => setCompleting(false)}
      />
    </div>
  )
}
