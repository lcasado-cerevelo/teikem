// Pantalla D (Lote F6) — Compras: órdenes de compra. `/warehouse/purchase-orders`. Lectura: purchasing.view +
// PURCHASING (por la ruta). Alta: purchasing.manage. Nace DRAFT con número PO-#####.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { parseApiDate } from '../../kernel/api/dates'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  DataTable,
  DateInput,
  DateRangeFilter,
  EMPTY_RANGE,
  Field,
  Filters,
  Form,
  Modal,
  NumberInput,
  Panel,
  QBox,
  Select,
  SelectFilter,
  SearchSelect,
  TextArea,
  toast,
  type DataColumn,
  type DateRange,
} from '../../kernel/ui'
import { useCreatePurchaseOrder, usePurchaseOrders, useSuppliers, type PurchaseOrderDto } from './api'
import { ProductPickerInput, WarehousePicker, WarehousePickerInput } from './pickers'

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'PurchaseOrderStatus'

function decimals(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

function formatDate(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium' }).format(date)
}

// ---- Modal de alta ----
interface LineFormValues {
  productPublicId: string
  qtyOrdered: number | null
  unitCost: number | null
}

function CreatePurchaseOrderModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const navigate = useNavigate()
  const create = useCreatePurchaseOrder()
  const { data: suppliers = [] } = useSuppliers({ includeInactive: false })
  const supplierOptions = useMemo(() => suppliers.map((s) => ({ value: String(s.id), label: s.name ?? '' })), [suppliers])

  const lineSchema = useMemo(
    () =>
      z.object({
        productPublicId: z.string().min(1, t('warehouse.purchaseOrders.errors.productRequired')),
        qtyOrdered: z
          .number(t('warehouse.purchaseOrders.errors.qtyInvalid'))
          .nullable()
          .superRefine((v, ctx) => {
            if (v === null || v <= 0) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.qtyRequired') })
            else if (decimals(v) > 3) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.qtyDecimals') })
          }),
        unitCost: z
          .number(t('warehouse.purchaseOrders.errors.costInvalid'))
          .nullable()
          .superRefine((v, ctx) => {
            if (v === null) return
            if (v < 0) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.costNegative') })
            else if (decimals(v) > 4) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.costDecimals') })
          }),
      }),
    [t],
  )
  const schema = useMemo(
    () =>
      z.object({
        supplierId: z.string().min(1, t('warehouse.purchaseOrders.errors.supplierRequired')),
        warehousePublicId: z.string().nullable(),
        expectedDate: z.string(),
        notes: z.string(),
        lines: z
          .array(lineSchema)
          .min(1, t('warehouse.purchaseOrders.errors.linesRequired'))
          .max(200, t('warehouse.purchaseOrders.errors.tooManyLines'))
          .superRefine((lines, ctx) => {
            const seen = new Set<string>()
            lines.forEach((l, i) => {
              if (!l.productPublicId) return
              if (seen.has(l.productPublicId)) {
                ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.errors.duplicateProduct'), path: [i, 'productPublicId'] })
              }
              seen.add(l.productPublicId)
            })
          }),
      }),
    [t, lineSchema],
  )

  const emptyLine: LineFormValues = { productPublicId: '', qtyOrdered: null, unitCost: null }
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { supplierId: '', warehousePublicId: null as string | null, expectedDate: '', notes: '', lines: [emptyLine] },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const formId = 'purchase-order-create'

  const close = () => {
    form.reset({ supplierId: '', warehousePublicId: null, expectedDate: '', notes: '', lines: [emptyLine] })
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.purchaseOrders.new')}
      onClose={close}
      size="lg"
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
          const created = await create.mutateAsync({
            supplierId: Number(v.supplierId),
            warehousePublicId: v.warehousePublicId || null,
            expectedDate: v.expectedDate || null,
            notes: v.notes || null,
            lines: v.lines.map((l) => ({ productPublicId: l.productPublicId, qtyOrdered: l.qtyOrdered, unitCost: l.unitCost })),
          })
          toast.success(t('warehouse.purchaseOrders.created'))
          close()
          if (created.publicId) navigate(`/warehouse/purchase-orders/${created.publicId}`)
        }}
      >
        <div className="r2">
          <Field name="supplierId" label={t('warehouse.purchaseOrders.fields.supplier')} required>
            <Select options={supplierOptions} placeholder={t('warehouse.suppliers.fields.none')} />
          </Field>
          <Field name="warehousePublicId" label={t('warehouse.purchaseOrders.fields.warehouse')}>
            <WarehousePickerInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="expectedDate" label={t('warehouse.purchaseOrders.fields.expectedDate')}>
            <DateInput />
          </Field>
        </div>
        <Field name="notes" label={t('warehouse.purchaseOrders.fields.notes')}>
          <TextArea rows={2} />
        </Field>

        <p className="help">{t('warehouse.purchaseOrders.fields.lines')}</p>
        {fields.map((f, i) => (
          <div key={f.id} style={{ padding: 12, marginBottom: 10, border: '1px solid var(--line)', borderRadius: 9 }}>
            <div className="r3">
              <Field name={`lines.${i}.productPublicId`} label={t('warehouse.purchaseOrders.fields.product')} required>
                <ProductPickerInput ownOnly />
              </Field>
              <Field name={`lines.${i}.qtyOrdered`} label={t('warehouse.purchaseOrders.fields.qtyOrdered')} required>
                <NumberInput step="0.001" />
              </Field>
              <Field name={`lines.${i}.unitCost`} label={t('warehouse.purchaseOrders.fields.unitCost')}>
                <NumberInput step="0.0001" />
              </Field>
            </div>
            {fields.length > 1 && (
              <button type="button" className="btn sm danger" onClick={() => remove(i)}>
                {t('warehouse.purchaseOrders.fields.removeLine')}
              </button>
            )}
          </div>
        ))}
        <button type="button" className="btn sm" disabled={fields.length >= 200} onClick={() => append(emptyLine)}>
          {t('warehouse.purchaseOrders.fields.addLine')}
        </button>
      </Form>
    </Modal>
  )
}

// ---- Pantalla ----
export default function PurchaseOrderListScreen() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const [statusFilter, setStatusFilter] = useState<string[]>([])
  const [supplierId, setSupplierId] = useState('')
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    const h = setTimeout(() => {
      setSearch(text.trim())
      setPage(1)
    }, 250)
    return () => clearTimeout(h)
  }, [text])

  const { data: poStatuses = [] } = useStatuses(STATUS_DOMAIN)
  const statusOptions = useMemo(() => poStatuses.map((s) => ({ value: s.code, label: s.label })), [poStatuses])
  const { data: suppliers = [] } = useSuppliers({ includeInactive: true })
  const supplierOptions = useMemo(() => suppliers.map((s) => ({ value: String(s.id), label: s.name ?? '' })), [suppliers])

  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }
  const changeStatus = withPageReset(setStatusFilter)
  const changeSupplier = withPageReset(setSupplierId)
  const changeWarehouse = withPageReset(setWarehousePublicId)
  const changeRange = withPageReset(setRange)

  const query = useMemo(
    () => ({
      status: statusFilter.length > 0 ? statusFilter : undefined,
      supplierId: supplierId ? Number(supplierId) : undefined,
      warehousePublicId: warehousePublicId || undefined,
      from: range.from || undefined,
      to: range.to || undefined,
      search: search || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [statusFilter, supplierId, warehousePublicId, range, search, page],
  )
  const { data, isLoading, error } = usePurchaseOrders(query)

  const columns = useMemo<DataColumn<PurchaseOrderDto>[]>(
    () => [
      { id: 'number', header: t('warehouse.purchaseOrders.columns.number'), cell: (p) => <span className="ref">{p.number}</span>, card: 'title' },
      { id: 'supplier', header: t('warehouse.purchaseOrders.columns.supplier'), cell: (p) => p.supplierName },
      { id: 'warehouse', header: t('warehouse.purchaseOrders.columns.warehouse'), cell: (p) => p.warehouseCode },
      {
        id: 'status',
        header: t('warehouse.purchaseOrders.columns.status'),
        cell: (p) => <StatusChip domain={STATUS_DOMAIN} code={p.statusCode} label={p.status} />,
      },
      { id: 'expectedDate', header: t('warehouse.purchaseOrders.columns.expectedDate'), cell: (p) => formatDate(p.expectedDate, lang) },
      { id: 'orderDate', header: t('warehouse.purchaseOrders.columns.orderDate'), cell: (p) => formatDate(p.orderDate, lang), card: 'hidden' },
    ],
    [t, lang],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.purchaseOrders.title')}</h1>
          <p>{t('warehouse.purchaseOrders.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="purchasing.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.purchaseOrders.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setStatusFilter([])
          setSupplierId('')
          setWarehousePublicId(null)
          setRange(EMPTY_RANGE)
          setText('')
        }}
      >
        <SearchSelect label={t('warehouse.purchaseOrders.filters.status')} options={statusOptions} value={statusFilter} onChange={changeStatus} />
        <SelectFilter
          label={t('warehouse.purchaseOrders.filters.supplier')}
          value={supplierId}
          onChange={changeSupplier}
          options={supplierOptions}
          allLabel={t('warehouse.purchaseOrders.filters.anySupplier')}
        />
        <div className="f">
          <label>{t('warehouse.purchaseOrders.filters.warehouse')}</label>
          <WarehousePicker value={warehousePublicId} onChange={changeWarehouse} placeholder={t('warehouse.purchaseOrders.filters.anyWarehouse')} />
        </div>
        <DateRangeFilter label={t('warehouse.purchaseOrders.filters.range')} value={range} onChange={changeRange} />
      </Filters>

      <Panel flush title={t('warehouse.purchaseOrders.title')} subtitle={data ? t('warehouse.purchaseOrders.count', { count: data.total ?? 0 }) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={setText} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.purchaseOrders.title')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(p) => p.publicId ?? String(p.id)}
            loading={isLoading}
            page={page}
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            onRowClick={(p) => navigate(`/warehouse/purchase-orders/${p.publicId}`)}
          />
        )}
      </Panel>

      <CreatePurchaseOrderModal open={creating} onClose={() => setCreating(false)} />
    </div>
  )
}
