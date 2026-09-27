// Pantalla E (Lote F6) — Tareas de almacén: cola unificada. `/warehouse/tasks`. Lectura: inventory.view + WMS_LOTSERIAL
// (aplicado por la ruta). Asignar/cancelar: warehouse.manage. Iniciar: el permiso del handler de cada tipo (PUTAWAY →
// warehouse.receive, REPLENISH → warehouse.pick, COUNT → warehouse.count, CROSSDOCK → warehouse.crossdock). Completar:
// el mismo permiso por tipo (WarehouseTaskService.CompleteAsync exige el del handler) y solo cuando `completableFromQueue`
// (COUNT y CROSSDOCK se completan desde su propia pantalla). Correr reabasto: warehouse.pick.
import { useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { parseApiDate } from '../../kernel/api/dates'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  ConfirmDialog,
  DataTable,
  Field,
  Filters,
  Form,
  Modal,
  NumberInput,
  Panel,
  Select,
  SearchSelect,
  TextArea,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import {
  useRunReplenishment,
  usePutawaySuggestions,
  useWarehouseBins,
  useWarehouseTaskAction,
  useWarehouseTasks,
  productLabel,
  type WarehouseTaskDto,
} from './api'
import { WarehousePicker } from './pickers'

const PAGE_SIZE = 25
const TYPE_DOMAIN = 'WarehouseTaskType'
const STATUS_DOMAIN = 'WarehouseTaskStatus'

/** Permiso del handler de 'Iniciar' y 'Completar' según el tipo (el servidor lo vuelve a validar en StartAsync/CompleteAsync). */
const START_PERM: Record<string, string> = {
  PUTAWAY: 'warehouse.receive',
  REPLENISH: 'warehouse.pick',
  COUNT: 'warehouse.count',
  CROSSDOCK: 'warehouse.crossdock',
}
const CANCELLABLE_TYPES = new Set(['PUTAWAY', 'REPLENISH'])

function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

/** Filtro booleano fuera de un <Form> (como en InventoryScreen). */
function ToggleFilter({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="f">
      <label className="sw">
        <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{label}</span>
      </label>
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Asignar (Select de usuario del tenant; GET /api/v1/users exige admin.users — sin ese permiso se avisa sin sacar de la
// pantalla, como ClientPicker/WarehousePicker).
// ---------------------------------------------------------------------------------------------------------------------
function AssignModal({ task, open, onClose }: { task: WarehouseTaskDto | null; open: boolean; onClose: () => void }) {
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
  const close = () => onClose()

  return (
    <Modal
      open={open}
      title={t('warehouse.tasks.assign.title')}
      onClose={close}
      size="sm"
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
      {users.error ? (
        <p className="note">{t('warehouse.tasks.assign.noAccess')}</p>
      ) : (
        <Form
          id={formId}
          form={form}
          onSubmit={async (v) => {
            await action.mutateAsync({ id: task.id ?? 0, action: 'assign', body: { userId: v.userId ? Number(v.userId) : null } })
            toast.success(t('warehouse.tasks.assign.saved'))
            close()
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
  /** Id de la posición destino como texto (valor del Select); '' = sin elegir. */
  toBinId: string
  quantity: number | null
  serialNumbers: string
}

function CompleteModal({ task, open, onClose }: { task: WarehouseTaskDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const action = useWarehouseTaskAction()
  const isPutaway = task?.typeCode === 'PUTAWAY'
  const suggestions = usePutawaySuggestions({ taskId: task?.id }, { enabled: open && isPutaway && Boolean(task?.id) })
  // Posiciones activas del almacén de la tarea: el usuario elige por código y se envía el id (nunca un número a mano).
  const { data: bins = [] } = useWarehouseBins(task?.warehousePublicId ?? null, {}, { enabled: open, handleAccessDenied: false })
  const binOptions = useMemo(() => {
    const suggested = new Set((suggestions.data ?? []).map((s) => s.binId))
    const label = (code: string | null | undefined, zone: string | null | undefined, id: number | undefined) =>
      `${code ?? ''}${zone ? ` · ${zone}` : ''}${suggested.has(id) ? ` (${t('warehouse.tasks.complete.suggested')})` : ''}`
    const active = bins.filter((b) => b.isActive !== false)
    // las sugeridas primero, en el orden del servidor
    const first = (suggestions.data ?? [])
      .filter((s) => s.binId != null)
      .map((s) => ({ value: String(s.binId), label: label(s.binCode, s.zoneCode, s.binId) }))
    const rest = active.filter((b) => !suggested.has(b.id)).map((b) => ({ value: String(b.id), label: label(b.code, b.zoneCode, b.id) }))
    return [...first, ...rest]
  }, [bins, suggestions.data, t])
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
            <Select options={binOptions} placeholder={t('warehouse.tasks.complete.anyBin')} />
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

// ---------------------------------------------------------------------------------------------------------------------
// Correr reabasto
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

// ---------------------------------------------------------------------------------------------------------------------
// Pantalla
// ---------------------------------------------------------------------------------------------------------------------
export default function WarehouseTaskListScreen() {
  const t = useT()
  const lang = useLang()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [types, setTypes] = useState<string[]>([])
  const [statusFilter, setStatusFilter] = useState<string[]>([])
  const [assignedToMe, setAssignedToMe] = useState(false)
  const [includeClosed, setIncludeClosed] = useState(false)
  const [page, setPage] = useState(1)
  const [assignTask, setAssignTask] = useState<WarehouseTaskDto | null>(null)
  const [completeTask, setCompleteTask] = useState<WarehouseTaskDto | null>(null)
  const [cancelTask, setCancelTask] = useState<WarehouseTaskDto | null>(null)
  const [replenishing, setReplenishing] = useState(false)

  const { data: typeOptions = [] } = useLookups(TYPE_DOMAIN)
  const { data: statusOptions = [] } = useStatuses(STATUS_DOMAIN)
  const action = useWarehouseTaskAction()

  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }
  const changeWarehouse = withPageReset(setWarehousePublicId)
  const changeTypes = withPageReset(setTypes)
  const changeStatus = withPageReset(setStatusFilter)
  const changeAssignedToMe = withPageReset(setAssignedToMe)
  const changeIncludeClosed = withPageReset(setIncludeClosed)

  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId || undefined,
      types: types.length > 0 ? types : undefined,
      status: statusFilter.length > 0 ? statusFilter : undefined,
      assignedToMe: assignedToMe || undefined,
      includeClosed: includeClosed || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [warehousePublicId, types, statusFilter, assignedToMe, includeClosed, page],
  )
  const { data, isLoading, error } = useWarehouseTasks(query)

  const startMutation = useMutation({
    mutationFn: (id: number) => action.mutateAsync({ id, action: 'start' }),
    onSuccess: () => toast.success(t('warehouse.tasks.started')),
    onError: (err) => toast.error(applyProblemDetails(err).title),
  })

  // Orden en cliente sobre la página cargada (el API no recibe parámetro de orden).
  const columns = useMemo<DataColumn<WarehouseTaskDto>[]>(
    () => [
      { id: 'type', header: t('warehouse.tasks.columns.type'), cell: (r) => r.type ?? r.typeCode, sortValue: (r) => r.type ?? r.typeCode, card: 'title' },
      {
        id: 'status',
        header: t('warehouse.tasks.columns.status'),
        cell: (r) => <StatusChip domain={STATUS_DOMAIN} code={r.statusCode} label={r.status} />,
        sortValue: (r) => r.status ?? r.statusCode,
      },
      { id: 'priority', header: t('warehouse.tasks.columns.priority'), cell: (r) => r.priority, sortValue: (r) => r.priority, align: 'end' },
      { id: 'warehouse', header: t('warehouse.tasks.columns.warehouse'), cell: (r) => r.warehouseCode, sortValue: (r) => r.warehouseCode },
      {
        id: 'product',
        header: t('warehouse.tasks.columns.product'),
        cell: (r) => productLabel({ sku: r.sku, name: r.productName }),
        sortValue: (r) => r.sku,
      },
      { id: 'quantity', header: t('warehouse.tasks.columns.quantity'), cell: (r) => r.quantity, sortValue: (r) => r.quantity, align: 'end' },
      {
        id: 'bins',
        header: t('warehouse.tasks.columns.bins'),
        cell: (r) => [r.fromBinCode, r.toBinCode].filter(Boolean).join(' → ') || '—',
        sortValue: (r) => r.fromBinCode ?? r.toBinCode,
      },
      { id: 'ref', header: t('warehouse.tasks.columns.ref'), cell: (r) => r.refLabel, sortValue: (r) => r.refLabel },
      {
        id: 'assignedTo',
        header: t('warehouse.tasks.columns.assignedTo'),
        cell: (r) => r.assignedToName ?? t('warehouse.tasks.unassigned'),
        sortValue: (r) => r.assignedToName,
      },
      {
        id: 'createdAt',
        header: t('warehouse.tasks.columns.createdAt'),
        cell: (r) => formatDateTime(r.createdAtUtc, lang),
        sortValue: (r) => r.createdAtUtc,
        card: 'hidden',
      },
    ],
    [t, lang],
  )

  const startActions = useMemo<RowAction<WarehouseTaskDto>[]>(
    () =>
      Object.entries(START_PERM).map(([typeCode, perm]) => ({
        key: `start-${typeCode}`,
        label: t('warehouse.tasks.actions.start'),
        perm,
        visible: (r) => r.typeCode === typeCode && r.statusCode === 'PENDING',
        onClick: (r) => startMutation.mutate(r.id ?? 0),
      })),
    [t, startMutation],
  )

  // Completar: mismo permiso por tipo que Iniciar (el servidor exige el del handler; sin él respondería 403).
  const completeActions = useMemo<RowAction<WarehouseTaskDto>[]>(
    () =>
      Object.entries(START_PERM).map(([typeCode, perm]) => ({
        key: `complete-${typeCode}`,
        label: t('warehouse.tasks.actions.complete'),
        perm,
        visible: (r) => r.typeCode === typeCode && r.completableFromQueue === true,
        onClick: (r) => setCompleteTask(r),
      })),
    [t],
  )

  const rowActions = useMemo<RowAction<WarehouseTaskDto>[]>(
    () => [
      {
        key: 'assign',
        label: t('warehouse.tasks.actions.assign'),
        perm: 'warehouse.manage',
        visible: (r) => r.statusCode !== 'DONE' && r.statusCode !== 'CANCELLED',
        onClick: (r) => setAssignTask(r),
      },
      ...startActions,
      ...completeActions,
      {
        key: 'cancel',
        label: t('warehouse.tasks.actions.cancel'),
        perm: 'warehouse.manage',
        visible: (r) => CANCELLABLE_TYPES.has(r.typeCode ?? '') && r.statusCode !== 'DONE' && r.statusCode !== 'CANCELLED',
        onClick: (r) => setCancelTask(r),
        tone: 'danger',
      },
    ],
    [t, startActions, completeActions],
  )

  const typeSelectOptions = useMemo(() => typeOptions.map((o) => ({ value: o.code, label: o.label })), [typeOptions])
  const statusSelectOptions = useMemo(() => statusOptions.map((o) => ({ value: o.code, label: o.label })), [statusOptions])

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.tasks.title')}</h1>
          <p>{t('warehouse.tasks.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.pick">
            <button type="button" className="btn flow" onClick={() => setReplenishing(true)}>
              {t('warehouse.tasks.replenish.run')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setWarehousePublicId(null)
          setTypes([])
          setStatusFilter([])
          setAssignedToMe(false)
          setIncludeClosed(false)
        }}
      >
        <div className="f">
          <label>{t('warehouse.tasks.filters.warehouse')}</label>
          <WarehousePicker value={warehousePublicId} onChange={changeWarehouse} placeholder={t('warehouse.tasks.filters.anyWarehouse')} />
        </div>
        <SearchSelect label={t('warehouse.tasks.filters.types')} options={typeSelectOptions} value={types} onChange={changeTypes} />
        <SearchSelect label={t('warehouse.tasks.filters.status')} options={statusSelectOptions} value={statusFilter} onChange={changeStatus} />
        <ToggleFilter label={t('warehouse.tasks.filters.assignedToMe')} checked={assignedToMe} onChange={changeAssignedToMe} />
        <ToggleFilter label={t('warehouse.tasks.filters.includeClosed')} checked={includeClosed} onChange={changeIncludeClosed} />
      </Filters>

      <Panel flush title={t('warehouse.tasks.title')} subtitle={data ? t('warehouse.tasks.count', { count: data.total ?? 0 }) : undefined}>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.tasks.title')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(r) => r.id ?? 0}
            loading={isLoading}
            page={page}
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            rowActions={rowActions}
          />
        )}
      </Panel>

      <AssignModal task={assignTask} open={assignTask !== null} onClose={() => setAssignTask(null)} />
      <CompleteModal task={completeTask} open={completeTask !== null} onClose={() => setCompleteTask(null)} />
      <ReplenishModal open={replenishing} onClose={() => setReplenishing(false)} />

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
    </div>
  )
}
