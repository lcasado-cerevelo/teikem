// Pieza "Conteo cíclico" (Lote F6) — lista de conteos. `/warehouse/cycle-counts`.
// Lectura de la lista: inventory.view + WMS_LOTSERIAL (por la ruta). La ficha, el alta y todo el conteo (captura, terminar,
// refrescar, reconciliar, eliminar): warehouse.count. Sin filtros el conteo toma todo el saldo en mano del almacén
// (máx. 1000 líneas). Manual 06 §6.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useController, useForm, useFormContext, useWatch } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  DateRangeFilter,
  EMPTY_RANGE,
  Field,
  Filters,
  Form,
  Modal,
  Panel,
  QBox,
  SearchMultiSelect,
  SearchSelect,
  toast,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { useCreateCycleCount, useCycleCounts, useWarehouseBins, useWarehouseZones, useWarehouses, warehouseLabel, type CycleCountDto } from './api'
import { formatDateTime, formatNumber, useDebounced } from './lineRules'
import { ProductPicker, WarehousePickerInput } from './pickers'

const STATUS_DOMAIN = 'CycleCountStatus'
const PAGE_SIZE = 25

/** SearchMultiSelect dentro de un <Field name="…">: el valor del formulario es un arreglo de strings. */
function MultiSelectInput({ options, placeholder, disabled }: { options: readonly { value: string; label: string }[]; placeholder?: string; disabled?: boolean }) {
  const info = useFieldInfo('MultiSelectInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <SearchMultiSelect
      id={info.id}
      options={options}
      value={(field.value as string[] | undefined) ?? []}
      onChange={(v) => field.onChange(v)}
      onBlur={field.onBlur}
      placeholder={placeholder}
      disabled={disabled}
      invalid={info.invalid}
      describedBy={info.describedBy}
      buttonRef={field.ref}
    />
  )
}

// =====================================================================================================================
// Alta del conteo
// =====================================================================================================================
function CreateCountModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const navigate = useNavigate()
  const create = useCreateCycleCount()
  const schema = useMemo(
    () =>
      z
        .object({ warehousePublicId: z.string().nullable(), zoneIds: z.array(z.string()), binIds: z.array(z.string()) })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
        }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { warehousePublicId: null as string | null, zoneIds: [] as string[], binIds: [] as string[] } })
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const zoneIds = useWatch({ control: form.control, name: 'zoneIds' })
  const zones = useWarehouseZones(warehousePublicId, {}, { handleAccessDenied: false })
  const bins = useWarehouseBins(warehousePublicId, {}, { handleAccessDenied: false })
  const zoneOptions = useMemo(
    () => (zones.data ?? []).filter((zone) => zone.isActive !== false).map((zone) => ({ value: String(zone.id), label: [zone.code, zone.name].filter(Boolean).join(' · ') })),
    [zones.data],
  )
  const binOptions = useMemo(
    () =>
      (bins.data ?? [])
        .filter((b) => b.isActive !== false && (zoneIds.length === 0 || zoneIds.includes(String(b.zoneId))))
        .map((b) => ({ value: String(b.id), label: [b.code, b.zoneCode].filter(Boolean).join(' · ') })),
    [bins.data, zoneIds],
  )
  const formId = 'cycle-count-create'

  return (
    <Modal
      open
      title={t('warehouse.cycleCounts.new')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('warehouse.cycleCounts.create')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const created = await create.mutateAsync({
            warehousePublicId: v.warehousePublicId,
            zoneIds: v.zoneIds.length > 0 ? v.zoneIds.map(Number) : null,
            binIds: v.binIds.length > 0 ? v.binIds.map(Number) : null,
          })
          toast.success(t('warehouse.cycleCounts.created', { number: created.count?.number ?? '', count: created.lines?.length ?? 0 }))
          onClose()
          if (created.count?.id != null) navigate(`/warehouse/cycle-counts/${created.count.id}`)
        }}
      >
        <Field name="warehousePublicId" label={t('warehouse.cycleCounts.fields.warehouse')} required>
          <WarehousePickerInput />
        </Field>
        <div className="r2">
          <Field name="zoneIds" label={t('warehouse.cycleCounts.fields.zones')}>
            <MultiSelectInput options={zoneOptions} placeholder={t('warehouse.cycleCounts.fields.allZones')} disabled={!warehousePublicId} />
          </Field>
          <Field name="binIds" label={t('warehouse.cycleCounts.fields.bins')}>
            <MultiSelectInput options={binOptions} placeholder={t('warehouse.cycleCounts.fields.allBins')} disabled={!warehousePublicId} />
          </Field>
        </div>
        <p className="note">{t('warehouse.cycleCounts.createHelp')}</p>
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function CycleCountListScreen() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const canCount = useCan('warehouse.count')
  const [warehouses, setWarehouses] = useState<string[]>([])
  const [status, setStatus] = useState<string[]>([])
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [productPublicId, setProductPublicId] = useState<string | null>(null)
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)
  const search = useDebounced(q.trim())

  const { data: warehouseList = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const warehouseOptions = useMemo(() => warehouseList.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouseList])
  const { data: statuses = [] } = useStatuses(STATUS_DOMAIN)
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])

  const query = useMemo(
    () => ({
      warehousePublicIds: warehouses.length > 0 ? warehouses : undefined,
      status: status.length > 0 ? status : undefined,
      from: range.from || undefined,
      to: range.to || undefined,
      productPublicIds: productPublicId ? [productPublicId] : undefined,
      search: search || undefined,
    }),
    [warehouses, status, range, productPublicId, search],
  )
  const { data, isLoading, error } = useCycleCounts(query)
  const rows = useMemo(() => data ?? [], [data])

  const columns = useMemo<DataColumn<CycleCountDto>[]>(
    () => [
      { id: 'number', header: t('warehouse.cycleCounts.columns.number'), cell: (c) => <span className="ref">{c.number}</span>, sortValue: (c) => c.number, card: 'title' },
      { id: 'warehouse', header: t('warehouse.cycleCounts.columns.warehouse'), cell: (c) => c.warehouseCode, sortValue: (c) => c.warehouseCode },
      { id: 'status', header: t('warehouse.cycleCounts.columns.status'), cell: (c) => <StatusChip domain={STATUS_DOMAIN} code={c.statusCode} label={c.status} />, sortValue: (c) => c.status },
      {
        id: 'progress',
        header: t('warehouse.cycleCounts.columns.progress'),
        cell: (c) => `${c.countedLines ?? 0} / ${c.lineCount ?? 0}`,
        sortValue: (c) => (c.lineCount ? (c.countedLines ?? 0) / c.lineCount : 0),
        align: 'end',
      },
      // Conteo a ciegas (Lote 8A): sin warehouse.count el API devuelve netVariance = null; la columna no se muestra y,
      // por defensa, null se pinta como '—' (nunca '0', que diría "todo cuadra").
      ...(canCount
        ? [
            {
              id: 'netVariance',
              header: t('warehouse.cycleCounts.columns.netVariance'),
              cell: (c: CycleCountDto) =>
                c.netVariance == null ? '—' : c.netVariance !== 0 ? <Chip tone="warn">{formatNumber(c.netVariance, lang)}</Chip> : '0',
              sortValue: (c: CycleCountDto) => c.netVariance ?? undefined,
              align: 'end' as const,
            },
          ]
        : []),
      { id: 'createdAt', header: t('warehouse.cycleCounts.columns.createdAt'), cell: (c) => formatDateTime(c.createdAtUtc, lang), sortValue: (c) => c.createdAtUtc },
    ],
    [t, lang, canCount],
  )

  const actions = useMemo<RowAction<CycleCountDto>[]>(
    () => [{ key: 'open', label: t('warehouse.cycleCounts.open'), perm: 'warehouse.count', onClick: (c) => navigate(`/warehouse/cycle-counts/${c.id}`) }],
    [t, navigate],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.cycleCounts.title')}</h1>
          <p>{t('warehouse.cycleCounts.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.count">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.cycleCounts.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setWarehouses([])
          setStatus([])
          setRange(EMPTY_RANGE)
          setProductPublicId(null)
          setQ('')
        }}
      >
        <SearchSelect label={t('warehouse.cycleCounts.filters.warehouses')} options={warehouseOptions} value={warehouses} onChange={setWarehouses} />
        <SearchSelect label={t('warehouse.cycleCounts.filters.status')} options={statusOptions} value={status} onChange={setStatus} />
        <DateRangeFilter label={t('warehouse.cycleCounts.filters.created')} value={range} onChange={setRange} />
        <div className="f">
          <label>{t('warehouse.cycleCounts.filters.product')}</label>
          <ProductPicker value={productPublicId} onChange={(v) => setProductPublicId(v)} aria-label={t('warehouse.cycleCounts.filters.product')} />
        </div>
      </Filters>

      <Panel flush title={t('warehouse.cycleCounts.title')} subtitle={data ? t('warehouse.cycleCounts.count', { count: rows.length }) : undefined}>
        <div className="qrow">
          <QBox value={q} onChange={setQ} placeholder={t('warehouse.cycleCounts.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.cycleCounts.title')}
            columns={columns}
            rows={rows}
            rowKey={(c) => c.id ?? 0}
            defaultSort={{ id: 'createdAt', desc: true }}
            pageSize={PAGE_SIZE}
            loading={isLoading}
            rowActions={actions}
            onRowClick={canCount ? (c) => navigate(`/warehouse/cycle-counts/${c.id}`) : undefined}
          />
        )}
      </Panel>

      {creating && <CreateCountModal onClose={() => setCreating(false)} />}
    </div>
  )
}
