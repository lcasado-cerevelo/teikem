// Pantalla D (Lote F6) — Compras: órdenes de compra. `/warehouse/purchase-orders`. Lectura: purchasing.view +
// PURCHASING (por la ruta). Alta: purchasing.manage. Nace DRAFT con número PO-#####.
// Lote 2: sin buscador dentro de la tabla; filtros Estatus/Proveedor/Almacén con multiselección y buscador (SearchSelect); en el alta, Proveedor y Producto con buscador, Almacén obligatorio y al menos una
// línea con cantidad > 0.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  DataTable,
  DateInput,
  DateRangeFilter,
  EMPTY_RANGE,
  ComboSelectInput,
  Field,
  Filters,
  Form,
  Modal,
  NumberInput,
  Panel,
  SearchSelect,
  TextArea,
  toast,
  type DataColumn,
  type DateRange,
} from '../../kernel/ui'
import { exportPurchaseOrders, useCreatePurchaseOrder, usePurchaseOrders, useSuppliers, useWarehouses, warehouseLabel, type PurchaseOrderDto } from './api'
import { ProductPickerInput, WarehousePickerInput } from './pickers'
import { IconCart } from '../../kernel/ui/screenIcons'
import { formatDate as formatCompanyDate } from '../../kernel/format'

const PAGE_SIZE = 25
const STATUS_DOMAIN = 'PurchaseOrderStatus'

function decimals(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

function formatDate(iso: string | null | undefined, _lang: string): string {
  // fecha corta de la compañía (Región y formatos); un día 'YYYY-MM-DD' no se corre de zona
  return formatCompanyDate(iso)
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
        warehousePublicId: z.string().nullable().refine((v) => Boolean(v), t('warehouse.purchaseOrders.errors.warehouseRequired')),
        expectedDate: z.string(),
        notes: z.string(),
        lines: z
          .array(lineSchema)
          .min(1, t('warehouse.purchaseOrders.errors.linesRequired'))
          .max(200, t('warehouse.purchaseOrders.errors.tooManyLines'))
          .refine((lines) => lines.some((l) => (l.qtyOrdered ?? 0) > 0), t('warehouse.purchaseOrders.errors.linesQtyRequired'))
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
  const linesErrors = form.formState.errors.lines
  const linesError = linesErrors?.message ?? linesErrors?.root?.message

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
            <ComboSelectInput options={supplierOptions} placeholder={t('warehouse.purchaseOrders.fields.supplierSearch')} />
          </Field>
          <Field name="warehousePublicId" label={t('warehouse.purchaseOrders.fields.warehouse')} required>
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
        {linesError && (
          <p className="ferr" role="alert">
            {linesError}
          </p>
        )}
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
  const [supplierIds, setSupplierIds] = useState<string[]>([])
  const [warehouseIds, setWarehouseIds] = useState<string[]>([])
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [creating, setCreating] = useState(false)

  const { data: poStatuses = [] } = useStatuses(STATUS_DOMAIN)
  const statusOptions = useMemo(() => poStatuses.map((s) => ({ value: s.code, label: s.label })), [poStatuses])
  const { data: suppliers = [] } = useSuppliers({ includeInactive: true })
  const supplierOptions = useMemo(() => suppliers.map((s) => ({ value: String(s.id), label: s.name ?? '' })), [suppliers])
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])

  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }
  const changeStatus = withPageReset(setStatusFilter)
  const changeSupplier = withPageReset(setSupplierIds)
  const changeWarehouse = withPageReset(setWarehouseIds)
  const changeRange = withPageReset(setRange)

  const query = useMemo(
    () => ({
      status: statusFilter.length > 0 ? statusFilter : undefined,
      supplierIds: supplierIds.length > 0 ? supplierIds.map(Number) : undefined,
      warehousePublicIds: warehouseIds.length > 0 ? warehouseIds : undefined,
      from: range.from || undefined,
      to: range.to || undefined,
      skip: (page - 1) * pageSize,
      take: pageSize,
    }),
    [statusFilter, supplierIds, warehouseIds, range, page, pageSize],
  )
  const { data, isLoading, error } = usePurchaseOrders(query)

  const columns = useMemo<DataColumn<PurchaseOrderDto>[]>(
    () => [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'number', header: t('warehouse.purchaseOrders.columns.number'), cell: (p) => <span className="ref">{p.number}</span>, sortValue: (p) => p.number, card: 'title' },
      { id: 'supplier', header: t('warehouse.purchaseOrders.columns.supplier'), cell: (p) => p.supplierName, sortValue: (p) => p.supplierName },
      { id: 'warehouse', header: t('warehouse.purchaseOrders.columns.warehouse'), cell: (p) => p.warehouseCode, sortValue: (p) => p.warehouseCode },
      {
        id: 'status',
        header: t('warehouse.purchaseOrders.columns.status'),
        cell: (p) => <StatusChip domain={STATUS_DOMAIN} code={p.statusCode} label={p.status} />,
        sortValue: (p) => p.status ?? p.statusCode,
      },
      {
        id: 'expectedDate',
        header: t('warehouse.purchaseOrders.columns.expectedDate'),
        cell: (p) => formatDate(p.expectedDate, lang),
        sortValue: (p) => p.expectedDate,
      },
      {
        id: 'orderDate',
        header: t('warehouse.purchaseOrders.columns.orderDate'),
        cell: (p) => formatDate(p.orderDate, lang),
        sortValue: (p) => p.orderDate,
        card: 'hidden',
      },
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
          setSupplierIds([])
          setWarehouseIds([])
          setRange(EMPTY_RANGE)
        }}
      >
        <SearchSelect label={t('warehouse.purchaseOrders.filters.status')} options={statusOptions} value={statusFilter} onChange={changeStatus} />
        <SearchSelect label={t('warehouse.purchaseOrders.filters.supplier')} options={supplierOptions} value={supplierIds} onChange={changeSupplier} />
        <SearchSelect label={t('warehouse.purchaseOrders.filters.warehouse')} options={warehouseOptions} value={warehouseIds} onChange={changeWarehouse} />
        <DateRangeFilter label={t('warehouse.purchaseOrders.filters.range')} value={range} onChange={changeRange} />
      </Filters>

      <Panel flush icon={<IconCart />} title={t('warehouse.purchaseOrders.title')} badge={data ? (data.total ?? 0) : undefined}>
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
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportPurchaseOrders(query)}
            onRowClick={(p) => navigate(`/warehouse/purchase-orders/${p.publicId}`)}
          />
        )}
      </Panel>

      <CreatePurchaseOrderModal open={creating} onClose={() => setCreating(false)} />
    </div>
  )
}
