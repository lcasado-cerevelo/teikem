// Lote 13 — "Tareas de acomodo" (PUTAWAY) de un recibo confirmado: se asignan, inician, completan (con la posición
// sugerida) y cancelan aquí mismo con las acciones de `taskActions.tsx` (asignar/cancelar warehouse.manage; iniciar y
// completar warehouse.receive). Lo usan el detalle del recibo (debajo de sus líneas, con el enlace a la pestaña 'Acomodo
// pendiente') y la pestaña 'Acomodo pendiente' (a la derecha de la lista). Al completar la última, el recibo pasa a Acomodado.
import { useMemo, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, EmptyState, IconCheckin, Panel, type DataColumn } from '../../kernel/ui'
import { productLabel, type WarehouseTaskDto } from './api'
import { formatNumber } from './lineRules'
import { useTaskRowActions } from './taskActions'

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
        cell: (r) => [r.fromBinCode, r.toBinCode].filter(Boolean).join(' → ') || '—',
        sortValue: (r) => r.fromBinCode ?? r.toBinCode,
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
          queueLink ? (
            <Link className="btn sm" to="/warehouse/receipts?tab=putaway">
              {t('warehouse.receipts.detail.goTasks')}
            </Link>
          ) : undefined
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
    </>
  )
}
