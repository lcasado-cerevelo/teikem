// Productos e inventario (Fase 8, maqueta `inventario()`; Lote 12): `/warehouse/products`, un solo ítem de menú que reúne el
// catálogo y sus existencias por producto. Pestañas Productos (sin parámetro) y Categorías (`?tab=categories`).
// - Cabecera: "Reporte de inventario" y "Reporte de ajustes" (PDF generado en el cliente con los filtros de la tabla:
//   InventoryReportButtons / inventoryReports.ts), "Códigos de barras" (Lote F14: un código por SKU de lo filtrado,
//   BarcodeReportButtons / barcodeReports.ts) y "Nuevo producto" (inventory.manage).
// - Río de KPIs de todo el catálogo (`useProductInventoryKpis`, todos con take=1): SKUs activos, Unidades totales, Bajo mínimo
//   y Con número de serie. Cada KPI es un botón que filtra la tabla (`?kpi=active|available|low|serial`; otro clic lo quita):
//   activos; activos con existencia en mano > 0 (`onlyOnHand`; la cifra suma solo la existencia de los productos
//   activos, `activeProductsOnly`); bajo mínimo; rastreo SERIAL o con series. Bajo mínimo va en naranja solo si es > 0;
//   Con número de serie, si hay productos SERIAL con series incompletas (`serialMissing`), con "N sin series completas".
// - Filtros encima del panel, todos al API: Almacén (varios: acotan las cantidades de cada fila a esos almacenes, no quitan
//   productos), SKU (`ProductMultiFilter` → productPublicIds), Nombre (contiene), Categoría y Marca (GET /products/brands).
//   Sin buscador dentro de la tabla. Lógica pura de los filtros en productFilters.ts.
// - Tabla paginada en el servidor: SKU, Producto, Categoría, Marca (con el modelo debajo), Dueño, Disponible, Reservado,
//   Total, Rastreo, Estado. Exportar = todo lo filtrado. Clic en una fila = "Editar producto" (ProductEditorModal).
// Lectura: inventory.view + WMS_LOTSERIAL (aplicado por la ruta).
import { useMemo, useRef, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, Filters, IconBox, IconLayers, IconTag, Panel, SearchSelect, Tabs, useRegisterFilter, type DataColumn } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import {
  exportProducts,
  useProductBrands,
  useProductCategories,
  useProductInventoryKpis,
  useProducts,
  useWarehouses,
  warehouseLabel,
  type ProductListItemDto,
} from './api'
import { ProductBarcodeReportButton } from './BarcodeReportButtons'
import { TextFilter } from './filterControls'
import { AdjustmentsReportButton, InventoryReportButton } from './InventoryReportButtons'
import { listParam } from './kardexView'
import { formatNumber, useDebounced } from './lineRules'
import { ProductMultiFilter, type ProductFilterItem } from './pickers'
import { ProductCategoriesPanel } from './ProductCategoriesPanel'
import { ProductEditorByIdModal, ProductEditorModal } from './ProductEditorModal'
import { parseKpiParam, productListQuery, toggleKpi, type ProductFilterState, type ProductKpi } from './productFilters'
import '../analytics/pulse.css'
import './warehouse.css'

type ListTab = 'products' | 'categories'

const PAGE_SIZE = 25
const NO_ROWS: ProductListItemDto[] = []

/** Estado de la fila como la maqueta: inactivo manda; si no, bajo mínimo u OK. */
function productState(p: ProductListItemDto): 'inactive' | 'low' | 'ok' {
  if (p.isActive === false) return 'inactive'
  return p.isBelowMin ? 'low' : 'ok'
}

const STATE_TONE = { inactive: 'cap', low: 'fail', ok: 'deliv' } as const

/** Nodo del río de KPIs (`.node` de la maqueta) como botón que filtra la tabla; `money` = tono naranja de alerta. */
function KpiNode({
  tone,
  icon,
  label,
  value,
  sub,
  subAlert,
  active,
  hint,
  onToggle,
}: {
  tone: 'flow' | 'money'
  icon: ReactNode
  label: string
  value: string
  sub?: string
  subAlert?: boolean
  active: boolean
  hint: string
  onToggle: () => void
}) {
  return (
    <button
      type="button"
      className={`node ${tone} inv-kpi${active ? ' on' : ''}`}
      aria-pressed={active}
      aria-label={[`${label}: ${value}`, sub].filter(Boolean).join('. ')}
      title={hint}
      onClick={onToggle}
    >
      <span className="ph">
        {icon}
        <span>{label}</span>
      </span>
      <span className="big">{value}</span>
      {sub && <span className={subAlert ? 'sub inv-alert' : 'sub'}>{sub}</span>}
    </button>
  )
}

/** Río con los cuatro KPIs de la maqueta, de todo el catálogo (no siguen los filtros de la tabla); cada uno filtra la tabla. */
function InventoryKpis({ kpi, onToggle }: { kpi: ProductKpi | null; onToggle: (k: ProductKpi) => void }) {
  const t = useT()
  const lang = useLang()
  const { activeSkus, totalUnits, belowMin, unavailable, serial, serialMissing } = useProductInventoryKpis()
  // separador de miles del idioma de la interfaz (367.329 en español), igual que la tabla y los reportes
  const show = (n: number | null | undefined, loading: boolean, failed: boolean) => {
    if (n != null) return formatNumber(n, lang)
    return loading ? '…' : failed ? '—' : formatNumber(0, lang)
  }
  const belowMinCount = belowMin.data?.total ?? 0
  const missing = serialMissing.data?.total ?? 0
  const hint = (key: string) => t('warehouse.products.kpis.filterHint', { view: t(`warehouse.products.kpis.view.${key}`) })
  // el KPI elegido filtra la tabla: va en la línea de filtros de las exportaciones ("Vista Activos con existencia")
  const riverRef = useRef<HTMLDivElement>(null)
  useRegisterFilter(t('warehouse.products.reports.filters.view'), kpi ? t(`warehouse.products.kpis.view.${kpi}`) : null, riverRef)
  return (
    <div className="pulse">
      <div className="river inv-river" role="group" aria-label={t('warehouse.products.kpis.aria')} ref={riverRef}>
        <KpiNode
          tone="flow"
          icon={<IconLayers />}
          label={t('warehouse.products.kpis.activeSkus')}
          value={show(activeSkus.data?.total, activeSkus.isLoading, activeSkus.isError)}
          active={kpi === 'active'}
          hint={hint('active')}
          onToggle={() => onToggle('active')}
        />
        <div className="pipe" aria-hidden="true" />
        <KpiNode
          tone="flow"
          icon={<IconBox />}
          label={t('warehouse.products.kpis.totalUnits')}
          value={show(totalUnits.data?.totalOnHand, totalUnits.isLoading, totalUnits.isError)}
          active={kpi === 'available'}
          hint={`${hint('available')} ${t('warehouse.products.kpis.totalUnitsNote')}`}
          onToggle={() => onToggle('available')}
        />
        <div className="pipe" aria-hidden="true" />
        <KpiNode
          tone={belowMinCount > 0 ? 'money' : 'flow'}
          icon={<IconAlert />}
          label={t('warehouse.products.kpis.belowMin')}
          value={show(belowMin.data?.total, belowMin.isLoading, belowMin.isError)}
          active={kpi === 'low'}
          hint={hint('low')}
          onToggle={() => onToggle('low')}
        />
        <div className="pipe" aria-hidden="true" />
        <KpiNode
          tone="flow"
          icon={<IconBox />}
          label={t('warehouse.products.kpis.unavailable')}
          value={show(unavailable.data?.total, unavailable.isLoading, unavailable.isError)}
          active={kpi === 'unavailable'}
          hint={`${hint('unavailable')} ${t('warehouse.products.kpis.unavailableNote')}`}
          onToggle={() => onToggle('unavailable')}
        />
        <div className="pipe" aria-hidden="true" />
        <KpiNode
          tone={missing > 0 ? 'money' : 'flow'}
          icon={<IconTag />}
          label={t('warehouse.products.kpis.serial')}
          value={show(serial.data?.total, serial.isLoading, serial.isError)}
          sub={missing > 0 ? t('warehouse.products.kpis.serialMissing', { count: formatNumber(missing, lang) }) : undefined}
          subAlert
          active={kpi === 'serial'}
          hint={hint('serial')}
          onToggle={() => onToggle('serial')}
        />
      </div>
    </div>
  )
}

// ---- Pestaña Productos ----
function ProductsTab() {
  const t = useT()
  const lang = useLang()
  const [params, setParams] = useSearchParams()
  // Lote 15: almacenes iniciales de la URL (`?warehousePublicIds=`, una vez al montar; la franja "Almacén hoy" del Pulso)
  const [warehouses, setWarehouses] = useState<string[]>(() => listParam(params, 'warehousePublicIds'))
  const [products, setProducts] = useState<ProductFilterItem[]>([])
  const [nameText, setNameText] = useState('')
  const [categoryIds, setCategoryIds] = useState<string[]>([])
  const [brands, setBrands] = useState<string[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [creating, setCreating] = useState(false)
  const [editingPublicId, setEditingPublicId] = useState<string | null>(null)
  // Nombre va al API con una pausa (no una consulta por tecla)
  const name = useDebounced(nameText.trim(), 300)

  // KPI elegido: vive en la URL (?kpi=) para poder enlazarlo; otro clic en el mismo lo quita
  const kpi = parseKpiParam(params.get('kpi'))
  const setKpi = (next: ProductKpi | null) => {
    setParams(
      (prev) => {
        const q = new URLSearchParams(prev)
        if (next) q.set('kpi', next)
        else q.delete('kpi')
        return q
      },
      { replace: true },
    )
    setPage(1)
  }

  /** Cambia un filtro y vuelve a la página 1. */
  const withPageReset =
    <V,>(set: (v: V) => void) =>
    (v: V) => {
      set(v)
      setPage(1)
    }

  const { data: warehouseList = [] } = useWarehouses({ includeInactive: false })
  const warehouseOptions = useMemo(() => warehouseList.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouseList])
  const { data: categories = [] } = useProductCategories()
  const categoryOptions = useMemo(() => categories.map((c) => ({ value: String(c.id), label: c.path || c.name || '' })), [categories])
  const { data: brandList = [] } = useProductBrands()
  const brandOptions = useMemo(() => brandList.map((b) => ({ value: b, label: b })), [brandList])
  // Etiquetas de Rastreo del catálogo del tenant (NONE/LOT/SERIAL → 'Ninguno'/'Lote'/'Serie'); sin catálogo, el código.
  const { data: trackingTypes = [] } = useLookups('TrackingType', { includeDisabled: true })
  const trackingLabel = useMemo(() => {
    const labels = new Map(trackingTypes.map((o) => [o.code, o.label]))
    return (code: string | null | undefined) => (code ? (labels.get(code) ?? code) : '')
  }, [trackingTypes])

  // Un solo estado de filtros para la tabla, su Exportar y los dos reportes PDF
  const filters = useMemo<ProductFilterState>(
    () => ({ warehouses, products, name, categoryIds, brands, kpi }),
    [warehouses, products, name, categoryIds, brands, kpi],
  )
  const baseQuery = useMemo(() => productListQuery(filters), [filters])
  const query = useMemo(() => ({ ...baseQuery, skip: (page - 1) * pageSize, take: pageSize }), [baseQuery, page, pageSize])
  const { data, isLoading, error } = useProducts(query)

  const columns = useMemo<DataColumn<ProductListItemDto>[]>(() => {
    const num = (n: number | null | undefined) => <span className="mono">{formatNumber(n ?? 0, lang)}</span>
    return [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'sku', header: t('warehouse.products.columns.sku'), cell: (p) => <span className="ref">{p.sku}</span>, sortValue: (p) => p.sku, card: 'title' },
      { id: 'name', header: t('warehouse.products.columns.product'), cell: (p) => p.name, sortValue: (p) => p.name },
      { id: 'category', header: t('warehouse.products.columns.category'), cell: (p) => p.categoryName ?? '', sortValue: (p) => p.categoryName },
      {
        id: 'brand',
        header: t('warehouse.products.columns.brand'),
        // el modelo va tenue debajo de la marca (no cabe una columna más sin apretar la tabla)
        cell: (p) =>
          p.brand || p.model ? (
            <span className="inv-brand" title={[p.brand, p.model].filter(Boolean).join(' · ')}>
              <span>{p.brand ?? ''}</span>
              {p.model && <span className="inv-model">{p.model}</span>}
            </span>
          ) : (
            ''
          ),
        sortValue: (p) => p.brand ?? undefined,
        exportValue: (p) => [p.brand, p.model].filter(Boolean).join(' · '),
      },
      {
        id: 'owner',
        header: t('warehouse.products.columns.owner'),
        cell: (p) => (p.isOwn ? <span className="inv-own">{t('warehouse.products.own')}</span> : (p.ownerName ?? '')),
        sortValue: (p) => (p.isOwn ? t('warehouse.products.own') : p.ownerName),
      },
      { id: 'available', header: t('warehouse.products.columns.available'), cell: (p) => num(p.qtyAvailable), sortValue: (p) => p.qtyAvailable, align: 'end' },
      {
        id: 'reserved',
        header: t('warehouse.products.columns.reserved'),
        cell: (p) => (p.qtyReserved ? num(p.qtyReserved) : <span className="mono">—</span>),
        sortValue: (p) => p.qtyReserved,
        align: 'end',
      },
      { id: 'total', header: t('warehouse.products.columns.total'), cell: (p) => num(p.qtyOnHand), sortValue: (p) => p.qtyOnHand, align: 'end' },
      {
        id: 'tracking',
        header: t('warehouse.products.columns.tracking'),
        cell: (p) => trackingLabel(p.trackingTypeCode),
        sortValue: (p) => trackingLabel(p.trackingTypeCode),
      },
      {
        id: 'state',
        header: t('warehouse.products.columns.state'),
        cell: (p) => {
          const s = productState(p)
          return <Chip tone={STATE_TONE[s]}>{t(`warehouse.products.states.${s}`)}</Chip>
        },
        sortValue: (p) => t(`warehouse.products.states.${productState(p)}`),
      },
    ]
  }, [t, lang, trackingLabel])

  return (
    <>
      <div className="head">
        <div>
          <h1>{t('warehouse.products.title')}</h1>
          <p>{t('warehouse.products.subtitle')}</p>
        </div>
        <div className="act inv-head-act">
          <InventoryReportButton filters={filters} />
          <AdjustmentsReportButton filters={filters} />
          {/* Lote F14: un código de barras por SKU de lo filtrado, para imprimir y escanear el papel en el conteo */}
          <ProductBarcodeReportButton filters={filters} />
          <Can perm="inventory.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.products.new')}
            </button>
          </Can>
        </div>
      </div>

      <InventoryKpis kpi={kpi} onToggle={(k) => setKpi(toggleKpi(kpi, k))} />

      <Filters
        onClear={() => {
          setWarehouses([])
          setProducts([])
          setNameText('')
          setCategoryIds([])
          setBrands([])
          setKpi(null)
        }}
      >
        <SearchSelect label={t('warehouse.products.filters.warehouse')} options={warehouseOptions} value={warehouses} onChange={withPageReset(setWarehouses)} />
        <ProductMultiFilter label={t('warehouse.products.filters.sku')} value={products} onChange={withPageReset(setProducts)} includeInactive />
        <TextFilter
          label={t('warehouse.products.filters.name')}
          value={nameText}
          onChange={withPageReset(setNameText)}
          placeholder={t('warehouse.products.filters.namePlaceholder')}
        />
        <SearchSelect label={t('warehouse.products.filters.category')} options={categoryOptions} value={categoryIds} onChange={withPageReset(setCategoryIds)} />
        <SearchSelect label={t('warehouse.products.filters.brand')} options={brandOptions} value={brands} onChange={withPageReset(setBrands)} />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('warehouse.products.title')} badge={data ? (data.total ?? 0) : undefined}>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.products.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
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
            exportRows={() => exportProducts(baseQuery)}
            onRowClick={(p) => setEditingPublicId(p.publicId ?? null)}
            rowClassName={(p) => (p.isActive === false ? 'dim' : undefined)}
          />
        )}
      </Panel>

      <ProductEditorModal open={creating} product={null} onClose={() => setCreating(false)} />
      <ProductEditorByIdModal publicId={editingPublicId} onClose={() => setEditingPublicId(null)} />
    </>
  )
}

// ---- Pantalla ----
export default function ProductListScreen() {
  const t = useT()
  // La pestaña va en la URL (?tab=categories; Productos, la primera, sin parámetro) para poder enlazarla.
  const [params, setParams] = useSearchParams()
  const tab: ListTab = params.get('tab') === 'categories' ? 'categories' : 'products'
  const setTab = (key: ListTab) => setParams(key === 'products' ? {} : { tab: key }, { replace: true })

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
