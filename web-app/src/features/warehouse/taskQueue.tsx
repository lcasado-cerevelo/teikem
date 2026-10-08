// Tareas de almacén (Lote F6; desde la reconciliación con la maqueta, Fase 3, sin pantalla ni ítem de menú propio).
// La maqueta no tiene "Tareas de almacén": cada tipo se trabaja en la pantalla a la que pertenece —
//   PUTAWAY (acomodo)      → Recibo (pestaña 'Acomodo pendiente' y la ficha del recibo),
//   REPLENISH (reabasto)   → Recolección y empaque (pestaña 'Reabasto', con 'Correr reabasto'),
//   COUNT (conteo)         → Conteo cíclico (Lote 14: sin pestaña; se asigna con el ícono de la lista y se cierra al confirmar),
//   CROSSDOCK (cruce)      → Cruce de muelle (pestaña 'Tareas de cruce'; completar = mover la asignación).
// PICK, PACK y LOAD existen en el catálogo pero el API no tiene handler para ellos (D41): no nacen tareas de esos tipos.
// Permisos: lectura inventory.view + WMS_LOTSERIAL (la cola exige ese módulo). Asignar/cancelar: warehouse.manage. Iniciar y
// Completar: el permiso del handler de cada tipo (PUTAWAY → warehouse.receive, REPLENISH → warehouse.pick, COUNT →
// warehouse.count, CROSSDOCK → warehouse.crossdock); Completar solo cuando `completableFromQueue`. Correr reabasto:
// warehouse.pick. Cancelar solo PUTAWAY y REPLENISH (WarehouseTaskRules.CancelableFromQueue). Acciones y diálogos por fila:
// taskActions.tsx (`useTaskRowActions`).
import { useId, useMemo, useState, type ReactNode } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, Filters, Modal, Panel, SearchSelect, toast, type DataColumn } from '../../kernel/ui'
import { exportWarehouseTasks, productLabel, useRunReplenishment, useWarehouseTasks, type WarehouseTaskDto } from './api'
import { ToggleFilter } from './filterControls'
import { formatDateTime, formatNumber } from './lineRules'
import { WarehousePicker } from './pickers'
import { useTaskRowActions, type WarehouseTaskType } from './taskActions'
import { TaskBins, taskBinsSort } from './TaskBins'

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'WarehouseTaskStatus'
const NO_ROWS: never[] = []


// ---------------------------------------------------------------------------------------------------------------------
// Correr reabasto (warehouse.pick): botón + diálogo
// ---------------------------------------------------------------------------------------------------------------------
function ReplenishModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const run = useRunReplenishment()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)

  const close = () => {
    setWarehousePublicId(null)
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.tasks.replenish.title')}
      onClose={close}
      size="sm"
      dismissible={!run.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="btn flow"
            disabled={run.isPending}
            onClick={async () => {
              try {
                const result = await run.mutateAsync({ warehousePublicId })
                toast.success(t('warehouse.tasks.replenish.result', { count: result.tasksCreated ?? 0 }))
                close()
              } catch (err) {
                toast.error(applyProblemDetails(err).title)
              }
            }}
          >
            {run.isPending ? t('common.loading') : t('warehouse.tasks.replenish.run')}
          </button>
        </>
      }
    >
      <div className="f">
        <label>{t('warehouse.tasks.replenish.warehouse')}</label>
        <WarehousePicker value={warehousePublicId} onChange={setWarehousePublicId} placeholder={t('warehouse.tasks.replenish.anyWarehouse')} />
      </div>
    </Modal>
  )
}

/** 'Correr reabasto' (solo con warehouse.pick): genera tareas REPLENISH para las posiciones de surtido bajo su mínimo. */
export function ReplenishButton({ className = 'btn flow' }: { className?: string }) {
  const t = useT()
  const [open, setOpen] = useState(false)
  return (
    <Can perm="warehouse.pick">
      <button type="button" className={className} onClick={() => setOpen(true)}>
        {t('warehouse.tasks.replenish.run')}
      </button>
      <ReplenishModal open={open} onClose={() => setOpen(false)} />
    </Can>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Cola de tareas de uno o más tipos (la pestaña de tareas de cada pantalla)
// ---------------------------------------------------------------------------------------------------------------------
export interface TaskQueueProps {
  /** Tipos de tarea que muestra la cola (el API filtra con `types`). */
  types: readonly WarehouseTaskType[]
  /** Título del panel (ya traducido). */
  title: string
  /** Ícono del panel: el de la pantalla que la contiene. */
  icon?: ReactNode
  /** Acciones de la cabecera del panel (p. ej. `<ReplenishButton className="btn flow sm" />`). */
  actions?: ReactNode
  /** false = un 403 de la cola no saca al usuario de la pantalla (cola pedida desde otro módulo, p. ej. CROSSDOCK). */
  handleAccessDenied?: boolean
}

/**
 * Cola paginada en el servidor de los tipos indicados: filtros (almacén, estatus, asignadas a mí, incluir cerradas), tabla
 * con prioridad, producto, cantidad, posiciones, referencia y asignado, y las acciones de `useTaskRowActions`.
 * `<TaskQueue types={['PUTAWAY']} title={t('warehouse.tasks.putawayTitle')} icon={<IconCheckin />} />`
 */
export function TaskQueue({ types, title, icon, actions, handleAccessDenied }: TaskQueueProps) {
  const t = useT()
  const warehouseId = useId()
  const lang = useLang()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<string[]>([])
  const [assignedToMe, setAssignedToMe] = useState(false)
  const [includeClosed, setIncludeClosed] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const { rowActions, dialogs } = useTaskRowActions()

  const { data: statusOptions = [] } = useStatuses(STATUS_DOMAIN)

  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }

  const typesKey = types.join(',')
  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId || undefined,
      types: typesKey ? typesKey.split(',') : undefined,
      status: statusFilter.length > 0 ? statusFilter : undefined,
      assignedToMe: assignedToMe || undefined,
      includeClosed: includeClosed || undefined,
      skip: (page - 1) * pageSize,
      take: pageSize,
    }),
    [warehousePublicId, typesKey, statusFilter, assignedToMe, includeClosed, page, pageSize],
  )
  const { data, isLoading, error } = useWarehouseTasks(query, { handleAccessDenied })

  // Llega en el orden de la cola (prioridad y antigüedad). El API no recibe parámetro de orden: el orden por encabezado
  // es en el cliente y solo reacomoda la página visible.
  const columns = useMemo<DataColumn<WarehouseTaskDto>[]>(
    () => [
      {
        id: 'product',
        header: t('warehouse.tasks.columns.product'),
        // una tarea COUNT no tiene producto: su título (en tarjetas) es la referencia (número de conteo)
        cell: (r) => (r.sku || r.productName ? productLabel({ sku: r.sku, name: r.productName }) : (r.refLabel ?? '—')),
        sortValue: (r) => r.sku || r.productName || r.refLabel,
        card: 'title',
      },
      ...(types.length > 1
        ? [
            {
              id: 'type',
              header: t('warehouse.tasks.columns.type'),
              cell: (r: WarehouseTaskDto) => r.type ?? r.typeCode,
              sortValue: (r: WarehouseTaskDto) => r.type ?? r.typeCode,
            },
          ]
        : []),
      {
        id: 'status',
        header: t('warehouse.tasks.columns.status'),
        cell: (r) => <StatusChip domain={STATUS_DOMAIN} code={r.statusCode} label={r.status} />,
        sortValue: (r) => r.status ?? r.statusCode,
      },
      { id: 'priority', header: t('warehouse.tasks.columns.priority'), cell: (r) => r.priority, sortValue: (r) => r.priority, align: 'end' },
      { id: 'warehouse', header: t('warehouse.tasks.columns.warehouse'), cell: (r) => r.warehouseCode, sortValue: (r) => r.warehouseCode },
      {
        id: 'quantity',
        header: t('warehouse.tasks.columns.quantity'),
        cell: (r) => (r.quantity != null ? formatNumber(r.quantity, lang) : '—'),
        sortValue: (r) => r.quantity,
        align: 'end',
      },
      {
        id: 'bins',
        header: t('warehouse.tasks.columns.bins'),
        cell: (r) => <TaskBins task={r} />,
        sortValue: taskBinsSort,
      },
      { id: 'ref', header: t('warehouse.tasks.columns.ref'), cell: (r) => r.refLabel ?? '—', sortValue: (r) => r.refLabel },
      {
        id: 'assignedTo',
        header: t('warehouse.tasks.columns.assignedTo'),
        cell: (r) => r.assignedToName ?? t('warehouse.tasks.unassigned'),
        sortValue: (r) => r.assignedToName ?? t('warehouse.tasks.unassigned'),
      },
      {
        id: 'createdAt',
        header: t('warehouse.tasks.columns.createdAt'),
        cell: (r) => formatDateTime(r.createdAtUtc, lang),
        sortValue: (r) => r.createdAtUtc,
        card: 'hidden',
      },
    ],
    [t, lang, types.length],
  )

  const statusSelectOptions = useMemo(() => statusOptions.map((o) => ({ value: o.code, label: o.label })), [statusOptions])

  return (
    <>
      <Filters
        onClear={() => {
          setPage(1)
          setWarehousePublicId(null)
          setStatusFilter([])
          setAssignedToMe(false)
          setIncludeClosed(false)
        }}
      >
        <div className="f">
          <label htmlFor={warehouseId}>{t('warehouse.tasks.filters.warehouse')}</label>
          <WarehousePicker
            id={warehouseId}
            value={warehousePublicId}
            onChange={withPageReset(setWarehousePublicId)}
            placeholder={t('warehouse.tasks.filters.anyWarehouse')}
            filterLabel={t('warehouse.tasks.filters.warehouse')}
          />
        </div>
        <SearchSelect label={t('warehouse.tasks.filters.status')} options={statusSelectOptions} value={statusFilter} onChange={withPageReset(setStatusFilter)} />
        <ToggleFilter label={t('warehouse.tasks.filters.assignedToMe')} checked={assignedToMe} onChange={withPageReset(setAssignedToMe)} />
        <ToggleFilter label={t('warehouse.tasks.filters.includeClosed')} checked={includeClosed} onChange={withPageReset(setIncludeClosed)} />
      </Filters>

      <Panel flush icon={icon} title={title} badge={data ? (data.total ?? 0) : undefined} actions={actions}>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={title}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(r) => r.id ?? 0}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportWarehouseTasks(query)}
            rowActions={rowActions}
          />
        )}
      </Panel>

      {dialogs}
    </>
  )
}
