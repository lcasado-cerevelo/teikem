// Diálogos de las acciones sobre una tarea de almacén: Asignar (usuario del tenant) y Completar (posición destino con las
// sugeridas del acomodo primero, cantidad parcial y series). Los abre `useTaskRowActions` (taskActions.tsx).
import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { api, unwrap } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, Select, TextArea, toast } from '../../kernel/ui'
import { usePutawaySuggestions, useWarehouseTaskAction, type WarehouseTaskDto } from './api'
import { BinPickerInput } from './pickers'

// ---------------------------------------------------------------------------------------------------------------------
// Asignar (Select de usuario del tenant; GET /api/v1/users exige admin.users — sin ese permiso se avisa sin sacar de la
// pantalla, como ClientPicker/WarehousePicker).
// ---------------------------------------------------------------------------------------------------------------------
export function AssignTaskModal({ task, open, onClose }: { task: WarehouseTaskDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const action = useWarehouseTaskAction()
  const users = useQuery({
    queryKey: ['/api/v1/users'],
    queryFn: () => unwrap(api.GET('/api/v1/users')),
    enabled: open,
    meta: { handleAccessDenied: false },
  })
  const form = useForm({ values: { userId: task?.assignedToUserId != null ? String(task.assignedToUserId) : '' } })
  const formId = 'warehouse-task-assign'

  const options = (users.data ?? []).filter((u) => u.isActive !== false).map((u) => ({ value: String(u.id), label: u.fullName ?? u.email ?? String(u.id) }))

  if (!task) return null

  return (
    <Modal
      open={open}
      title={t('warehouse.tasks.assign.title')}
      onClose={onClose}
      size="sm"
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
      {users.error ? (
        <p className="note">{t('warehouse.tasks.assign.noAccess')}</p>
      ) : (
        <Form
          id={formId}
          form={form}
          onSubmit={async (v) => {
            await action.mutateAsync({ id: task.id ?? 0, action: 'assign', body: { userId: v.userId ? Number(v.userId) : null } })
            toast.success(t('warehouse.tasks.assign.saved'))
            onClose()
          }}
        >
          <Field name="userId" label={t('warehouse.tasks.assign.user')}>
            <Select options={options} placeholder={t('warehouse.tasks.assign.none')} />
          </Field>
        </Form>
      )}
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Completar (con sugerencia de posición para PUTAWAY)
// ---------------------------------------------------------------------------------------------------------------------
interface CompleteFormValues {
  /** Id de la posición destino como texto (valor de BinPickerInput); '' = sin elegir. */
  toBinId: string
  quantity: number | null
  serialNumbers: string
}

export function CompleteTaskModal({ task, open, onClose }: { task: WarehouseTaskDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const action = useWarehouseTaskAction()
  const isPutaway = task?.typeCode === 'PUTAWAY'
  const suggestions = usePutawaySuggestions({ taskId: task?.id }, { enabled: open && isPutaway && Boolean(task?.id) })
  // Posiciones activas del almacén de la tarea (BinPicker: se busca/escanea por código y se envía el id, nunca un número a
  // mano). Las sugeridas del acomodo van primero, en el orden del servidor, con la marca "Sugerida"; no se preeligen
  // (vacío = el servidor decide).
  const suggestedBinIds = useMemo(() => (suggestions.data ?? []).map((s) => s.binId), [suggestions.data])
  const form = useForm<CompleteFormValues>({ defaultValues: { toBinId: '', quantity: null, serialNumbers: '' } })
  const formId = 'warehouse-task-complete'
  if (!task) return null

  const close = () => {
    form.reset({ toBinId: '', quantity: null, serialNumbers: '' })
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.tasks.complete.title')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      {isPutaway && (
        <div className="note" style={{ marginBottom: 10 }}>
          {suggestions.isLoading && t('common.loading')}
          {!suggestions.isLoading && (suggestions.data ?? []).length === 0 && t('warehouse.tasks.complete.noSuggestion')}
          {!suggestions.isLoading && (suggestions.data ?? []).length > 0 && (
            <>
              {t('warehouse.tasks.complete.suggestion')}{' '}
              {(suggestions.data ?? [])
                .map((s) => `${s.binCode ?? ''}${s.reason ? ` (${s.reason})` : ''}`)
                .join(' · ')}
            </>
          )}
        </div>
      )}
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          await action.mutateAsync({
            id: task.id ?? 0,
            action: 'complete',
            body: {
              toBinId: v.toBinId ? Number(v.toBinId) : null,
              quantity: v.quantity,
              serialNumbers: v.serialNumbers.trim() ? v.serialNumbers.split(',').map((s) => s.trim()).filter(Boolean) : null,
            },
          })
          toast.success(t('warehouse.tasks.complete.saved'))
          close()
        }}
      >
        <div className="r2">
          <Field name="toBinId" label={t('warehouse.tasks.complete.toBin')} help={isPutaway ? t('warehouse.tasks.complete.toBinHelp') : undefined}>
            <BinPickerInput warehousePublicId={task.warehousePublicId} suggestedBinIds={suggestedBinIds} placeholder={t('warehouse.tasks.complete.anyBin')} />
          </Field>
          <Field name="quantity" label={t('warehouse.tasks.complete.quantity')} help={t('warehouse.tasks.complete.quantityHelp')}>
            <NumberInput step="0.001" />
          </Field>
        </div>
        <Field name="serialNumbers" label={t('warehouse.tasks.complete.serials')} help={t('warehouse.tasks.complete.serialsHelp')}>
          <TextArea rows={2} />
        </Field>
      </Form>
    </Modal>
  )
}

