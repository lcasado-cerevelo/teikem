// Pantalla B (Lote F6) — Productos y categorías. `/warehouse/products`: lista de productos con pestaña Categorías.
// Lectura: inventory.view + WMS_LOTSERIAL (aplicado por la ruta). Alta: inventory.manage.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { CustomFieldsForm, useSaveCustomFields } from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ClientPicker,
  ClientPickerInput,
  DataTable,
  Field,
  Filters,
  Form,
  Modal,
  NumberInput,
  Panel,
  QBox,
  Select,
  SearchSelect,
  Tabs,
  TextInput,
  toast,
  type DataColumn,
} from '../../kernel/ui'
import { useCreateProduct, useProductCategories, useProducts, type ProductListItemDto } from './api'
import { ProductCategoriesPanel } from './ProductCategoriesPanel'
import { WarehousePicker } from './pickers'
import { moneySchema, volumeM3Schema, weightKgSchema } from './productRules'

type ListTab = 'products' | 'categories'

const PAGE_SIZE = 25
// eslint-disable-next-line no-control-regex -- intencional: el SKU no admite caracteres de control (manual §2).
const CONTROL_CHARS_OR_SPACES = /[\s\x00-\x1F\x7F]/

/** Nivel de indentación a partir de la ruta ("Raíz / Hija / Nieta") que arma el servidor (réplica de ProductCategoriesPanel). */
function levelOf(path: string | null | undefined): number {
  if (!path) return 0
  return path.split('/').length - 1
}


// ---- Modal de alta ----
function CreateProductModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const navigate = useNavigate()
  const create = useCreateProduct()
  const { save: saveCustomFields } = useSaveCustomFields('PRODUCT')
  const { data: trackingTypes = [] } = useLookups('TrackingType')
  const { data: categories = [] } = useProductCategories()
  const categoryOptions = useMemo(
    () =>
      categories
        .filter((c) => c.isActive)
        .map((c) => ({ value: String(c.id), label: '  '.repeat(levelOf(c.path)) + (c.name ?? '') })),
    [categories],
  )

  const schema = useMemo(
    () =>
      z.object({
        sku: z
          .string()
          .trim()
          .min(1, t('warehouse.products.errors.skuRequired'))
          .max(60, t('warehouse.products.errors.skuMax'))
          .refine((v) => !CONTROL_CHARS_OR_SPACES.test(v), t('warehouse.products.errors.skuChars')),
        name: z.string().trim().min(1, t('warehouse.products.errors.nameRequired')).max(200, t('warehouse.products.errors.nameMax')),
        barcode: z.string().trim().max(60, t('warehouse.products.errors.barcodeMax')),
        categoryId: z.string(),
        trackingType: z.string(),
        purchaseCost: moneySchema(t, 'cost'),
        salePrice: moneySchema(t, 'price'),
        weightKg: weightKgSchema(t),
        volumeM3: volumeM3Schema(t),
        minQty: z.number(t('warehouse.products.errors.numberInvalid')).min(0, t('warehouse.products.errors.minNegative')).nullable(),
        ownerClientPublicId: z.string().nullable(),
      }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      sku: '',
      name: '',
      barcode: '',
      categoryId: '',
      trackingType: '',
      purchaseCost: null,
      salePrice: null,
      weightKg: null,
      volumeM3: null,
      minQty: null,
      ownerClientPublicId: null,
    },
  })
  const formId = 'product-create'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.products.new')}
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
          const created = await create.mutateAsync({
            sku: v.sku,
            name: v.name,
            barcode: v.barcode || null,
            categoryId: v.categoryId ? Number(v.categoryId) : null,
            trackingType: v.trackingType || null,
            purchaseCost: v.purchaseCost,
            salePrice: v.salePrice,
            weightKg: v.weightKg,
            volumeM3: v.volumeM3,
            minQty: v.minQty,
            ownerClientPublicId: v.ownerClientPublicId || null,
          })
          const id = created.product?.id
          if (typeof id === 'number' && id > 0) {
            const problem = await saveCustomFields(id, form)
            if (problem) toast.error(problem.title)
          }
          toast.success(t('warehouse.products.created'))
          close()
          const publicId = created.product?.publicId
          if (publicId) navigate(`/warehouse/products/${publicId}`)
        }}
      >
        <div className="r2">
          <Field name="sku" label={t('warehouse.products.fields.sku')} required>
            <TextInput maxLength={60} />
          </Field>
          <Field name="name" label={t('warehouse.products.fields.name')} required>
            <TextInput maxLength={200} />
          </Field>
        </div>
        <div className="r2">
          <Field name="barcode" label={t('warehouse.products.fields.barcode')}>
            <TextInput maxLength={60} />
          </Field>
          <Field name="categoryId" label={t('warehouse.products.fields.category')}>
            <Select options={categoryOptions} placeholder={t('warehouse.products.fields.none')} />
          </Field>
        </div>
        <div className="r2">
          <Field name="trackingType" label={t('warehouse.products.fields.trackingType')}>
            <Select options={trackingTypes.map((o) => ({ value: o.code, label: o.label }))} placeholder={t('warehouse.products.fields.none')} />
          </Field>
          <div aria-hidden="true" />
        </div>
        <div className="r2">
          <Field name="purchaseCost" label={t('warehouse.products.fields.purchaseCost')}>
            <NumberInput min={0} />
          </Field>
          <Field name="salePrice" label={t('warehouse.products.fields.salePrice')}>
            <NumberInput min={0} />
          </Field>
        </div>
        <div className="r2">
          <Field name="weightKg" label={t('warehouse.products.fields.weightKg')}>
            <NumberInput min={0} />
          </Field>
          <Field name="volumeM3" label={t('warehouse.products.fields.volumeM3')}>
            <NumberInput min={0} />
          </Field>
        </div>
        <div className="r2">
          <Field name="minQty" label={t('warehouse.products.fields.minQty')}>
            <NumberInput min={0} />
          </Field>
          <Field name="ownerClientPublicId" label={t('warehouse.products.fields.owner')}>
            <ClientPickerInput />
          </Field>
        </div>
        <CustomFieldsForm entityType="PRODUCT" form={form} />
      </Form>
    </Modal>
  )
}

// ---- Filtro booleano (fuera de un <Form>: no usa react-hook-form) ----
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

// ---- Pestaña Productos ----
function ProductsTab() {
  const t = useT()
  const navigate = useNavigate()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [categoryIds, setCategoryIds] = useState<string[]>([])
  const [ownerClientPublicId, setOwnerClientPublicId] = useState<string | null>(null)
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(null)
  const [ownOnly, setOwnOnly] = useState(false)
  const [activeOnly, setActiveOnly] = useState(true)
  const [onlyAvailable, setOnlyAvailable] = useState(false)
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)

  // El buscador libre de este listado va al API (paginación de servidor): pausa de 250 ms, como ProductPicker.
  // Cada filtro nuevo vuelve a la primera página (se hace en el propio setter, no en un efecto).
  useEffect(() => {
    // solo un texto distinto al aplicado vuelve a la página 1 (al montar no hay cambio: no se pisa la página elegida)
    const next = text.trim()
    if (next === search) return
    const h = setTimeout(() => {
      setSearch(next)
      setPage(1)
    }, 250)
    return () => clearTimeout(h)
  }, [text, search])

  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }
  const changeCategoryIds = withPageReset(setCategoryIds)
  const changeOwner = withPageReset(setOwnerClientPublicId)
  const changeWarehouse = withPageReset(setWarehousePublicId)
  const changeOwnOnly = withPageReset(setOwnOnly)
  const changeActiveOnly = withPageReset(setActiveOnly)
  const changeOnlyAvailable = withPageReset(setOnlyAvailable)

  const { data: categories = [] } = useProductCategories()
  const categoryOptions = useMemo(() => categories.map((c) => ({ value: String(c.id), label: c.name ?? '' })), [categories])

  const query = useMemo(
    () => ({
      search: search || undefined,
      categoryIds: categoryIds.length > 0 ? categoryIds.map(Number) : undefined,
      ownerClientPublicId: ownerClientPublicId || undefined,
      ownOnly: ownOnly || undefined,
      activeOnly: activeOnly || undefined,
      warehousePublicId: warehousePublicId || undefined,
      onlyAvailable: onlyAvailable || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [search, categoryIds, ownerClientPublicId, ownOnly, activeOnly, warehousePublicId, onlyAvailable, page],
  )
  const { data, isLoading, error } = useProducts(query)

  const columns = useMemo<DataColumn<ProductListItemDto>[]>(
    () => [
      { id: 'sku', header: t('warehouse.products.fields.sku'), cell: (p) => <span className="ref">{p.sku}</span>, card: 'title' },
      { id: 'name', header: t('warehouse.products.fields.name'), cell: (p) => p.name },
      { id: 'category', header: t('warehouse.products.fields.category'), cell: (p) => p.categoryName ?? '' },
      { id: 'owner', header: t('warehouse.products.fields.owner'), cell: (p) => (p.isOwn ? t('warehouse.products.own') : (p.ownerName ?? '')) },
      { id: 'tracking', header: t('warehouse.products.fields.trackingType'), cell: (p) => <Chip>{p.trackingTypeCode}</Chip> },
      { id: 'onHand', header: t('warehouse.products.fields.qtyOnHand'), cell: (p) => p.qtyOnHand, align: 'end' },
      {
        id: 'available',
        header: t('warehouse.products.fields.qtyAvailable'),
        cell: (p) => (
          <>
            {p.qtyAvailable} {p.isBelowMin && <Chip tone="warn">{t('warehouse.products.belowMin')}</Chip>}
          </>
        ),
        align: 'end',
      },
      {
        id: 'active',
        header: t('warehouse.products.fields.active'),
        cell: (p) => <Chip tone={p.isActive ? 'neutral' : 'fail'}>{p.isActive ? t('warehouse.products.active') : t('warehouse.products.inactive')}</Chip>,
      },
    ],
    [t],
  )

  return (
    <>
      <div className="head">
        <div>
          <h1>{t('warehouse.products.title')}</h1>
          <p>{t('warehouse.products.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="inventory.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.products.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setText('')
          setCategoryIds([])
          setOwnerClientPublicId(null)
          setWarehousePublicId(null)
          setOwnOnly(false)
          setActiveOnly(true)
          setOnlyAvailable(false)
        }}
      >
        <SearchSelect label={t('warehouse.products.filters.category')} options={categoryOptions} value={categoryIds} onChange={changeCategoryIds} />
        <div className="f">
          <label>{t('warehouse.products.filters.owner')}</label>
          <ClientPicker value={ownerClientPublicId} onChange={(publicId) => changeOwner(publicId)} />
        </div>
        <div className="f">
          <label>{t('warehouse.products.filters.warehouse')}</label>
          <WarehousePicker value={warehousePublicId} onChange={changeWarehouse} placeholder={t('warehouse.products.filters.anyWarehouse')} />
        </div>
        <ToggleFilter label={t('warehouse.products.filters.ownOnly')} checked={ownOnly} onChange={changeOwnOnly} />
        <ToggleFilter label={t('warehouse.products.filters.activeOnly')} checked={activeOnly} onChange={changeActiveOnly} />
        <ToggleFilter label={t('warehouse.products.filters.onlyAvailable')} checked={onlyAvailable} onChange={changeOnlyAvailable} />
      </Filters>

      <Panel flush title={t('warehouse.products.title')} subtitle={data ? t('warehouse.products.count', { count: data.total ?? 0 }) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={setText} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.products.title')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(p) => p.publicId ?? String(p.id)}
            loading={isLoading}
            page={page}
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            onRowClick={(p) => navigate(`/warehouse/products/${p.publicId}`)}
          />
        )}
      </Panel>

      <CreateProductModal open={creating} onClose={() => setCreating(false)} />
    </>
  )
}

// ---- Pantalla ----
export default function ProductListScreen() {
  const t = useT()
  const [tab, setTab] = useState<ListTab>('products')

  return (
    <div className="wrap">
      <div style={{ marginBottom: 14 }}>
        <Tabs<ListTab>
          label={t('warehouse.products.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'products', label: t('warehouse.products.tabProducts') },
            { key: 'categories', label: t('warehouse.products.tabCategories') },
          ]}
        />
      </div>
      {tab === 'products' ? <ProductsTab /> : <ProductCategoriesPanel />}
    </div>
  )
}
