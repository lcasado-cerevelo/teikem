// Pantalla E (Lote F6) — Cruce de muelle (demo): citas de muelle. Desde la reconciliación con la maqueta (Fase 3) no tiene
// ítem de menú propio: es la pestaña 'Citas de muelle' de Cruce de muelle (`/warehouse/cross-dock-plans?tab=appointments`;
// `/warehouse/dock-appointments` redirige ahí). Lectura: inventory.view
// + CROSSDOCK (aplicado por la ruta). Agendar/reprogramar/cambiar estatus: warehouse.crossdock. Muelles y avisos de llegada
// son lecturas de WMS_LOTSERIAL (otro módulo): se piden con `handleAccessDenied: false` para que un tenant con CROSSDOCK y
// sin WMS vea la agenda en lugar de 'Módulo apagado'; los ASN solo se piden al elegir vincular uno.
import { useMemo, useState } from 'react'
import { useController, useForm, useFormContext } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { utcFromZonedInput, zonedInputFromUtc } from '../../kernel/api/tenantZone'
import { StatusChip, StatusPipeline, useLookups, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  DataTable,
  DateRangeFilter,
  EMPTY_RANGE,
  Field,
  Filters,
  Form,
  Modal,
  Panel,
  Select,
  SelectFilter,
  SearchSelect,
  TextInput,
  toast,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { useAsns, useDockAppointments, useSaveDockAppointment, useWarehouseDocks, type DockAppointmentDto } from './api'
import { WarehousePicker, WarehousePickerInput } from './pickers'
import { IconSwap } from '../../kernel/ui/screenIcons'
import { formatDateTime as formatCompanyDateTime } from '../../kernel/format'

/** Fecha y hora (`<input type="datetime-local">`; no lo tiene el kit): registrado con `useController` como
 *  WarehousePickerInput/ProductPickerInput en pickers.tsx. Valor de formulario: 'YYYY-MM-DDTHH:mm' en hora de la compañía. */
function DateTimeInput() {
  const info = useFieldInfo('DateTimeInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <input
      id={info.id}
      type="datetime-local"
      value={(field.value as string | undefined) ?? ''}
      onChange={(e) => field.onChange(e.target.value)}
      onBlur={field.onBlur}
      aria-invalid={info.invalid || undefined}
      aria-required={info.required || undefined}
      aria-describedby={info.describedBy}
    />
  )
}

const STATUS_DOMAIN = 'AppointmentStatus'
const ENTITY_TYPE = 'DOCK_APPOINTMENT'
const DIRECTION_DOMAIN = 'DockDirection'

function formatDateTime(iso: string | null | undefined, lang: string): string {
  // fecha corta y hora de la compañía (Región y formatos), en su zona
  return formatCompanyDateTime(iso, lang)
}

/** ISO del API (sin zona = UTC) → valor de un <input type="datetime-local"> en la hora de la COMPAÑÍA (Región y formatos). */
function toLocalInput(iso: string | null | undefined): string {
  return zonedInputFromUtc(iso)
}

/** Valor de un <input type="datetime-local"> (hora de la compañía) → ISO UTC para el request. */
function fromLocalInput(v: string): string | null {
  return v ? utcFromZonedInput(v) : null
}

// ---------------------------------------------------------------------------------------------------------------------
// Alta (agendar)
// ---------------------------------------------------------------------------------------------------------------------
interface CreateFormValues {
  warehousePublicId: string
  dockId: string
  direction: string
  scheduledStartUtc: string
  scheduledEndUtc: string
  linkType: 'none' | 'asn' | 'trip'
  asnId: string
  tripPublicId: string
}

function CreateAppointmentModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const save = useSaveDockAppointment()
  const { data: directions = [] } = useLookups(DIRECTION_DOMAIN)
  const form = useForm<CreateFormValues>({
    defaultValues: { warehousePublicId: '', dockId: '', direction: '', scheduledStartUtc: '', scheduledEndUtc: '', linkType: 'none', asnId: '', tripPublicId: '' },
  })
  const warehousePublicId = form.watch('warehousePublicId')
  const linkType = form.watch('linkType')
  const { data: docks = [] } = useWarehouseDocks(warehousePublicId || null, { includeInactive: false }, { enabled: open, handleAccessDenied: false })
  const { data: allAsns = [] } = useAsns(warehousePublicId ? { warehousePublicId } : {}, {
    enabled: open && linkType === 'asn',
    handleAccessDenied: false,
  })
  // un ASN cancelado no admite cita (DockScheduleRules.AsnNotActive)
  const asns = useMemo(() => allAsns.filter((a) => a.statusCode !== 'CANCELLED'), [allAsns])
  const formId = 'dock-appointment-create'

  const close = () => {
    form.reset({ warehousePublicId: '', dockId: '', direction: '', scheduledStartUtc: '', scheduledEndUtc: '', linkType: 'none', asnId: '', tripPublicId: '' })
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.dockAppointments.new')}
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
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          if (!v.dockId) {
            form.setError('dockId', { message: t('warehouse.dockAppointments.errors.dockRequired') })
            return
          }
          // el API rechaza una cita sin dirección (DockScheduleRules.NormalizeDirection) y, con aviso de llegada, exige INBOUND
          if (!v.direction) {
            form.setError('direction', { message: t('warehouse.dockAppointments.errors.directionRequired') })
            return
          }
          if (v.linkType === 'asn' && v.asnId && v.direction !== 'INBOUND') {
            form.setError('direction', { message: t('warehouse.dockAppointments.errors.asnInboundOnly') })
            return
          }
          if (!v.scheduledStartUtc) {
            form.setError('scheduledStartUtc', { message: t('warehouse.dockAppointments.errors.startRequired') })
            return
          }
          await save.mutateAsync({
            action: 'create',
            body: {
              warehousePublicId: v.warehousePublicId || null,
              dockId: Number(v.dockId),
              direction: v.direction || null,
              scheduledStartUtc: fromLocalInput(v.scheduledStartUtc),
              scheduledEndUtc: fromLocalInput(v.scheduledEndUtc),
              asnId: v.linkType === 'asn' && v.asnId ? Number(v.asnId) : null,
              tripPublicId: v.linkType === 'trip' && v.tripPublicId ? v.tripPublicId : null,
            },
          })
          toast.success(t('warehouse.dockAppointments.created'))
          close()
        }}
      >
        <div className="r2">
          <Field name="warehousePublicId" label={t('warehouse.dockAppointments.fields.warehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="dockId" label={t('warehouse.dockAppointments.fields.dock')} required>
            <Select options={docks.map((d) => ({ value: String(d.id), label: d.code ?? '' }))} placeholder={t('warehouse.dockAppointments.fields.selectDock')} />
          </Field>
        </div>
        <div className="r2">
          <Field name="direction" label={t('warehouse.dockAppointments.fields.direction')} required>
            <Select options={directions.map((d) => ({ value: d.code, label: d.label }))} placeholder={t('warehouse.dockAppointments.fields.selectDirection')} />
          </Field>
        </div>
        <div className="r2">
          <Field name="scheduledStartUtc" label={t('warehouse.dockAppointments.fields.start')} required>
            <DateTimeInput />
          </Field>
          <Field name="scheduledEndUtc" label={t('warehouse.dockAppointments.fields.end')}>
            <DateTimeInput />
          </Field>
        </div>
        <Field name="linkType" label={t('warehouse.dockAppointments.fields.linkType')} help={t('warehouse.dockAppointments.fields.linkHelp')}>
          <Select
            options={[
              { value: 'none', label: t('warehouse.dockAppointments.fields.linkNone') },
              { value: 'asn', label: t('warehouse.dockAppointments.fields.linkAsn') },
              { value: 'trip', label: t('warehouse.dockAppointments.fields.linkTrip') },
            ]}
          />
        </Field>
        {linkType === 'asn' && (
          <Field name="asnId" label={t('warehouse.dockAppointments.fields.asn')}>
            <Select
              options={asns.map((a) => ({ value: String(a.id), label: a.reference ?? String(a.id) }))}
              placeholder={t('warehouse.dockAppointments.fields.selectAsn')}
            />
          </Field>
        )}
        {linkType === 'trip' && (
          <Field name="tripPublicId" label={t('warehouse.dockAppointments.fields.trip')} help={t('warehouse.dockAppointments.fields.tripHelp')}>
            <TextInput />
          </Field>
        )}
      </Form>
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Reprogramar
// ---------------------------------------------------------------------------------------------------------------------
interface ReprogramFormValues {
  dockId: string
  scheduledStartUtc: string
  scheduledEndUtc: string
}

function ReprogramModal({ appointment, open, onClose }: { appointment: DockAppointmentDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const save = useSaveDockAppointment()
  const { data: docks = [] } = useWarehouseDocks(appointment?.warehousePublicId ?? null, { includeInactive: false }, { enabled: open, handleAccessDenied: false })
  const form = useForm<ReprogramFormValues>({
    values: {
      dockId: appointment?.dockId != null ? String(appointment.dockId) : '',
      scheduledStartUtc: toLocalInput(appointment?.scheduledStartUtc),
      scheduledEndUtc: toLocalInput(appointment?.scheduledEndUtc),
    },
  })
  const formId = 'dock-appointment-reprogram'
  if (!appointment) return null

  return (
    <Modal
      open={open}
      title={t('warehouse.dockAppointments.reprogram')}
      onClose={onClose}
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
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          await save.mutateAsync({
            action: 'update',
            id: appointment.id ?? 0,
            body: {
              dockId: v.dockId ? Number(v.dockId) : null,
              scheduledStartUtc: fromLocalInput(v.scheduledStartUtc),
              scheduledEndUtc: fromLocalInput(v.scheduledEndUtc),
            },
          })
          toast.success(t('warehouse.dockAppointments.saved'))
          onClose()
        }}
      >
        <Field name="dockId" label={t('warehouse.dockAppointments.fields.dock')}>
          <Select options={docks.map((d) => ({ value: String(d.id), label: d.code ?? '' }))} placeholder="" />
        </Field>
        <div className="r2">
          <Field name="scheduledStartUtc" label={t('warehouse.dockAppointments.fields.start')}>
            <DateTimeInput />
          </Field>
          <Field name="scheduledEndUtc" label={t('warehouse.dockAppointments.fields.end')}>
            <DateTimeInput />
          </Field>
        </div>
      </Form>
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Cambiar estatus
// ---------------------------------------------------------------------------------------------------------------------
function StatusModal({ appointment, open, onClose }: { appointment: DockAppointmentDto | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const save = useSaveDockAppointment()
  if (!appointment) return null
  return (
    <Modal open={open} title={t('warehouse.dockAppointments.changeStatus')} onClose={onClose} size="sm">
      <StatusPipeline
        domain={STATUS_DOMAIN}
        entityType={ENTITY_TYPE}
        entityId={appointment.id}
        currentCode={appointment.statusCode}
        onTransition={(toCode, comment) => save.mutateAsync({ action: 'status', id: appointment.id ?? 0, body: { status: toCode, comment: comment ?? null } })}
      />
    </Modal>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Pestaña 'Citas de muelle' de Cruce de muelle (CrossDockPlanListScreen, ?tab=appointments)
// ---------------------------------------------------------------------------------------------------------------------
export function DockAppointmentsTab() {
  const t = useT()
  const lang = useLang()
  const canManage = useCan('warehouse.crossdock')
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [dockId, setDockId] = useState('')
  const [statusFilter, setStatusFilter] = useState<string[]>([])
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [creating, setCreating] = useState(false)
  const [reprogramming, setReprogramming] = useState<DockAppointmentDto | null>(null)
  const [changingStatus, setChangingStatus] = useState<DockAppointmentDto | null>(null)

  const { data: docks = [] } = useWarehouseDocks(warehousePublicId, { includeInactive: true }, { handleAccessDenied: false })
  const { data: statusOptions = [] } = useStatuses(STATUS_DOMAIN)

  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId || undefined,
      dockId: dockId ? Number(dockId) : undefined,
      fromUtc: range.from || undefined,
      toUtc: range.to ? `${range.to}T23:59:59` : undefined,
      status: statusFilter.length > 0 ? statusFilter : undefined,
    }),
    [warehousePublicId, dockId, range, statusFilter],
  )
  const { data = [], isLoading, error } = useDockAppointments(query)

  const columns = useMemo<DataColumn<DockAppointmentDto>[]>(
    () => [
      { id: 'dock', header: t('warehouse.dockAppointments.columns.dock'), cell: (a) => <span className="ref">{a.dockCode}</span>, sortValue: (a) => a.dockCode, card: 'title' },
      {
        id: 'direction',
        header: t('warehouse.dockAppointments.columns.direction'),
        cell: (a) => a.direction ?? a.directionCode,
        sortValue: (a) => a.direction ?? a.directionCode,
      },
      {
        id: 'start',
        header: t('warehouse.dockAppointments.columns.start'),
        cell: (a) => formatDateTime(a.scheduledStartUtc, lang),
        sortValue: (a) => a.scheduledStartUtc,
      },
      { id: 'end', header: t('warehouse.dockAppointments.columns.end'), cell: (a) => formatDateTime(a.scheduledEndUtc, lang), sortValue: (a) => a.scheduledEndUtc },
      {
        id: 'status',
        header: t('warehouse.dockAppointments.columns.status'),
        cell: (a) => <StatusChip domain={STATUS_DOMAIN} code={a.statusCode} label={a.status} />,
        sortValue: (a) => a.status ?? a.statusCode,
      },
      {
        id: 'ref',
        header: t('warehouse.dockAppointments.columns.ref'),
        cell: (a) => a.asnReference ?? a.tripCode ?? '—',
        sortValue: (a) => a.asnReference ?? a.tripCode,
      },
    ],
    [t, lang],
  )

  const rowActions = useMemo<RowAction<DockAppointmentDto>[]>(
    () => [
      { key: 'reprogram', label: t('warehouse.dockAppointments.reprogram'), perm: 'warehouse.crossdock', onClick: (a) => setReprogramming(a) },
      { key: 'status', label: t('warehouse.dockAppointments.changeStatus'), perm: 'warehouse.crossdock', onClick: (a) => setChangingStatus(a) },
    ],
    [t],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setWarehousePublicId(null)
          setDockId('')
          setStatusFilter([])
          setRange(EMPTY_RANGE)
        }}
      >
        <div className="f">
          <label>{t('warehouse.dockAppointments.filters.warehouse')}</label>
          <WarehousePicker
            value={warehousePublicId}
            onChange={setWarehousePublicId}
            placeholder={t('warehouse.dockAppointments.filters.anyWarehouse')}
            filterLabel={t('warehouse.dockAppointments.filters.warehouse')}
          />
        </div>
        <SelectFilter
          label={t('warehouse.dockAppointments.filters.dock')}
          value={dockId}
          onChange={setDockId}
          options={docks.map((d) => ({ value: String(d.id), label: d.code ?? '' }))}
          allLabel={t('warehouse.dockAppointments.filters.anyDock')}
        />
        <SearchSelect
          label={t('warehouse.dockAppointments.filters.status')}
          options={statusOptions.map((s) => ({ value: s.code, label: s.label }))}
          value={statusFilter}
          onChange={setStatusFilter}
        />
        <DateRangeFilter label={t('warehouse.dockAppointments.filters.range')} value={range} onChange={setRange} />
      </Filters>

      <Panel
        flush
        icon={<IconSwap />}
        title={t('warehouse.dockAppointments.title')}
        badge={data.length}
        actions={
          <Can perm="warehouse.crossdock">
            <button type="button" className="btn flow sm" onClick={() => setCreating(true)}>
              {t('warehouse.dockAppointments.new')}
            </button>
          </Can>
        }
      >
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.dockAppointments.title')}
            columns={columns}
            rows={data}
            rowKey={(a) => a.id ?? 0}
            defaultSort={{ id: 'start', desc: false }}
            loading={isLoading}
            rowActions={canManage ? rowActions : []}
          />
        )}
      </Panel>

      <CreateAppointmentModal open={creating} onClose={() => setCreating(false)} />
      <ReprogramModal appointment={reprogramming} open={reprogramming !== null} onClose={() => setReprogramming(null)} />
      <StatusModal appointment={changingStatus} open={changingStatus !== null} onClose={() => setChangingStatus(null)} />
    </>
  )
}
