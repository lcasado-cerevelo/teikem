// Ficha de una orden de trabajo (manual 04 §6): encabezado editable solo si el servidor lo permite (`canEdit`: la capacidad EDIT_WORK_ORDER
// está denegada en «Cerrada» y «Cancelada») con un solo «Guardar cambios» (PATCH mínimo + `rowVersion`), tareas (agregar, editar, marcar
// completada, quitar) con costos que calcula el servidor, acciones por estatus (Iniciar / Cerrar orden / Cancelar orden) con sus efectos
// explicados, e historial de estatus. Escribir exige `fleet.maintenance`; leer, `fleet.view`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip, StatusHistory } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  ConfirmDialog,
  DataTable,
  DateInput,
  EmptyState,
  Field,
  Form,
  IconCheckCircle,
  IconClock,
  IconEdit,
  IconPlay,
  IconPlus,
  IconTrash,
  IconXCircle,
  NumberInput,
  Panel,
  Spinner,
  TextArea,
  TextInput,
  toast,
  useElementWidth,
  type DataColumn,
} from '../../kernel/ui'
import { IconClip } from '../../kernel/ui/screenIcons'
import { CommentDialog } from '../drivers/CommentDialog'
import { maintenanceKeys, useTaskMutations, useUpdateWorkOrder, useWorkOrder, useWorkOrderStatusChange, WORK_ORDER_ENTITY_TYPE, WORK_ORDER_STATUS_DOMAIN, type MaintenanceTask, type WorkOrderDetail } from './maintenanceApi'
import {
  buildWorkOrderPatch,
  incompleteTasks,
  taskTotals,
  workOrderActions,
  workOrderEditSchema,
  workOrderEditValuesOf,
  WorkOrderStatuses,
  type WorkOrderEditValues,
} from './maintenanceRules'
import { VehicleCell } from './maintenanceShared'
import { useVehicleChoices } from './vehicleChoices'
import { CloseOrderModal, TaskModal } from './WorkOrderDialogs'

/** Encabezado de la orden: datos, edición y acciones por estatus. */
function WorkOrderHeaderPanel({ order, onClose }: { order: WorkOrderDetail; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const qc = useQueryClient()
  const publicId = order.publicId ?? ''
  const vehicles = useVehicleChoices()
  const canMaintain = useCan('fleet.maintenance')
  const editable = canMaintain && order.canEdit === true && order.isTerminal !== true
  const update = useUpdateWorkOrder(publicId)
  const change = useWorkOrderStatusChange(publicId)
  const [dialog, setDialog] = useState<'start' | 'close' | 'cancel' | null>(null)
  const actions = canMaintain ? workOrderActions(order) : []

  const initial = useMemo(() => workOrderEditValuesOf(order), [order])
  const costsFromTasks = order.costsFromTasks === true
  const schema = useMemo(() => workOrderEditSchema(t, { original: initial, costsFromTasks }), [t, initial, costsFromTasks])
  const form = useForm({ resolver: zodResolver(schema), values: initial, resetOptions: { keepDirtyValues: true } })

  const submit = async (v: WorkOrderEditValues) => {
    const plan = buildWorkOrderPatch(v, order)
    if (!plan.changed) return
    try {
      const saved = await update.mutateAsync(plan.request)
      // lo guardado (recortado por el servidor) pasa a ser el valor de partida: el botón vuelve a apagarse
      form.reset(workOrderEditValuesOf(saved), { keepDirtyValues: false })
    } catch (err) {
      // 409: otro usuario guardó antes; se relee la orden (y se conserva lo escrito) para tener el `rowVersion` nuevo
      if (applyProblemDetails(err).code === 'conflict') void qc.invalidateQueries({ queryKey: maintenanceKeys.workOrder(publicId) })
      throw err
    }
    toast.success(t('fleet.workOrders.detail.saved'))
  }

  const vehicle = vehicles.find(order.vehiclePublicId)

  return (
    <Panel
      icon={<IconClip />}
      title={t('fleet.workOrders.detail.title', { number: order.number ?? '' })}
      actions={
        <button type="button" className="btn sm" onClick={onClose}>
          {t('fleet.workOrders.detail.close')}
        </button>
      }
    >
      <div className="flt-wo-facts">
        <span>
          <StatusChip domain={WORK_ORDER_STATUS_DOMAIN} code={order.statusCode} label={order.status} />
        </span>
        <span>
          <span className="meta">{t('fleet.workOrders.detail.vehicle')}</span> <VehicleCell plate={vehicle?.plateNumber} code={order.vehicleCode} />
        </span>
        <span>
          <span className="meta">{t('fleet.workOrders.detail.type')}</span> <b>{order.maintenanceType || order.maintenanceTypeCode}</b>
        </span>
        <span>
          <span className="meta">{t('fleet.workOrders.detail.program')}</span> <b>{order.scheduleName || t('fleet.workOrders.detail.none')}</b>
        </span>
        {order.completedDate && (
          <span>
            <span className="meta">{t('fleet.workOrders.detail.completedDate')}</span> <b className="mono">{f.date(order.completedDate)}</b>
          </span>
        )}
      </div>

      {actions.length > 0 && (
        <div className="flt-wo-acts">
          {actions.includes('start') && (
            <button type="button" className="btn flow" onClick={() => setDialog('start')}>
              <IconPlay /> {t('fleet.workOrders.actions.start')}
            </button>
          )}
          {actions.includes('close') && (
            <button type="button" className="btn flow" onClick={() => setDialog('close')}>
              <IconCheckCircle /> {t('fleet.workOrders.actions.close')}
            </button>
          )}
          {actions.includes('cancel') && (
            <button type="button" className="btn flt-danger" onClick={() => setDialog('cancel')}>
              <IconXCircle /> {t('fleet.workOrders.actions.cancel')}
            </button>
          )}
        </div>
      )}

      {order.isTerminal === true && (
        <p className="note flt-retired" role="status">
          {t('fleet.workOrders.detail.readOnly', { status: order.status ?? order.statusCode ?? '' })}
        </p>
      )}

      <Form form={form} onSubmit={submit}>
        <div className="flt-grid">
          <Field name="scheduledDate" label={t('fleet.workOrders.detail.scheduledDate')}>
            <DateInput disabled={!editable} />
          </Field>
          <Field name="odometerKm" label={t('fleet.workOrders.detail.odometer')}>
            <NumberInput className="mono" min={0} disabled={!editable} />
          </Field>
          <Field name="vendor" label={t('fleet.workOrders.detail.vendor')} className="wide">
            <TextInput autoComplete="off" disabled={!editable} />
          </Field>
          <Field name="laborCost" label={t('fleet.workOrders.detail.labor')}>
            <NumberInput className="mono" min={0} disabled={!editable || costsFromTasks} />
          </Field>
          <Field name="partsCost" label={t('fleet.workOrders.detail.parts')}>
            <NumberInput className="mono" min={0} disabled={!editable || costsFromTasks} />
          </Field>
          <div className="f">
            <label htmlFor="flt-wo-total">{t('fleet.workOrders.detail.total')}</label>
            <input id="flt-wo-total" className="mono" value={f.money(order.totalCost ?? 0)} disabled readOnly />
          </div>
          {costsFromTasks && <p className="help wide">{t('fleet.workOrders.detail.costsFromTasks')}</p>}
          <Field name="notes" label={t('fleet.workOrders.detail.notes')} className="wide">
            <TextArea rows={2} disabled={!editable} />
          </Field>
        </div>
        {editable && (
          <div className="form-acts flt-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('fleet.workOrders.detail.saveChanges')}
            </button>
          </div>
        )}
      </Form>

      <Can perm="fleet.maintenance">
        <ConfirmDialog
          open={dialog === 'start'}
          title={t('fleet.workOrders.start.title', { number: order.number ?? '' })}
          message={t('fleet.workOrders.start.body', { code: order.vehicleCode ?? '' })}
          confirmLabel={t('fleet.workOrders.actions.start')}
          onConfirm={async () => {
            await change.mutateAsync({ toCode: WorkOrderStatuses.InProgress, rowVersion: order.rowVersion ?? null })
            toast.success(t('fleet.workOrders.start.done'))
          }}
          onClose={() => setDialog(null)}
        />
        {dialog === 'close' && <CloseOrderModal order={order} onClose={() => setDialog(null)} />}
        <CommentDialog
          open={dialog === 'cancel'}
          title={t('fleet.workOrders.cancelling.title', { number: order.number ?? '' })}
          confirmLabel={t('fleet.workOrders.actions.cancel')}
          onClose={() => setDialog(null)}
          onConfirm={async (comment) => {
            await change.mutateAsync({ toCode: WorkOrderStatuses.Cancelled, ...(comment ? { comment } : {}), rowVersion: order.rowVersion ?? null })
            toast.success(t('fleet.workOrders.cancelling.done'))
          }}
        >
          <p>{t('fleet.workOrders.cancelling.body', { code: order.vehicleCode ?? '' })}</p>
        </CommentDialog>
      </Can>
    </Panel>
  )
}

/** Tareas de la orden: los costos de la orden son la suma de las activas. */
function WorkOrderTasksPanel({ order }: { order: WorkOrderDetail }) {
  const t = useT()
  const f = useFormat()
  const canMaintain = useCan('fleet.maintenance')
  const editable = canMaintain && order.canEdit === true && order.isTerminal !== true
  const tasks = useTaskMutations(order.publicId ?? '')
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const [editing, setEditing] = useState<{ task: MaintenanceTask | null } | null>(null)
  const [removing, setRemoving] = useState<MaintenanceTask | null>(null)
  // la casilla responde al clic de inmediato: mientras viaja la petición muestra lo pedido (si falla, vuelve a lo que había)
  const [asked, setAsked] = useState<Record<number, boolean>>({})
  const rows = useMemo(() => order.tasks ?? [], [order.tasks])
  const pending = incompleteTasks(rows)
  const totals = taskTotals(rows)

  const toggle = async (task: MaintenanceTask, done: boolean) => {
    const id = task.id ?? 0
    setAsked((m) => ({ ...m, [id]: done }))
    try {
      await tasks.update.mutateAsync({ id, body: { isCompleted: done } })
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    } finally {
      setAsked((m) => {
        const next = { ...m }
        delete next[id]
        return next
      })
    }
  }

  const columns: DataColumn<MaintenanceTask>[] = [
    { id: 'description', header: t('fleet.workOrders.tasks.description'), cell: (x) => x.description, sortValue: (x) => x.description ?? '', card: 'title' },
    { id: 'parts', header: t('fleet.workOrders.tasks.parts'), align: 'end', cell: (x) => <span className="mono">{x.partCost != null ? f.money(x.partCost) : '—'}</span>, sortValue: (x) => x.partCost ?? null },
    { id: 'labor', header: t('fleet.workOrders.tasks.labor'), align: 'end', cell: (x) => <span className="mono">{x.laborCost != null ? f.money(x.laborCost) : '—'}</span>, sortValue: (x) => x.laborCost ?? null },
    {
      id: 'done',
      header: t('fleet.workOrders.tasks.done'),
      cell: (x) => (
        <input
          type="checkbox"
          checked={asked[x.id ?? 0] ?? x.isCompleted === true}
          disabled={!editable || (x.id ?? 0) in asked}
          aria-label={t('fleet.workOrders.tasks.markDone', { description: x.description ?? '' })}
          onChange={(e) => void toggle(x, e.target.checked)}
        />
      ),
      exportValue: (x) => (x.isCompleted ? t('customFields.yes') : ''),
      sortValue: (x) => (x.isCompleted ? 1 : 0),
    },
  ]

  return (
    <Panel
      icon={<IconClip />}
      title={t('fleet.workOrders.tasks.title')}
      badge={rows.length}
      actions={
        editable ? (
          <button type="button" className="btn sm flow" onClick={() => setEditing({ task: null })}>
            <IconPlus /> {t('fleet.workOrders.tasks.add')}
          </button>
        ) : undefined
      }
    >
      <div ref={ref}>
        <p className="help" style={{ marginTop: 0 }}>
          {t('fleet.workOrders.tasks.help')} {pending > 0 && <b className="flt-pending">{t('fleet.workOrders.tasks.pending', { count: pending })}</b>}
        </p>
        {rows.length === 0 ? (
          <p className="flt-empty">{t('fleet.workOrders.tasks.none')}</p>
        ) : (
          <>
            <DataTable
              label={t('fleet.workOrders.tasks.title')}
              columns={columns}
              rows={rows}
              rowKey={(x) => String(x.id)}
              pagination={false}
              exportable={false}
              forceCards={width > 0 && width < 560}
              rowClassName={(x) => (x.isCompleted ? 'dim' : undefined)}
              rowActions={[
                { key: 'edit', label: t('fleet.edit'), icon: <IconEdit />, onClick: (x) => setEditing({ task: x }), visible: () => editable },
                { key: 'remove', label: t('fleet.remove'), icon: <IconTrash />, tone: 'danger', onClick: (x) => setRemoving(x), visible: () => editable },
              ]}
            />
            <p className="flt-totals">
              <span>{t('fleet.workOrders.tasks.totals')}</span>
              <span>
                {t('fleet.workOrders.tasks.labor')} <b className="mono">{f.money(totals.labor)}</b>
              </span>
              <span>
                {t('fleet.workOrders.tasks.parts')} <b className="mono">{f.money(totals.parts)}</b>
              </span>
              <span>
                {t('fleet.workOrders.detail.total')} <b className="mono">{f.money(totals.total)}</b>
              </span>
            </p>
          </>
        )}
      </div>
      {editing && <TaskModal key={editing.task?.id ?? 'new'} order={order} task={editing.task} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('fleet.workOrders.tasks.removeTitle')}
        message={t('fleet.workOrders.tasks.removeBody', { description: removing?.description ?? '' })}
        confirmLabel={t('fleet.remove')}
        onConfirm={async () => {
          if (!removing) return
          await tasks.remove.mutateAsync(removing.id ?? 0)
          toast.success(t('fleet.workOrders.tasks.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}

/** Ficha completa (arriba de la lista): encabezado, tareas e historial. */
export function WorkOrderDetailPanel({ publicId, onClose }: { publicId: string; onClose: () => void }) {
  const t = useT()
  const { data: order, isPending, error } = useWorkOrder(publicId)

  if (isPending) {
    return (
      <Panel>
        <Spinner block />
      </Panel>
    )
  }
  if (error || !order) {
    return (
      <Panel icon={<IconClip />} title={t('fleet.workOrders.title')} actions={<button type="button" className="btn sm" onClick={onClose}>{t('fleet.workOrders.detail.close')}</button>}>
        <EmptyState icon={<IconClip />} title={error ? applyProblemDetails(error).title : t('fleet.workOrders.detail.loadFailed')} />
      </Panel>
    )
  }
  return (
    <div className="flt-stack flt-wo">
      <WorkOrderHeaderPanel order={order} onClose={onClose} />
      <WorkOrderTasksPanel order={order} />
      <Panel icon={<IconClock />} title={t('fleet.workOrders.detail.history')}>
        <StatusHistory entityType={WORK_ORDER_ENTITY_TYPE} entityId={order.id ?? 0} domain={WORK_ORDER_STATUS_DOMAIN} />
      </Panel>
    </div>
  )
}
