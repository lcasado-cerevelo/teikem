// Lote 13 — "Tareas de acomodo" (PUTAWAY) de un recibo confirmado: se asignan, inician, completan (con la posición
// sugerida) y cancelan aquí mismo con las acciones de `taskActions.tsx` (asignar/cancelar warehouse.manage; iniciar y
// completar warehouse.receive). Lo usan el detalle del recibo (debajo de sus líneas, con el enlace a la pestaña 'Acomodo
// pendiente') y la pestaña 'Acomodo pendiente' (a la derecha de la lista). Al completar la última, el recibo pasa a Acomodado.
import { useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, EmptyState, IconCheckin, Panel, toast, type DataColumn } from '../../kernel/ui'
import { productLabel, useWarehouseTaskAction, type WarehouseTaskDto } from './api'
import { formatNumber } from './lineRules'
import { useTaskRowActions } from './taskActions'
import { TaskBins, taskBinsSort } from './TaskBins'
import { AssignTaskModal } from './taskDialogs'

const TASK_STATUS_DOMAIN = 'WarehouseTaskStatus'
const NO_TASKS: WarehouseTaskDto[] = []

export interface ReceiptPutawayTasksProps {
  tasks: readonly WarehouseTaskDto[] | null | undefined
  /** Título del panel (por defecto "Tareas de acomodo"). */
  title?: ReactNode
  /** Enlace "Ver todo el acomodo pendiente" (pestaña `?tab=putaway`). */
  queueLink?: boolean
}

export function ReceiptPutawayTasks({ tasks, title, queueLink }: ReceiptPutawayTasksProps) {
  const t = useT()
  const lang = useLang()
  const { rowActions, dialogs } = useTaskRowActions()
  const rows = tasks ?? NO_TASKS
  const action = useWarehouseTaskAction()
  // acciones a nivel del RECIBO: se aplican a todas sus tareas que lo admiten (asignar: abiertas; iniciar: pendientes; completar: abiertas, con la posición sugerida)
  const open = rows.filter((r) => r.statusCode !== 'DONE' && r.statusCode !== 'CANCELLED')
  const pending = rows.filter((r) => r.statusCode === 'PENDING')
  const [assignAll, setAssignAll] = useState(false)
  const [completeAll, setCompleteAll] = useState(false)
  const [busy, setBusy] = useState(false)
  const runAll = async (list: readonly WarehouseTaskDto[], one: (r: WarehouseTaskDto) => Promise<unknown>, okKey: string) => {
    setBusy(true)
    let failed = 0
    let lastError = ''
    for (const r of list) {
      try {
        await one(r)
      } catch (err) {
        failed++
        lastError = applyProblemDetails(err).title
      }
    }
    setBusy(false)
    if (failed === 0) toast.success(t(okKey, { count: list.length }))
    else toast.error(t('warehouse.receipts.bulk.partial', { failed, total: list.length, error: lastError }))
  }
  const columns = useMemo<DataColumn<WarehouseTaskDto>[]>(
    () => [
      {
        id: 'product',
        header: t('warehouse.receipts.detail.columns.product'),
        cell: (r) => productLabel({ sku: r.sku, name: r.productName }),
        card: 'title',
        sortValue: (r) => r.sku,
      },
      {
        id: 'status',
        header: t('warehouse.receipts.columns.status'),
        cell: (r) => <StatusChip domain={TASK_STATUS_DOMAIN} code={r.statusCode} label={r.status} />,
        sortValue: (r) => r.status ?? r.statusCode,
      },
      { id: 'qty', header: t('warehouse.receipts.detail.columns.quantity'), cell: (r) => formatNumber(r.quantity, lang), align: 'end', sortValue: (r) => r.quantity },
      {
        id: 'bins',
        header: t('warehouse.receipts.detail.columns.bins'),
        cell: (r) => <TaskBins task={r} />,
        sortValue: taskBinsSort,
      },
      { id: 'assigned', header: t('warehouse.receipts.detail.columns.assignedTo'), cell: (r) => r.assignedToName ?? '—', sortValue: (r) => r.assignedToName },
    ],
    [t, lang],
  )
  const label = t('warehouse.receipts.detail.putaway')
  return (
    <>
      <Panel
        flush
        className="rcp-tasks"
        icon={<IconCheckin />}
        title={title ?? label}
        badge={rows.length}
        actions={
          <>
            <Can perm="warehouse.manage">
              <button type="button" className="btn sm" disabled={busy || open.length === 0} onClick={() => setAssignAll(true)}>
                {t('warehouse.receipts.bulk.assign')}
              </button>
            </Can>
            <Can perm="warehouse.receive">
              <button type="button" className="btn sm" disabled={busy || pending.length === 0} onClick={() => void runAll(pending, (r) => action.mutateAsync({ id: r.id ?? 0, action: 'start' }), 'warehouse.receipts.bulk.started')}>
                {t('warehouse.receipts.bulk.start')}
              </button>
              <button type="button" className="btn sm flow" disabled={busy || open.length === 0} onClick={() => setCompleteAll(true)}>
                {t('warehouse.receipts.bulk.complete')}
              </button>
            </Can>
            {queueLink && (
              <Link className="btn sm" to="/warehouse/receipts?tab=putaway">
                {t('warehouse.receipts.detail.goTasks')}
              </Link>
            )}
          </>
        }
      >
        <DataTable
          label={label}
          columns={columns}
          rows={rows}
          rowKey={(r) => r.id ?? 0}
          pageSize={25}
          dense
          rowActions={rowActions}
          empty={<EmptyState title={t('warehouse.receipts.putawayTab.noTasks')} />}
        />
      </Panel>
      {dialogs}
      <AssignTaskModal task={open[0] ?? null} tasks={open} open={assignAll} onClose={() => setAssignAll(false)} />
      <ConfirmDialog
        open={completeAll}
        title={t('warehouse.receipts.bulk.completeTitle')}
        message={t('warehouse.receipts.bulk.completeBody', { count: open.length })}
        confirmLabel={t('warehouse.receipts.bulk.complete')}
        onConfirm={() => runAll(open, (r) => action.mutateAsync({ id: r.id ?? 0, action: 'complete', body: { toBinId: null, quantity: r.quantity ?? null, serialNumbers: null } }), 'warehouse.receipts.bulk.completed')}
        onClose={() => setCompleteAll(false)}
      />
    </>
  )
}
