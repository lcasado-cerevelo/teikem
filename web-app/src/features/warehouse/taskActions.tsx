// Acciones sobre una tarea de almacén (asignar, iniciar, completar con posición sugerida, cancelar) y sus diálogos. Las
// usan la cola de cada pantalla (`TaskQueue` en taskQueue.tsx) y la tabla de acomodo de la ficha del recibo.
// Permisos: asignar/cancelar warehouse.manage; iniciar y completar el permiso del handler de cada tipo (PUTAWAY →
// warehouse.receive, REPLENISH → warehouse.pick, COUNT → warehouse.count, CROSSDOCK → warehouse.crossdock); completar solo
// cuando `completableFromQueue` (COUNT se completa desde la ficha del conteo); cancelar solo PUTAWAY y REPLENISH
// (WarehouseTaskRules.CancelableFromQueue).
import { useMutation } from '@tanstack/react-query'
import { useMemo, useState, type ReactNode } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, toast, type RowAction } from '../../kernel/ui'
import { useWarehouseTaskAction, type WarehouseTaskDto } from './api'
import { AssignTaskModal, CompleteTaskModal } from './taskDialogs'

/** Tipos de tarea con handler en el API (PICK, PACK y LOAD están en el catálogo pero no nacen ni se completan, D41). */
export type WarehouseTaskType = 'PUTAWAY' | 'REPLENISH' | 'COUNT' | 'CROSSDOCK'

/** Permiso del handler de 'Iniciar' y 'Completar' según el tipo (el servidor lo vuelve a validar en StartAsync/CompleteAsync). */
const TASK_HANDLER_PERM: Record<WarehouseTaskType, string> = {
  PUTAWAY: 'warehouse.receive',
  REPLENISH: 'warehouse.pick',
  COUNT: 'warehouse.count',
  CROSSDOCK: 'warehouse.crossdock',
}
const CANCELLABLE_TYPES = new Set(['PUTAWAY', 'REPLENISH'])
const isClosed = (r: WarehouseTaskDto) => r.statusCode === 'DONE' || r.statusCode === 'CANCELLED'

// ---------------------------------------------------------------------------------------------------------------------
// Acciones por fila (asignar, iniciar, completar, cancelar) con sus diálogos
// ---------------------------------------------------------------------------------------------------------------------
/**
 * Acciones de fila de una tarea y los diálogos que abren (pintar `dialogs` una vez en la pantalla). Las usan la cola de
 * cada pantalla (`TaskQueue`) y la tabla de acomodo de la ficha del recibo.
 */
export function useTaskRowActions(): { rowActions: RowAction<WarehouseTaskDto>[]; dialogs: ReactNode } {
  const t = useT()
  const action = useWarehouseTaskAction()
  const [assignTask, setAssignTask] = useState<WarehouseTaskDto | null>(null)
  const [completeTask, setCompleteTask] = useState<WarehouseTaskDto | null>(null)
  const [cancelTask, setCancelTask] = useState<WarehouseTaskDto | null>(null)

  const startMutation = useMutation({
    mutationFn: (id: number) => action.mutateAsync({ id, action: 'start' }),
    onSuccess: () => toast.success(t('warehouse.tasks.started')),
    onError: (err) => toast.error(applyProblemDetails(err).title),
  })
  const start = startMutation.mutate

  const rowActions = useMemo<RowAction<WarehouseTaskDto>[]>(() => {
    const entries = Object.entries(TASK_HANDLER_PERM)
    return [
      {
        key: 'assign',
        label: t('warehouse.tasks.actions.assign'),
        perm: 'warehouse.manage',
        visible: (r) => !isClosed(r),
        onClick: (r) => setAssignTask(r),
      },
      ...entries.map(([typeCode, perm]): RowAction<WarehouseTaskDto> => ({
        key: `start-${typeCode}`,
        label: t('warehouse.tasks.actions.start'),
        perm,
        visible: (r) => r.typeCode === typeCode && r.statusCode === 'PENDING',
        onClick: (r) => start(r.id ?? 0),
      })),
      // Completar: mismo permiso por tipo que Iniciar (el servidor exige el del handler; sin él respondería 403).
      ...entries.map(([typeCode, perm]): RowAction<WarehouseTaskDto> => ({
        key: `complete-${typeCode}`,
        label: t('warehouse.tasks.actions.complete'),
        perm,
        visible: (r) => r.typeCode === typeCode && r.completableFromQueue === true && !isClosed(r),
        onClick: (r) => setCompleteTask(r),
      })),
      {
        key: 'cancel',
        label: t('warehouse.tasks.actions.cancel'),
        perm: 'warehouse.manage',
        visible: (r) => CANCELLABLE_TYPES.has(r.typeCode ?? '') && !isClosed(r),
        onClick: (r) => setCancelTask(r),
        tone: 'danger',
      },
    ]
  }, [t, start])

  const dialogs = (
    <>
      <AssignTaskModal task={assignTask} open={assignTask !== null} onClose={() => setAssignTask(null)} />
      <CompleteTaskModal task={completeTask} open={completeTask !== null} onClose={() => setCompleteTask(null)} />
      <ConfirmDialog
        open={cancelTask !== null}
        tone="danger"
        title={t('warehouse.tasks.cancelTitle')}
        message={t('warehouse.tasks.cancelBody')}
        confirmLabel={t('warehouse.tasks.actions.cancel')}
        onConfirm={async () => {
          if (!cancelTask) return
          await action.mutateAsync({ id: cancelTask.id ?? 0, action: 'cancel', body: { comment: null } })
          toast.success(t('warehouse.tasks.cancelled'))
        }}
        onClose={() => setCancelTask(null)}
      />
    </>
  )

  return { rowActions, dialogs }
}

