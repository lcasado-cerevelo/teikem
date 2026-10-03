// Pantalla E (Lote F6) — Cruce de muelle (demo): planes. `/warehouse/cross-dock-plans`. Lectura: inventory.view +
// CROSSDOCK (aplicado por la ruta). Crear plan: warehouse.crossdock. Pestañas (?tab=appointments|tasks): 'Citas de muelle'
// (DockAppointmentsTab.tsx) y 'Tareas de cruce' (cola CROSSDOCK de taskQueue.tsx; completar = mover la asignación).
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Can, ModuleKeys, useModule } from '../../kernel/access'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, Field, Filters, Form, Modal, Panel, Select, SearchSelect, Tabs, toast, type DataColumn } from '../../kernel/ui'
import { useCrossDockAction, useCrossDockPlans, useWarehouseZones, type CrossDockPlanDto } from './api'
import { DockAppointmentsTab } from './DockAppointmentsTab'
import { WarehousePicker, WarehousePickerInput } from './pickers'
import { TaskQueue } from './taskQueue'
import { IconSwap } from '../../kernel/ui/screenIcons'
import { formatDateTime as formatCompanyDateTime } from '../../kernel/format'

const STATUS_DOMAIN = 'CrossDockStatus'
const STAGING_ZONE_TYPES = new Set(['STAGING', 'CROSSDOCK'])

function formatDateTime(iso: string | null | undefined, lang: string): string {
  // fecha corta y hora de la compañía (Región y formatos), en su zona
  return formatCompanyDateTime(iso, lang)
}

interface CreateFormValues {
  warehousePublicId: string
  stagingZoneId: string
}

function CreatePlanModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const navigate = useNavigate()
  const action = useCrossDockAction()
  const form = useForm<CreateFormValues>({ defaultValues: { warehousePublicId: '', stagingZoneId: '' } })
  const warehousePublicId = form.watch('warehousePublicId')
  // Zonas (lectura de WMS_LOTSERIAL, otro módulo): sin acceso no se saca al usuario de la pantalla.
  const { data: zones = [] } = useWarehouseZones(warehousePublicId || null, { includeInactive: false }, { enabled: open, handleAccessDenied: false })
  // CrossDockRules.IsStagingZoneType acepta zonas STAGING y CROSSDOCK
  const stagingZones = zones.filter((z) => STAGING_ZONE_TYPES.has(z.zoneTypeCode ?? ''))
  const formId = 'cross-dock-plan-create'

  const close = () => {
    form.reset({ warehousePublicId: '', stagingZoneId: '' })
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.crossDockPlans.new')}
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
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const created = await action.mutateAsync({
            action: 'create',
            body: { warehousePublicId: v.warehousePublicId || null, stagingZoneId: v.stagingZoneId ? Number(v.stagingZoneId) : null },
          })
          toast.success(t('warehouse.crossDockPlans.created'))
          close()
          if (created.id != null) navigate(`/warehouse/cross-dock-plans/${created.id}`)
        }}
      >
        <Field name="warehousePublicId" label={t('warehouse.crossDockPlans.fields.warehouse')} required>
          <WarehousePickerInput />
        </Field>
        <Field name="stagingZoneId" label={t('warehouse.crossDockPlans.fields.stagingZone')} help={t('warehouse.crossDockPlans.fields.stagingZoneHelp')}>
          <Select options={stagingZones.map((z) => ({ value: String(z.id), label: z.code ?? '' }))} placeholder="" />
        </Field>
      </Form>
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Pestaña Planes
// ---------------------------------------------------------------------------------------------------------------------
function PlansTab() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<string[]>([])

  const { data: statusOptions = [] } = useStatuses(STATUS_DOMAIN)
  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId || undefined,
      status: statusFilter.length > 0 ? statusFilter : undefined,
    }),
    [warehousePublicId, statusFilter],
  )
  const { data = [], isLoading, error } = useCrossDockPlans(query)

  const columns = useMemo<DataColumn<CrossDockPlanDto>[]>(
    () => [
      { id: 'number', header: t('warehouse.crossDockPlans.columns.number'), cell: (p) => <span className="ref">{p.number}</span>, sortValue: (p) => p.number, card: 'title' },
      { id: 'warehouse', header: t('warehouse.crossDockPlans.columns.warehouse'), cell: (p) => p.warehouseCode, sortValue: (p) => p.warehouseCode },
      {
        id: 'status',
        header: t('warehouse.crossDockPlans.columns.status'),
        cell: (p) => <StatusChip domain={STATUS_DOMAIN} code={p.statusCode} label={p.status} />,
        sortValue: (p) => p.status ?? p.statusCode,
      },
      { id: 'allocations', header: t('warehouse.crossDockPlans.columns.allocations'), cell: (p) => p.allocationCount, sortValue: (p) => p.allocationCount, align: 'end' },
      { id: 'allocatedQty', header: t('warehouse.crossDockPlans.columns.allocatedQty'), cell: (p) => p.allocatedQty, sortValue: (p) => p.allocatedQty, align: 'end' },
      { id: 'movedQty', header: t('warehouse.crossDockPlans.columns.movedQty'), cell: (p) => p.movedQty, sortValue: (p) => p.movedQty, align: 'end' },
      { id: 'shortQty', header: t('warehouse.crossDockPlans.columns.shortQty'), cell: (p) => p.shortQty, sortValue: (p) => p.shortQty, align: 'end' },
      {
        id: 'createdAt',
        header: t('warehouse.crossDockPlans.columns.createdAt'),
        cell: (p) => formatDateTime(p.createdAtUtc, lang),
        sortValue: (p) => p.createdAtUtc,
        card: 'hidden',
      },
    ],
    [t, lang],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setWarehousePublicId(null)
          setStatusFilter([])
        }}
      >
        <div className="f">
          <label>{t('warehouse.crossDockPlans.filters.warehouse')}</label>
          <WarehousePicker
            value={warehousePublicId}
            onChange={setWarehousePublicId}
            placeholder={t('warehouse.crossDockPlans.filters.anyWarehouse')}
            filterLabel={t('warehouse.crossDockPlans.filters.warehouse')}
          />
        </div>
        <SearchSelect
          label={t('warehouse.crossDockPlans.filters.status')}
          options={statusOptions.map((s) => ({ value: s.code, label: s.label }))}
          value={statusFilter}
          onChange={setStatusFilter}
        />
      </Filters>

      <Panel flush icon={<IconSwap />} title={t('warehouse.crossDockPlans.title')} badge={data.length}>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.crossDockPlans.title')}
            columns={columns}
            rows={data}
            rowKey={(p) => p.id ?? 0}
            defaultSort={{ id: 'createdAt', desc: true }}
            loading={isLoading}
            onRowClick={(p) => navigate(`/warehouse/cross-dock-plans/${p.id}`)}
          />
        )}
      </Panel>
    </>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Pantalla: pestañas Planes, Citas de muelle (sin ítem de menú propio: la maqueta solo tiene 'Cruce de muelle') y Tareas
// de cruce (tareas CROSSDOCK, antes en 'Tareas de almacén'; la cola exige WMS_LOTSERIAL, así que la pestaña solo aparece
// con ese módulo y su 403 no saca de la pantalla).
// ---------------------------------------------------------------------------------------------------------------------
const TAB_KEYS = ['plans', 'appointments', 'tasks'] as const
type TabKey = (typeof TAB_KEYS)[number]
const isTabKey = (v: string | null): v is TabKey => (TAB_KEYS as readonly string[]).includes(v ?? '')
const CROSSDOCK_TYPES = ['CROSSDOCK'] as const

export default function CrossDockPlanListScreen() {
  const t = useT()
  const hasWms = useModule(ModuleKeys.WmsLotSerial)
  const visibleTabs = TAB_KEYS.filter((k) => k !== 'tasks' || hasWms)
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab')
  const requested: TabKey = isTabKey(raw) ? raw : 'plans'
  const tab: TabKey = visibleTabs.includes(requested) ? requested : 'plans'
  const setTab = (key: TabKey) => setParams(key === 'plans' ? {} : { tab: key }, { replace: true })
  const [creating, setCreating] = useState(false)

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.crossDockPlans.title')}</h1>
          <p>{t('warehouse.crossDockPlans.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.crossdock">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.crossDockPlans.new')}
            </button>
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.crossDockPlans.title')}
          value={tab}
          onChange={setTab}
          tabs={visibleTabs.map((key) => ({ key, label: t(`warehouse.crossDockPlans.tabs.${key}`) }))}
        />
      </div>

      {tab === 'plans' && <PlansTab />}
      {tab === 'appointments' && <DockAppointmentsTab />}
      {tab === 'tasks' && (
        <TaskQueue types={CROSSDOCK_TYPES} title={t('warehouse.crossDockPlans.tasksTitle')} icon={<IconSwap />} handleAccessDenied={false} />
      )}

      <CreatePlanModal open={creating} onClose={() => setCreating(false)} />
    </div>
  )
}
