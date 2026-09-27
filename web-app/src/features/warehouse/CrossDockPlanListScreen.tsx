// Pantalla E (Lote F6) — Cruce de muelle (demo): planes. `/warehouse/cross-dock-plans`. Lectura: inventory.view +
// CROSSDOCK (aplicado por la ruta). Crear plan: warehouse.crossdock.
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { parseApiDate } from '../../kernel/api/dates'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, Field, Filters, Form, Modal, Panel, Select, SearchSelect, toast, type DataColumn } from '../../kernel/ui'
import { useCrossDockAction, useCrossDockPlans, useWarehouseZones, type CrossDockPlanDto } from './api'
import { WarehousePicker, WarehousePickerInput } from './pickers'

const STATUS_DOMAIN = 'CrossDockStatus'

function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
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
  const { data: zones = [] } = useWarehouseZones(warehousePublicId || null, { includeInactive: false })
  const stagingZones = zones.filter((z) => z.zoneTypeCode === 'STAGING')
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

export default function CrossDockPlanListScreen() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<string[]>([])
  const [creating, setCreating] = useState(false)

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
      { id: 'number', header: t('warehouse.crossDockPlans.columns.number'), cell: (p) => <span className="ref">{p.number}</span>, card: 'title' },
      { id: 'warehouse', header: t('warehouse.crossDockPlans.columns.warehouse'), cell: (p) => p.warehouseCode },
      { id: 'status', header: t('warehouse.crossDockPlans.columns.status'), cell: (p) => <StatusChip domain={STATUS_DOMAIN} code={p.statusCode} label={p.status} /> },
      { id: 'allocations', header: t('warehouse.crossDockPlans.columns.allocations'), cell: (p) => p.allocationCount, align: 'end' },
      { id: 'allocatedQty', header: t('warehouse.crossDockPlans.columns.allocatedQty'), cell: (p) => p.allocatedQty, align: 'end' },
      { id: 'movedQty', header: t('warehouse.crossDockPlans.columns.movedQty'), cell: (p) => p.movedQty, align: 'end' },
      { id: 'shortQty', header: t('warehouse.crossDockPlans.columns.shortQty'), cell: (p) => p.shortQty, align: 'end' },
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

      <Filters
        onClear={() => {
          setWarehousePublicId(null)
          setStatusFilter([])
        }}
      >
        <div className="f">
          <label>{t('warehouse.crossDockPlans.filters.warehouse')}</label>
          <WarehousePicker value={warehousePublicId} onChange={setWarehousePublicId} placeholder={t('warehouse.crossDockPlans.filters.anyWarehouse')} />
        </div>
        <SearchSelect
          label={t('warehouse.crossDockPlans.filters.status')}
          options={statusOptions.map((s) => ({ value: s.code, label: s.label }))}
          value={statusFilter}
          onChange={setStatusFilter}
        />
      </Filters>

      <Panel flush title={t('warehouse.crossDockPlans.title')} subtitle={t('warehouse.crossDockPlans.count', { count: data.length })}>
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

      <CreatePlanModal open={creating} onClose={() => setCreating(false)} />
    </div>
  )
}
