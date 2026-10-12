// Diálogos de la ficha de una orden de trabajo (manual 04 §6.2 y §6.4): alta/edición de una tarea y «Cerrar orden» (fecha de cierre, odómetro
// —obligatorio si el programa es por kilometraje— y comentario). Los errores del servidor salen bajo su campo o arriba del formulario.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, NumberInput, TextArea, TextInput, toast } from '../../kernel/ui'
import { FormModal } from '../clients/contractUi'
import { useTaskMutations, useWorkOrderStatusChange, type MaintenanceTask, type WorkOrderDetail } from './maintenanceApi'
import {
  buildCloseRequest,
  buildTaskRequest,
  closeNeedsOdometer,
  closeSchema,
  closeValuesOf,
  emptyTaskValues,
  hasTaskChanges,
  taskSchema,
  taskValuesOf,
  type CloseValues,
  type TaskValues,
} from './maintenanceRules'

export function TaskModal({ order, task, onClose }: { order: WorkOrderDetail; task: MaintenanceTask | null; onClose: () => void }) {
  const t = useT()
  const tasks = useTaskMutations(order.publicId ?? '')
  const original = task ? taskValuesOf(task) : undefined
  const schema = useMemo(() => taskSchema(t), [t])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: original ?? emptyTaskValues() })

  return (
    <FormModal
      id="work-order-task"
      title={task ? t('fleet.workOrders.tasks.edit') : t('fleet.workOrders.tasks.add')}
      open
      size="md"
      onClose={onClose}
      form={form}
      onSubmit={async (v: TaskValues) => {
        const body = buildTaskRequest(v, original)
        if (task) {
          if (hasTaskChanges(body)) await tasks.update.mutateAsync({ id: task.id ?? 0, body })
        } else await tasks.add.mutateAsync(body)
        toast.success(t('fleet.workOrders.tasks.saved'))
        onClose()
      }}
    >
      <Field name="description" label={t('fleet.workOrders.tasks.description')} required>
        <TextInput autoComplete="off" />
      </Field>
      <div className="r2">
        <Field name="partCost" label={t('fleet.workOrders.tasks.parts')}>
          <NumberInput className="mono" min={0} />
        </Field>
        <Field name="laborCost" label={t('fleet.workOrders.tasks.labor')}>
          <NumberInput className="mono" min={0} />
        </Field>
      </div>
    </FormModal>
  )
}

export function CloseOrderModal({ order, onClose }: { order: WorkOrderDetail; onClose: () => void }) {
  const t = useT()
  const change = useWorkOrderStatusChange(order.publicId ?? '')
  const today = tenantToday()
  const needsOdometer = closeNeedsOdometer(order)
  const schema = useMemo(() => closeSchema(t, { today, needsOdometer }), [t, today, needsOdometer])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: closeValuesOf(order, today) })

  return (
    <FormModal
      id="work-order-close"
      title={t('fleet.workOrders.closing.title', { number: order.number ?? '' })}
      open
      size="md"
      saveLabel={t('fleet.workOrders.actions.close')}
      onClose={onClose}
      form={form}
      onSubmit={async (v: CloseValues) => {
        await change.mutateAsync(buildCloseRequest(v, order.rowVersion))
        toast.success(t('fleet.workOrders.closing.done'))
        onClose()
      }}
    >
      <p>{t('fleet.workOrders.closing.body', { code: order.vehicleCode ?? '' })}</p>
      <div className="r2">
        <Field name="completedDate" label={t('fleet.workOrders.closing.date')}>
          <DateInput max={today} />
        </Field>
        <Field name="odometerKm" label={t('fleet.workOrders.closing.odometer')} required={needsOdometer} help={needsOdometer ? t('fleet.workOrders.closing.odometerHelp') : undefined}>
          <NumberInput className="mono" min={0} />
        </Field>
      </div>
      <Field name="comment" label={t('fleet.workOrders.closing.comment')}>
        <TextArea rows={2} />
      </Field>
    </FormModal>
  )
}
