// Productos e inventario (Fase 8, maqueta `inventario()`): `/warehouse/products`, un solo ítem de menú que reúne el catálogo
// y sus existencias por producto. Pestañas Productos (sin parámetro) y Categorías (`?tab=categories`).
// - Cabecera: "Reporte de inventario" (→ Kárdex de movimientos, pestaña Saldos, con el almacén y las categorías filtrados
//   aquí), "Reporte de ajustes" (→ Kárdex filtrado por el tipo ADJUSTMENT y el almacén) y "Nuevo producto" (inventory.manage).
// - Río de KPIs de todo el catálogo (`useProductInventoryKpis`): SKUs activos, Unidades totales, Bajo mínimo y Con número de serie.
// - Filtros Almacén, Categoría y Estado (van al API) + buscador libre (`search` del API: SKU, nombre, código de barras o dueño).
// - Tabla SKU, Producto, Categoría, Dueño, Disponible, Reservado, Total, Rastreo, Estado. Disponible/Reservado/Total vienen
//   en la propia lista paginada (`ProductListItemDto.qtyAvailable/qtyReserved/qtyOnHand`, del almacén filtrado o de todos):
//   no hace falta cruzar con Saldos. Clic en una fila = "Editar producto" (ProductEditorModal, Fase 5).
// Lectura: inventory.view + WMS_LOTSERIAL (aplicado por la ruta).
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  Filters,
  IconBox,
  IconLayers,
  IconTag,
  Panel,
  QBox,
  SearchSelect,
  SelectFilter,
  Tabs,
  type DataColumn,
} from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import {
  SERIAL_COUNT_MAX_PAGES,
  SERIAL_COUNT_PAGE,
  useProductCategories,
  useProductInventoryKpis,
  useProducts,
  useWarehouses,
  warehouseLabel,
  type ProductListItemDto,
} from './api'
import { formatValue } from '../analytics/format'
import { formatNumber } from './lineRules'
import { ProductCategoriesPanel } from './ProductCategoriesPanel'
import { ProductEditorByIdModal, ProductEditorModal } from './ProductEditorModal'
import '../analytics/pulse.css'
import './warehouse.css'

type ListTab = 'products' | 'categories'

/** Filtro Estado: '' = todos (activos e inactivos, como la maqueta), solo activos o bajo mínimo (`belowMin` del API). */
type StateFilter = '' | 'active' | 'low'

const PAGE_SIZE = 25
/** InternalCode del tipo de movimiento 'Ajuste' (catálogo InventoryTxnType) para el "Reporte de ajustes". */
const ADJUSTMENT_TXN_TYPE = 'ADJUSTMENT'

/** Estado de la fila como la maqueta: inactivo manda; si no, bajo mínimo u OK. */
function productState(p: ProductListItemDto): 'inactive' | 'low' | 'ok' {
  if (p.isActive === false) return 'inactive'
  return p.isBelowMin ? 'low' : 'ok'
}

const STATE_TONE = { inactive: 'cap', low: 'fail', ok: 'deliv' } as const

/** Nodo del río de KPIs (`.node` de la maqueta; `money` = tono naranja de alerta, `.node.m`). */
function KpiNode({ tone, icon, label, value, sub }: { tone: 'flow' | 'money'; icon: ReactNode; label: string; value: string; sub?: string }) {
  return (
    <div className={`node ${tone}`} role="group" aria-label={`${label}: ${value}`}>
      <div className="ph">
        {icon}
        <span>{label}</span>
      </div>
      <div className="big">{value}</div>
      {sub && <div className="sub">{sub}</div>}
    </div>
  )
}

/** Río con los cuatro KPIs de la maqueta, de todo el catálogo (no siguen los filtros de la tabla). Cifras como el río de Pulso. */
function InventoryKpis() {
  const t = useT()
  const { activeSkus, totalUnits, belowMin, serial } = useProductInventoryKpis()
  const show = (n: number | null | undefined, loading: boolean, failed: boolean) => {
    if (n != null) return formatValue(n, false)
    return loading ? '…' : failed ? '—' : formatValue(0, false)
  }
  const serialValue = serial.data
    ? `${formatValue(serial.data.count, false)}${serial.data.truncated ? '+' : ''}`
    : show(undefined, serial.isLoading, serial.isError)
  return (
    <div className="pulse">
      <div className="river inv-river" role="group" aria-label={t('warehouse.products.kpis.aria')}>
        <KpiNode
          tone="flow"
          icon={<IconLayers />}
          label={t('warehouse.products.kpis.activeSkus')}
          value={show(activeSkus.data?.total, activeSkus.isLoading, activeSkus.isError)}
        />
        <div className="pipe" aria-hidden="true" />
        <KpiNode
          tone="flow"
          icon={<IconBox />}
          label={t('warehouse.products.kpis.totalUnits')}
          value={show(totalUnits.data?.totalOnHand, totalUnits.isLoading, totalUnits.isError)}
        />
        <div className="pipe" aria-hidden="true" />
        <KpiNode
          tone="money"
          icon={<IconAlert />}
          label={t('warehouse.products.kpis.belowMin')}
          value={show(belowMin.data?.total, belowMin.isLoading, belowMin.isError)}
        />
        <div className="pipe" aria-hidden="true" />
        <KpiNode
          tone="flow"
          icon={<IconTag />}
          label={t('warehouse.products.kpis.serial')}
          value={serialValue}
          sub={serial.data?.truncated ? t('warehouse.products.kpis.truncated', { count: formatValue(SERIAL_COUNT_PAGE * SERIAL_COUNT_MAX_PAGES, false) }) : undefined}
        />
      </div>
    </div>
  )
}

// ---- Pestaña Productos ----
function ProductsTab() {
  const t = useT()
  const lang = useLang()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [warehousePublicId, setWarehousePublicId] = useState('')
  const [categoryIds, setCategoryIds] = useState<string[]>([])
  const [state, setState] = useState<StateFilter>('')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)
  const [editingPublicId, setEditingPublicId] = useState<string | null>(null)

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

  const { data: warehouses = [] } = useWarehouses({ includeInactive: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const { data: categories = [] } = useProductCategories()
  const categoryOptions = useMemo(() => categories.map((c) => ({ value: String(c.id), label: c.name ?? '' })), [categories])
  const stateOptions = useMemo(
    () => [
      { value: 'active', label: t('warehouse.products.filters.stateActive') },
      { value: 'low', label: t('warehouse.products.filters.stateLow') },
    ],
    [t],
  )
  // Etiquetas de Rastreo del catálogo del tenant (NONE/LOT/SERIAL → 'Ninguno'/'Lote'/'Serie'); sin catálogo, el código.
  const { data: trackingTypes = [] } = useLookups('TrackingType', { includeDisabled: true })
  const trackingLabel = useMemo(() => {
    const labels = new Map(trackingTypes.map((o) => [o.code, o.label]))
    return (code: string | null | undefined) => (code ? (labels.get(code) ?? code) : '')
  }, [trackingTypes])

  const query = useMemo(
    () => ({
      search: search || undefined,
      categoryIds: categoryIds.length > 0 ? categoryIds.map(Number) : undefined,
      warehousePublicId: warehousePublicId || undefined,
      activeOnly: state === 'active' || undefined,
      belowMin: state === 'low' || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [search, categoryIds, warehousePublicId, state, page],
  )
  const { data, isLoading, error } = useProducts(query)

  const columns = useMemo<DataColumn<ProductListItemDto>[]>(() => {
    const num = (n: number | null | undefined) => <span className="mono">{formatNumber(n ?? 0, lang)}</span>
    return [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'sku', header: t('warehouse.products.columns.sku'), cell: (p) => <span className="ref">{p.sku}</span>, sortValue: (p) => p.sku, card: 'title' },
      { id: 'name', header: t('warehouse.products.columns.product'), cell: (p) => p.name, sortValue: (p) => p.name },
      { id: 'category', header: t('warehouse.products.columns.category'), cell: (p) => p.categoryName ?? '', sortValue: (p) => p.categoryName },
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

  // Reportes de la maqueta = vistas en pantalla del Kárdex de movimientos con los filtros de aquí (no hay PDF en el API).
  const invReportTo = useMemo(() => {
    const q = new URLSearchParams({ tab: 'balances' })
    if (warehousePublicId) q.set('warehousePublicIds', warehousePublicId)
    if (categoryIds.length > 0) q.set('categoryIds', categoryIds.join(','))
    return `/warehouse/kardex?${q.toString()}`
  }, [warehousePublicId, categoryIds])
  const adjReportTo = useMemo(() => {
    const q = new URLSearchParams({ types: ADJUSTMENT_TXN_TYPE })
    if (warehousePublicId) q.set('warehousePublicIds', warehousePublicId)
    return `/warehouse/kardex?${q.toString()}`
  }, [warehousePublicId])

  return (
    <>
      <div className="head">
        <div>
          <h1>{t('warehouse.products.title')}</h1>
          <p>{t('warehouse.products.subtitle')}</p>
        </div>
        <div className="act">
          <Link className="btn" to={invReportTo} title={t('warehouse.products.invReportHint')}>
            {t('warehouse.products.invReport')}
          </Link>
          <Link className="btn" to={adjReportTo} title={t('warehouse.products.adjReportHint')}>
            {t('warehouse.products.adjReport')}
          </Link>
          <Can perm="inventory.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.products.new')}
            </button>
          </Can>
        </div>
      </div>

      <InventoryKpis />

      <Filters
        onClear={() => {
          setPage(1)
          setText('')
          setWarehousePublicId('')
          setCategoryIds([])
          setState('')
        }}
      >
        <SelectFilter
          label={t('warehouse.products.filters.warehouse')}
          value={warehousePublicId}
          onChange={withPageReset(setWarehousePublicId)}
          options={warehouseOptions}
        />
        <SearchSelect label={t('warehouse.products.filters.category')} options={categoryOptions} value={categoryIds} onChange={withPageReset(setCategoryIds)} />
        <SelectFilter
          label={t('warehouse.products.filters.state')}
          value={state}
          onChange={(v) => withPageReset(setState)(v as StateFilter)}
          options={stateOptions}
        />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('warehouse.products.title')} badge={data ? (data.total ?? 0) : undefined}>
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
