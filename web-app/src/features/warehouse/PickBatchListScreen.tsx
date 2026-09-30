// Pieza "Recolección y empaque" (Lote F6) — lista de recolecciones. `/warehouse/pick-batches`.
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta). Recolectar: warehouse.pick (posición/lote/series opcionales para
// una recolección explícita; sin ellos el servidor elige por FEFO). Una recolección admite productos de un solo dueño.
// Manual 06 §7. Pestaña 'Reabasto' (?tab=replenish): cola de tareas REPLENISH (iniciar/completar con warehouse.pick,
// asignar/cancelar con warehouse.manage) y 'Correr reabasto' (warehouse.pick); ver taskQueue.tsx.
import { zodResolver } from '@hookform/resolvers/zod'
import { useId, useMemo, useState } from 'react'
import { useFieldArray, useForm, useFormContext, useWatch } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../kernel/access'
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
  NumberInput,
  Panel,
  QBox,
  SearchSelect,
  Select,
  Tabs,
  TextArea,
  toast,
  type DataColumn,
  type DateRange,
} from '../../kernel/ui'
import { exportPickBatches, useCreatePickBatch, usePickBatches, useProductLots, type PickBatchDto } from './api'
import {
  firstOtherOwner,
  formatDateTime,
  formatNumber,
  parseSerials,
  pickDuplicateAcrossLines,
  pickLineIssues,
  useDebounced,
  type LineIssue,
} from './lineRules'
import { BinPickerInput, ProductMultiFilter, ProductPickerInput, WarehousePickerInput, type ProductFilterItem } from './pickers'
import { ReplenishButton, TaskQueue } from './taskQueue'
import { IconBasket } from '../../kernel/ui/screenIcons'

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'PickBatchStatus'
const MAX_LINES = 100
const NO_ROWS: never[] = []
/** Dueño "propio" (producto sin cliente) en la regla de un solo dueño. */
const OWN = 'OWN'

interface PickLineForm {
  productPublicId: string | null
  sku: string
  trackingTypeCode: string
  /** '' = sin producto; OWN = propio; si no, publicId del cliente dueño. */
  owner: string
  quantity: number | null
  binId: string
  lotId: string
  serialNumbers: string
}
const EMPTY_LINE: PickLineForm = { productPublicId: null, sku: '', trackingTypeCode: '', owner: '', quantity: null, binId: '', lotId: '', serialNumbers: '' }

/** Filtro de texto simple fuera de un <Form>. */
function TextFilter({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  const id = useId()
  return (
    <div className="f">
      <label htmlFor={id}>{label}</label>
      <input id={id} type="text" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

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

// =====================================================================================================================
// Recolectar
// =====================================================================================================================
function PickLineFields({ index, warehousePublicId, onRemove }: { index: number; warehousePublicId: string | null; onRemove?: () => void }) {
  const t = useT()
  const { setValue } = useFormContext()
  const productPublicId = useWatch({ name: `lines.${index}.productPublicId` }) as string | null
  const tracking = useWatch({ name: `lines.${index}.trackingTypeCode` }) as string
  const lots = useProductLots(productPublicId, { enabled: tracking === 'LOT' || tracking === 'SERIAL', handleAccessDenied: false })
  const lotOptions = useMemo(
    () =>
      (lots.data ?? [])
        .filter((l) => l.isActive !== false && (l.qtyOnHand ?? 0) > 0)
        .map((l) => ({ value: String(l.id), label: [l.lotNumber, l.expiryDate].filter(Boolean).join(' · ') })),
    [lots.data],
  )
  return (
    <div className="panel" style={{ padding: 12, marginBottom: 10 }}>
      <div className="r2">
        <Field name={`lines.${index}.productPublicId`} label={t('warehouse.pickBatches.fields.product')} required>
          <ProductPickerInput
            warehousePublicId={warehousePublicId}
            onlyAvailable
            disabled={!warehousePublicId}
            placeholder={warehousePublicId ? undefined : t('warehouse.pickBatches.pickWarehouseFirst')}
            onPicked={(p) => {
              setValue(`lines.${index}.sku`, p?.sku ?? '')
              setValue(`lines.${index}.trackingTypeCode`, p?.trackingTypeCode ?? '')
              setValue(`lines.${index}.owner`, p ? (p.ownerClientPublicId ?? OWN) : '')
              setValue(`lines.${index}.lotId`, '')
            }}
          />
        </Field>
        <Field name={`lines.${index}.quantity`} label={t('warehouse.pickBatches.fields.quantity')} required>
          <NumberInput min={0} step={tracking === 'SERIAL' ? '1' : '0.001'} />
        </Field>
      </div>
      <div className="r2">
        <Field name={`lines.${index}.binId`} label={t('warehouse.pickBatches.fields.bin')} help={t('warehouse.pickBatches.fields.fefoHelp')}>
          {/* vacío = el servidor elige por FEFO; solo posiciones con existencias; al cambiar de almacén se quita sola */}
          <BinPickerInput warehousePublicId={warehousePublicId} onlyWithStock placeholder={t('warehouse.pickBatches.fefo')} />
        </Field>
        {(tracking === 'LOT' || tracking === 'SERIAL') && (
          <Field name={`lines.${index}.lotId`} label={t('warehouse.pickBatches.fields.lot')}>
            <Select options={lotOptions} placeholder={t('warehouse.pickBatches.fefo')} />
          </Field>
        )}
      </div>
      {tracking === 'SERIAL' && (
        <Field name={`lines.${index}.serialNumbers`} label={t('warehouse.pickBatches.fields.serials')} help={t('warehouse.receipts.fields.serialsHelp')} required>
          <TextArea rows={3} />
        </Field>
      )}
      {onRemove && (
        <button type="button" className="btn sm" onClick={onRemove}>
          {t('warehouse.receipts.removeLine')}
        </button>
      )}
    </div>
  )
}

function CollectModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const navigate = useNavigate()
  const create = useCreatePickBatch()
  const issueText = useMemo(() => (i: LineIssue) => t(`warehouse.lineRules.${i.code}`, i.params), [t])
  const schema = useMemo(
    () =>
      z
        .object({
          warehousePublicId: z.string().nullable(),
          lines: z.array(
            z.object({
              productPublicId: z.string().nullable(),
              sku: z.string(),
              trackingTypeCode: z.string(),
              owner: z.string(),
              quantity: z.number().nullable(),
              binId: z.string(),
              lotId: z.string(),
              serialNumbers: z.string(),
            }),
          ),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (v.lines.length === 0) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.lineRules.pickLinesRequired') })
          if (v.lines.length > MAX_LINES) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.lineRules.pickTooManyLines') })
          const serialsByLine = v.lines.map((l) => parseSerials(l.serialNumbers))
          v.lines.forEach((l, i) => {
            if (!l.productPublicId) ctx.addIssue({ code: 'custom', path: ['lines', i, 'productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
            for (const issue of pickLineIssues({ sku: l.sku, trackingTypeCode: l.trackingTypeCode, quantity: l.quantity, hasLot: Boolean(l.lotId), serials: serialsByLine[i] })) {
              ctx.addIssue({ code: 'custom', path: ['lines', i, issue.field], message: issueText(issue) })
            }
          })
          const dup = pickDuplicateAcrossLines(serialsByLine)
          if (dup) ctx.addIssue({ code: 'custom', path: ['lines', dup[0], 'serialNumbers'], message: issueText({ field: 'serialNumbers', code: 'pickSerialDuplicated', params: { serial: dup[1] } }) })
          const other = firstOtherOwner(v.lines.map((l) => (l.owner === '' ? undefined : l.owner === OWN ? null : l.owner)))
          if (other != null) ctx.addIssue({ code: 'custom', path: ['lines', other, 'productPublicId'], message: t('warehouse.lineRules.singleOwner') })
        }),
    [t, issueText],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { warehousePublicId: null as string | null, lines: [EMPTY_LINE] } })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const formId = 'pick-batch-create'

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.pickBatches.collectTitle')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('warehouse.pickBatches.collect')}
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
            lines: v.lines.map((l) => {
              const serials = parseSerials(l.serialNumbers)
              return {
                productPublicId: l.productPublicId,
                quantity: l.quantity,
                binId: l.binId ? Number(l.binId) : null,
                lotId: l.lotId ? Number(l.lotId) : null,
                serialNumbers: serials.length > 0 ? serials : null,
              }
            }),
          })
          toast.success(t('warehouse.pickBatches.collected', { number: created.number ?? '' }))
          onClose()
          if (created.publicId) navigate(`/warehouse/pick-batches/${created.publicId}`)
        }}
      >
        <Field name="warehousePublicId" label={t('warehouse.pickBatches.fields.warehouse')} required>
          <WarehousePickerInput />
        </Field>
        <p className="help">{t('warehouse.pickBatches.linesHelp')}</p>
        {fields.map((f, i) => (
          <PickLineFields key={f.id} index={i} warehousePublicId={warehousePublicId} onRemove={fields.length > 1 ? () => remove(i) : undefined} />
        ))}
        <button type="button" className="btn sm" disabled={fields.length >= MAX_LINES} onClick={() => append(EMPTY_LINE)}>
          {t('warehouse.receipts.addLine')}
        </button>
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Pestaña Recolecciones (paginada en el servidor)
// =====================================================================================================================
function PickBatchesTab() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [status, setStatus] = useState<string[]>([])
  const [products, setProducts] = useState<ProductFilterItem[]>([])
  const [orderNumber, setOrderNumber] = useState('')
  const [invoiceNumber, setInvoiceNumber] = useState('')
  const [includeDeleted, setIncludeDeleted] = useState(false)
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const search = useDebounced(q.trim())
  const order = useDebounced(orderNumber.trim())
  const invoice = useDebounced(invoiceNumber.trim())

  const { data: statuses = [] } = useStatuses(STATUS_DOMAIN)
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])

  function reset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }

  const query = useMemo(
    () => ({
      from: range.from || undefined,
      to: range.to || undefined,
      productPublicIds: products.length > 0 ? products.map((p) => p.publicId) : undefined,
      status: status.length > 0 ? status : undefined,
      orderNumber: order || undefined,
      invoiceNumber: invoice || undefined,
      search: search || undefined,
      includeDeleted: includeDeleted || undefined,
      skip: (page - 1) * pageSize,
      take: pageSize,
    }),
    [range, products, status, order, invoice, search, includeDeleted, page, pageSize],
  )
  const { data, isLoading, error } = usePickBatches(query)

  const columns = useMemo<DataColumn<PickBatchDto>[]>(
    () => [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'number', header: t('warehouse.pickBatches.columns.number'), cell: (b) => <span className="ref">{b.number}</span>, sortValue: (b) => b.number, card: 'title' },
      { id: 'warehouse', header: t('warehouse.pickBatches.columns.warehouse'), cell: (b) => b.warehouseCode, sortValue: (b) => b.warehouseCode },
      {
        id: 'status',
        header: t('warehouse.pickBatches.columns.status'),
        cell: (b) => (
          <>
            <StatusChip domain={STATUS_DOMAIN} code={b.statusCode} label={b.status} />
            {b.isActive === false && (
              <>
                {' '}
                <Chip tone="fail">{t('warehouse.pickBatches.deletedChip')}</Chip>
              </>
            )}
          </>
        ),
        sortValue: (b) => b.status ?? b.statusCode,
      },
      { id: 'numbers', header: t('warehouse.pickBatches.columns.numbers'), cell: (b) => b.displayNumbers ?? '—', sortValue: (b) => b.displayNumbers },
      {
        id: 'client',
        header: t('warehouse.pickBatches.columns.client'),
        cell: (b) => b.clientName ?? t('warehouse.pickBatches.own'),
        sortValue: (b) => b.clientName ?? t('warehouse.pickBatches.own'),
      },
      { id: 'qty', header: t('warehouse.pickBatches.columns.totalQty'), cell: (b) => formatNumber(b.totalQty, lang), sortValue: (b) => b.totalQty, align: 'end' },
      {
        id: 'collectedAt',
        header: t('warehouse.pickBatches.columns.collectedAt'),
        cell: (b) => formatDateTime(b.collectedAtUtc, lang),
        sortValue: (b) => b.collectedAtUtc,
      },
    ],
    [t, lang],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setPage(1)
          setRange(EMPTY_RANGE)
          setStatus([])
          setProducts([])
          setOrderNumber('')
          setInvoiceNumber('')
          setIncludeDeleted(false)
          setQ('')
        }}
      >
        <DateRangeFilter label={t('warehouse.pickBatches.filters.collected')} value={range} onChange={reset(setRange)} />
        <SearchSelect label={t('warehouse.pickBatches.filters.status')} options={statusOptions} value={status} onChange={reset(setStatus)} />
        <ProductMultiFilter label={t('warehouse.pickBatches.filters.product')} value={products} onChange={reset(setProducts)} includeInactive />
        <TextFilter label={t('warehouse.pickBatches.filters.orderNumber')} value={orderNumber} onChange={reset(setOrderNumber)} />
        <TextFilter label={t('warehouse.pickBatches.filters.invoiceNumber')} value={invoiceNumber} onChange={reset(setInvoiceNumber)} />
        <ToggleFilter label={t('warehouse.pickBatches.filters.includeDeleted')} checked={includeDeleted} onChange={reset(setIncludeDeleted)} />
      </Filters>

      <Panel flush icon={<IconBasket />} title={t('warehouse.pickBatches.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={q} onChange={reset(setQ)} placeholder={t('warehouse.pickBatches.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.pickBatches.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(b) => b.publicId ?? String(b.id)}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportPickBatches(query)}
            onRowClick={(b) => navigate(`/warehouse/pick-batches/${b.publicId}`)}
            rowActions={[{ key: 'open', label: t('warehouse.pickBatches.open'), onClick: (b) => navigate(`/warehouse/pick-batches/${b.publicId}`) }]}
          />
        )}
      </Panel>
    </>
  )
}

// =====================================================================================================================
// Pantalla: pestañas Recolecciones y Reabasto (tareas REPLENISH, antes en 'Tareas de almacén', que no está en la maqueta)
// =====================================================================================================================
const TAB_KEYS = ['batches', 'replenish'] as const
type TabKey = (typeof TAB_KEYS)[number]
const isTabKey = (v: string | null): v is TabKey => (TAB_KEYS as readonly string[]).includes(v ?? '')
const REPLENISH_TYPES = ['REPLENISH'] as const

export default function PickBatchListScreen() {
  const t = useT()
  // La pestaña va en la URL (?tab=replenish) para poder enlazarla (Actividad reciente).
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab')
  const tab: TabKey = isTabKey(raw) ? raw : 'batches'
  const setTab = (key: TabKey) => setParams(key === 'batches' ? {} : { tab: key }, { replace: true })
  const [collecting, setCollecting] = useState(false)

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.pickBatches.title')}</h1>
          <p>{t('warehouse.pickBatches.subtitle')}</p>
        </div>
        <div className="act">
          {tab === 'batches' ? (
            <Can perm="warehouse.pick">
              <button type="button" className="btn flow" onClick={() => setCollecting(true)}>
                {t('warehouse.pickBatches.collect')}
              </button>
            </Can>
          ) : (
            <ReplenishButton />
          )}
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.pickBatches.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'batches', label: t('warehouse.pickBatches.tabBatches') },
            { key: 'replenish', label: t('warehouse.pickBatches.tabReplenish') },
          ]}
        />
      </div>

      {tab === 'batches' && <PickBatchesTab />}
      {tab === 'replenish' && <TaskQueue types={REPLENISH_TYPES} title={t('warehouse.pickBatches.replenishTitle')} icon={<IconBasket />} />}

      {collecting && <CollectModal onClose={() => setCollecting(false)} />}
    </div>
  )
}
