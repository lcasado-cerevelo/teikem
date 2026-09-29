// Pantalla C (Lote F6) — Kárdex de movimientos (maqueta `ledger()`, ítem propio del menú desde la Fase 8): Kárdex, saldos,
// ajustes, transferencias, genealogía, rastro de serie y conciliación. `/warehouse/kardex` con pestañas Kárdex (la primera,
// sin parámetro) / Saldos (`?tab=balances`) / Conciliación (`?tab=reconciliation`); la vista por producto (disponible,
// reservado, total) es 'Productos e inventario'. `/warehouse/inventory` (dirección anterior) redirige aquí conservando la
// consulta. Sin StatusPipeline: el ledger no tiene estatus de entidad, solo mensajes de validación.
// Lectura: inventory.view + WMS_LOTSERIAL (aplicado por la ruta). Ajuste/transferencia/ejecutar conciliación:
// inventory.adjust.
// Parámetros de URL (Lote F7A, enlaces de Pulso y botones de reporte de 'Productos e inventario'): `tab` elige la pestaña;
// `warehousePublicIds=<publicId>` filtra por almacén y `product=<publicId>` por producto (Saldos o Kárdex);
// `categoryIds=<id>` filtra Saldos por categoría y `types=<InternalCode de InventoryTxnType>` el Kárdex por tipo (todos
// repetibles o separados por comas). Solo se leen al montar y solo los recibe la pestaña abierta: al cambiar de pestaña se
// descartan (la URL queda solo con la pestaña) y después los filtros son de la pantalla.
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { parseApiDate } from '../../kernel/api/dates'
import type { components } from '../../kernel/api/schema'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  EMPTY_RANGE,
  DateRangeFilter,
  EmptyState,
  Filters,
  Modal,
  Panel,
  QBox,
  SearchSelect,
  Spinner,
  Tabs,
  type DataColumn,
  type DateRange,
  type RowAction,
} from '../../kernel/ui'
import {
  useInventoryBalances,
  useInventoryReconciliation,
  useInventoryTransactions,
  useLotGenealogy,
  productLabel,
  useProductsByPublicId,
  useProductCategories,
  useSerialTrace,
  useWarehouses,
  warehouseLabel,
  type BalanceDto,
} from './api'
import { InventoryAdjustModal } from './InventoryAdjustModal'
import { InventoryTransferModal } from './InventoryTransferModal'
import { ProductMultiFilter, ProductPicker, type ProductFilterItem } from './pickers'
import { IconDoc, IconLayers } from '../../kernel/ui/screenIcons'

type KardexRowDto = components['schemas']['KardexRowDto']
type ReconciliationRowDto = components['schemas']['ReconciliationRowDto']

type TabKey = 'kardex' | 'balances' | 'reconciliation'
const PAGE_SIZE = 25

/** Pestaña de `?tab=`: la primera (Kárdex) va sin parámetro; valor desconocido = la primera. */
function tabFromParam(value: string | null): TabKey {
  return value === 'balances' || value === 'reconciliation' ? value : 'kardex'
}

/** Filtros iniciales que llegan en la URL (enlaces de Pulso y reportes de 'Productos e inventario'). */
interface InitialFilters {
  warehousePublicIds: string[]
  products: ProductFilterItem[]
  categoryIds: string[]
  types: string[]
}

/** Sin filtros iniciales: lo que recibe una pestaña a la que se llega cambiando de pestaña. */
const NO_INITIAL_FILTERS: InitialFilters = { warehousePublicIds: [], products: [], categoryIds: [], types: [] }

/** Valores de un parámetro repetible o separado por comas, sin vacíos ni duplicados. */
function listParam(params: URLSearchParams, name: string): string[] {
  const values = params
    .getAll(name)
    .flatMap((v) => v.split(','))
    .map((v) => v.trim())
    .filter(Boolean)
  return [...new Set(values)]
}

/** Lee `warehousePublicIds`, `product`, `categoryIds` y `types` de la URL. Un producto llega solo con su publicId: su SKU se resuelve después. */
function initialFiltersFromUrl(params: URLSearchParams): InitialFilters {
  return {
    warehousePublicIds: listParam(params, 'warehousePublicIds'),
    products: listParam(params, 'product').map((publicId) => ({ publicId, sku: '', label: '' })),
    categoryIds: listParam(params, 'categoryIds').filter((v) => /^\d+$/.test(v)),
    types: listParam(params, 'types'),
  }
}

/**
 * Productos del filtro para pintar: los que llegaron por URL sin SKU (pueden ser varios) toman "SKU · Nombre" de su ficha
 * (`GET /api/v1/products/{publicId}`); mientras llega se muestra '…' y, si la ficha no se puede leer (404/403), un texto
 * fijo para que la píldora se pueda quitar. Un elemento sin SKU sigue pendiente aunque el filtro lo haya guardado con '…'.
 */
function useResolvedProducts(products: ProductFilterItem[]): ProductFilterItem[] {
  const t = useT()
  const pending = useMemo(() => products.filter((p) => !p.sku).map((p) => p.publicId), [products])
  const results = useProductsByPublicId(pending, { handleAccessDenied: false })
  return products.map((p) => {
    if (p.sku) return p
    const r = results[pending.indexOf(p.publicId)]
    const found = r?.data?.product
    if (found) return { publicId: p.publicId, sku: found.sku ?? '', label: productLabel(found) }
    return { ...p, label: r?.isError ? t('warehouse.inventory.productUnavailable') : '…' }
  })
}

function formatDate(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium' }).format(date)
}

function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

/** Filtro booleano (fuera de un <Form>: no usa react-hook-form), como en ProductListScreen. */
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

/**
 * Columnas de un Kárdex (compartidas por la pestaña y los modales de genealogía / rastro de serie).
 * En la pestaña Kárdex la lista viene paginada del servidor (skip/take) y el endpoint no acepta orden: el orden por
 * encabezado solo reacomoda la página visible (pendiente un parámetro de orden en el endpoint).
 */
function useKardexColumns(): DataColumn<KardexRowDto>[] {
  const t = useT()
  const lang = useLang()
  return useMemo(
    (): DataColumn<KardexRowDto>[] => [
      {
        id: 'date',
        header: t('warehouse.inventory.kardex.columns.date'),
        cell: (r) => formatDateTime(r.createdAtUtc, lang),
        sortValue: (r) => r.createdAtUtc,
        card: 'title',
      },
      { id: 'type', header: t('warehouse.inventory.kardex.columns.type'), cell: (r) => <Chip>{r.type ?? r.typeCode}</Chip>, sortValue: (r) => r.type ?? r.typeCode },
      { id: 'sku', header: t('warehouse.inventory.kardex.columns.sku'), cell: (r) => <span className="ref">{r.sku}</span>, sortValue: (r) => r.sku },
      { id: 'product', header: t('warehouse.inventory.kardex.columns.product'), cell: (r) => r.productName, sortValue: (r) => r.productName },
      {
        id: 'warehouse',
        header: t('warehouse.inventory.kardex.columns.warehouse'),
        cell: (r) => [r.fromWarehouseCode, r.toWarehouseCode].filter(Boolean).join(' → ') || r.fromWarehouseCode || r.toWarehouseCode || '',
        sortValue: (r) => r.fromWarehouseCode ?? r.toWarehouseCode,
      },
      {
        id: 'bin',
        header: t('warehouse.inventory.kardex.columns.bin'),
        cell: (r) => [r.fromBinCode, r.toBinCode].filter(Boolean).join(' → ') || r.fromBinCode || r.toBinCode || '',
        sortValue: (r) => r.fromBinCode ?? r.toBinCode,
      },
      {
        id: 'quantity',
        header: t('warehouse.inventory.kardex.columns.quantity'),
        cell: (r) => {
          const q = r.signedQuantity ?? r.quantity ?? 0
          return <span className="ref">{q > 0 ? `+${q}` : q}</span>
        },
        sortValue: (r) => r.signedQuantity ?? r.quantity,
        align: 'end',
      },
      {
        id: 'reason',
        header: t('warehouse.inventory.kardex.columns.reason'),
        cell: (r) => (
          <span title={r.notes ?? undefined}>{r.reason ?? r.reasonCode ?? ''}</span>
        ),
        sortValue: (r) => r.reason ?? r.reasonCode,
      },
      { id: 'ref', header: t('warehouse.inventory.kardex.columns.ref'), cell: (r) => r.refLabel ?? '', sortValue: (r) => r.refLabel },
    ],
    [t, lang],
  )
}

// =====================================================================================================================
// Modal de genealogía de lote (se abre desde una fila de Saldos con lotId)
// =====================================================================================================================
function GenealogyModal({ lotId, onClose }: { lotId: number | null; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const kardexColumns = useKardexColumns()
  const { data, isLoading, error } = useLotGenealogy(lotId)

  const destinationColumns = useMemo<DataColumn<components['schemas']['GenealogyDestinationDto']>[]>(
    () => [
      { id: 'ref', header: t('warehouse.inventory.kardex.columns.ref'), cell: (d) => d.refLabel ?? '', sortValue: (d) => d.refLabel, card: 'title' },
      { id: 'client', header: t('warehouse.inventory.genealogy.client'), cell: (d) => d.clientName ?? '', sortValue: (d) => d.clientName },
      {
        id: 'consignee',
        header: t('warehouse.inventory.genealogy.consignee'),
        cell: (d) => d.consigneeName ?? '',
        sortValue: (d) => d.consigneeName,
        card: 'hidden',
      },
      { id: 'quantity', header: t('warehouse.inventory.kardex.columns.quantity'), cell: (d) => d.quantity, sortValue: (d) => d.quantity, align: 'end' },
    ],
    [t],
  )

  return (
    <Modal open={lotId != null} title={t('warehouse.inventory.genealogy.title', { lot: data?.lotNumber ?? '' })} onClose={onClose} size="lg">
      {isLoading && <Spinner block label={t('common.loading')} />}
      {error && (
        <p className="pb ferr" role="alert">
          {error.message}
        </p>
      )}
      {data && (
        <div className="pb">
          <div className="r2">
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.manufactureDate')}</label>
              <p>{formatDate(data.manufactureDate, lang) || '—'}</p>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.expiryDate')}</label>
              <p>{formatDate(data.expiryDate, lang) || '—'}</p>
            </div>
          </div>
          <div className="r3">
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.qtyIn')}</label>
              <p>{data.qtyIn}</p>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.qtyOut')}</label>
              <p>{data.qtyOut}</p>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.genealogy.qtyOnHand')}</label>
              <p>{data.qtyOnHand}</p>
            </div>
          </div>
          <h3>{t('warehouse.inventory.genealogy.movements')}</h3>
          <DataTable
            label={t('warehouse.inventory.genealogy.movements')}
            columns={kardexColumns}
            rows={data.movements ?? []}
            rowKey={(m) => m.id ?? 0}
            pageSize={10}
            dense
          />
          <h3>{t('warehouse.inventory.genealogy.destinations')}</h3>
          {data.destinations && data.destinations.length > 0 ? (
            <DataTable
              label={t('warehouse.inventory.genealogy.destinations')}
              columns={destinationColumns}
              rows={data.destinations}
              rowKey={(d) => `${d.refEntityCode ?? ''}-${d.refId ?? 0}`}
              pageSize={10}
              dense
            />
          ) : (
            <EmptyState title={t('warehouse.inventory.genealogy.noDestinations')} />
          )}
        </div>
      )}
    </Modal>
  )
}

// =====================================================================================================================
// Modal de rastro de serie (se abre desde una fila de Saldos o Kárdex)
// =====================================================================================================================
function SerialTraceModal({
  productPublicId,
  initialSerialNumber,
  onClose,
}: {
  productPublicId: string | null
  initialSerialNumber: string
  onClose: () => void
}) {
  const t = useT()
  const lang = useLang()
  const kardexColumns = useKardexColumns()
  // Montado solo mientras hay una petición abierta (ver `key` en InventoryScreen): el estado inicial ya trae el
  // número de serie de la fila que abrió el modal, sin necesidad de sincronizarlo con un efecto.
  const [serialNumber, setSerialNumber] = useState(initialSerialNumber)

  const { data, isLoading, error } = useSerialTrace({ productPublicId: productPublicId ?? undefined, serialNumber: serialNumber || undefined })

  return (
    <Modal open title={t('warehouse.inventory.serialTrace.title')} onClose={onClose} size="lg">
      <div className="pb">
        <div className="f">
          <label htmlFor="serial-trace-input">{t('warehouse.inventory.serialTrace.serialNumber')}</label>
          <input id="serial-trace-input" type="text" value={serialNumber} onChange={(e) => setSerialNumber(e.target.value)} maxLength={60} />
        </div>
        {isLoading && <Spinner block label={t('common.loading')} />}
        {error && (
          <p className="ferr" role="alert">
            {error.message}
          </p>
        )}
        {!isLoading && !error && serialNumber && !data && <p className="help">{t('warehouse.inventory.serialTrace.notFound')}</p>}
        {data && (
          <>
            <div className="r3">
              <div className="f">
                <label>{t('warehouse.inventory.serialTrace.status')}</label>
                <p>{data.serial?.status ?? ''}</p>
              </div>
              <div className="f">
                <label>{t('warehouse.inventory.serialTrace.warehouse')}</label>
                <p>{data.serial?.warehouseCode ?? '—'}</p>
              </div>
              <div className="f">
                <label>{t('warehouse.inventory.serialTrace.bin')}</label>
                <p>{data.serial?.binCode ?? '—'}</p>
              </div>
            </div>
            <div className="f">
              <label>{t('warehouse.inventory.serialTrace.lot')}</label>
              <p>{data.serial?.lotNumber ?? '—'}</p>
            </div>
            <h3>{t('warehouse.inventory.serialTrace.movements')}</h3>
            <DataTable
              label={t('warehouse.inventory.serialTrace.movements')}
              columns={kardexColumns}
              rows={data.movements ?? []}
              rowKey={(m) => m.id ?? 0}
              pageSize={10}
              dense
            />
            <h3>{t('warehouse.inventory.serialTrace.statusHistory')}</h3>
            {(data.statusHistory ?? []).length > 0 ? (
              <ul>
                {(data.statusHistory ?? []).map((h) => (
                  <li key={h.id}>
                    {formatDateTime(h.changedAtUtc, lang)} — {h.fromLabel ?? h.fromCode ?? '—'} → {h.toLabel ?? h.toCode} ({h.changedByName ?? ''})
                  </li>
                ))}
              </ul>
            ) : (
              <p className="help">—</p>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}

// =====================================================================================================================
// Pestaña Saldos
// =====================================================================================================================
function BalancesTab({
  initial,
  onGenealogy,
  onSerialTrace,
}: {
  initial: InitialFilters
  onGenealogy: (lotId: number) => void
  onSerialTrace: (productPublicId: string) => void
}) {
  const t = useT()
  const lang = useLang()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [warehousePublicIds, setWarehousePublicIds] = useState<string[]>(initial.warehousePublicIds)
  const [products, setProducts] = useState<ProductFilterItem[]>(initial.products)
  const [categoryIds, setCategoryIds] = useState<string[]>(initial.categoryIds)
  const [lotNumber, setLotNumber] = useState('')
  const [includeZero, setIncludeZero] = useState(false)
  const [onlyAvailable, setOnlyAvailable] = useState(false)
  const [page, setPage] = useState(1)

  // Paginación del servidor: todo cambio de filtro o del buscador vuelve a la página 1.
  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }

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

  const productPublicIds = useMemo(() => products.map((p) => p.publicId), [products])
  const shownProducts = useResolvedProducts(products)
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const { data: categories = [] } = useProductCategories()
  const categoryOptions = useMemo(() => categories.map((c) => ({ value: String(c.id), label: c.name ?? '' })), [categories])

  const query = useMemo(
    () => ({
      warehousePublicIds: warehousePublicIds.length > 0 ? warehousePublicIds : undefined,
      productPublicIds: productPublicIds.length > 0 ? productPublicIds : undefined,
      categoryIds: categoryIds.length > 0 ? categoryIds.map(Number) : undefined,
      lotNumber: lotNumber || undefined,
      includeZero: includeZero || undefined,
      onlyAvailable: onlyAvailable || undefined,
      search: search || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [warehousePublicIds, productPublicIds, categoryIds, lotNumber, includeZero, onlyAvailable, search, page],
  )
  const { data, isLoading, error } = useInventoryBalances(query)

  const columns = useMemo<DataColumn<BalanceDto>[]>(
    () => [
      // Orden en el cliente: la lista es paginada por el servidor (sin parámetro de orden), así que solo reacomoda la página visible.
      { id: 'warehouse', header: t('warehouse.inventory.balances.columns.warehouse'), cell: (b) => b.warehouseCode, sortValue: (b) => b.warehouseCode },
      { id: 'bin', header: t('warehouse.inventory.balances.columns.bin'), cell: (b) => b.binCode ?? '', sortValue: (b) => b.binCode },
      { id: 'zone', header: t('warehouse.inventory.balances.columns.zone'), cell: (b) => b.zoneCode ?? '', sortValue: (b) => b.zoneCode, card: 'hidden' },
      { id: 'sku', header: t('warehouse.inventory.balances.columns.sku'), cell: (b) => <span className="ref">{b.sku}</span>, sortValue: (b) => b.sku, card: 'title' },
      { id: 'product', header: t('warehouse.inventory.balances.columns.product'), cell: (b) => b.productName, sortValue: (b) => b.productName },
      { id: 'lot', header: t('warehouse.inventory.balances.columns.lot'), cell: (b) => b.lotNumber ?? '', sortValue: (b) => b.lotNumber },
      { id: 'expiry', header: t('warehouse.inventory.balances.columns.expiry'), cell: (b) => formatDate(b.expiryDate, lang), sortValue: (b) => b.expiryDate },
      { id: 'onHand', header: t('warehouse.inventory.balances.columns.onHand'), cell: (b) => b.qtyOnHand, sortValue: (b) => b.qtyOnHand, align: 'end' },
      { id: 'reserved', header: t('warehouse.inventory.balances.columns.reserved'), cell: (b) => b.qtyReserved, sortValue: (b) => b.qtyReserved, align: 'end' },
      { id: 'available', header: t('warehouse.inventory.balances.columns.available'), cell: (b) => b.qtyAvailable, sortValue: (b) => b.qtyAvailable, align: 'end' },
      { id: 'cost', header: t('warehouse.inventory.balances.columns.cost'), cell: (b) => b.costValue ?? '', sortValue: (b) => b.costValue, align: 'end', card: 'hidden' },
      { id: 'sale', header: t('warehouse.inventory.balances.columns.sale'), cell: (b) => b.saleValue ?? '', sortValue: (b) => b.saleValue, align: 'end', card: 'hidden' },
      {
        id: 'updated',
        header: t('warehouse.inventory.balances.columns.updated'),
        cell: (b) => formatDateTime(b.updatedAtUtc, lang),
        sortValue: (b) => b.updatedAtUtc,
        card: 'hidden',
      },
    ],
    [t, lang],
  )

  const actions = useMemo<RowAction<BalanceDto>[]>(
    () => [
      {
        key: 'genealogy',
        label: t('warehouse.inventory.balances.actions.genealogy'),
        perm: 'inventory.view',
        visible: (b) => b.lotId != null,
        onClick: (b) => onGenealogy(b.lotId!),
      },
      {
        key: 'serialTrace',
        label: t('warehouse.inventory.balances.actions.serialTrace'),
        perm: 'inventory.view',
        onClick: (b) => onSerialTrace(b.productPublicId!),
      },
    ],
    [t, onGenealogy, onSerialTrace],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setPage(1)
          setWarehousePublicIds([])
          setProducts([])
          setCategoryIds([])
          setLotNumber('')
          setIncludeZero(false)
          setOnlyAvailable(false)
        }}
      >
        <SearchSelect
          label={t('warehouse.inventory.balances.filters.warehouse')}
          options={warehouseOptions}
          value={warehousePublicIds}
          onChange={withPageReset(setWarehousePublicIds)}
        />
        <ProductMultiFilter label={t('warehouse.inventory.balances.filters.product')} value={shownProducts} onChange={withPageReset(setProducts)} />
        <SearchSelect
          label={t('warehouse.inventory.balances.filters.category')}
          options={categoryOptions}
          value={categoryIds}
          onChange={withPageReset(setCategoryIds)}
        />
        <div className="f">
          <label htmlFor="balances-lot">{t('warehouse.inventory.balances.filters.lotNumber')}</label>
          <input id="balances-lot" type="text" value={lotNumber} onChange={(e) => withPageReset(setLotNumber)(e.target.value)} maxLength={60} />
        </div>
        <ToggleFilter label={t('warehouse.inventory.balances.filters.includeZero')} checked={includeZero} onChange={withPageReset(setIncludeZero)} />
        <ToggleFilter label={t('warehouse.inventory.balances.filters.onlyAvailable')} checked={onlyAvailable} onChange={withPageReset(setOnlyAvailable)} />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('warehouse.inventory.tabBalances')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={setText} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.inventory.tabBalances')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(b) => b.id ?? 0}
            loading={isLoading}
            page={page}
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            rowActions={actions}
          />
        )}
      </Panel>
    </>
  )
}

// =====================================================================================================================
// Pestaña Kárdex
// =====================================================================================================================
function KardexTab({
  initial,
  onSerialTrace,
}: {
  initial: InitialFilters
  onSerialTrace: (productPublicId: string, serialNumber: string) => void
}) {
  const t = useT()
  const columns = useKardexColumns()
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE)
  const [types, setTypes] = useState<string[]>(initial.types)
  const [warehousePublicIds, setWarehousePublicIds] = useState<string[]>(initial.warehousePublicIds)
  const [products, setProducts] = useState<ProductFilterItem[]>(initial.products)
  const [lotNumber, setLotNumber] = useState('')
  const [serialNumber, setSerialNumber] = useState('')
  const [page, setPage] = useState(1)

  // Paginación del servidor: todo cambio de filtro o del buscador vuelve a la página 1.
  function withPageReset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }

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

  const productPublicIds = useMemo(() => products.map((p) => p.publicId), [products])
  const shownProducts = useResolvedProducts(products)
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const { data: txnTypes = [] } = useLookups('InventoryTxnType')
  const typeOptions = useMemo(() => txnTypes.map((o) => ({ value: o.code, label: o.label })), [txnTypes])

  const rangeInvalid = Boolean(range.from && range.to && range.from > range.to)

  const query = useMemo(
    () => ({
      from: range.from || undefined,
      to: range.to || undefined,
      types: types.length > 0 ? types : undefined,
      warehousePublicIds: warehousePublicIds.length > 0 ? warehousePublicIds : undefined,
      productPublicIds: productPublicIds.length > 0 ? productPublicIds : undefined,
      lotNumber: lotNumber || undefined,
      serialNumber: serialNumber || undefined,
      search: search || undefined,
      skip: (page - 1) * PAGE_SIZE,
      take: PAGE_SIZE,
    }),
    [range, types, warehousePublicIds, productPublicIds, lotNumber, serialNumber, search, page],
  )
  const { data, isLoading, error } = useInventoryTransactions(query, { enabled: !rangeInvalid })

  const actions = useMemo<RowAction<KardexRowDto>[]>(
    () => [
      {
        key: 'serialTrace',
        label: t('warehouse.inventory.kardex.actions.serialTrace'),
        perm: 'inventory.view',
        visible: (r) => Boolean(r.serialNumber) && Boolean(r.productPublicId),
        onClick: (r) => onSerialTrace(r.productPublicId!, r.serialNumber!),
      },
    ],
    [t, onSerialTrace],
  )

  return (
    <>
      <Filters
        onClear={() => {
          setPage(1)
          setRange(EMPTY_RANGE)
          setTypes([])
          setWarehousePublicIds([])
          setProducts([])
          setLotNumber('')
          setSerialNumber('')
        }}
      >
        <DateRangeFilter label={t('warehouse.inventory.kardex.filters.range')} value={range} onChange={withPageReset(setRange)} />
        <SearchSelect label={t('warehouse.inventory.kardex.filters.type')} options={typeOptions} value={types} onChange={withPageReset(setTypes)} />
        <SearchSelect
          label={t('warehouse.inventory.kardex.filters.warehouse')}
          options={warehouseOptions}
          value={warehousePublicIds}
          onChange={withPageReset(setWarehousePublicIds)}
        />
        <ProductMultiFilter label={t('warehouse.inventory.kardex.filters.product')} value={shownProducts} onChange={withPageReset(setProducts)} includeInactive />
        <div className="f">
          <label htmlFor="kardex-lot">{t('warehouse.inventory.kardex.filters.lotNumber')}</label>
          <input id="kardex-lot" type="text" value={lotNumber} onChange={(e) => withPageReset(setLotNumber)(e.target.value)} maxLength={60} />
        </div>
        <div className="f">
          <label htmlFor="kardex-serial">{t('warehouse.inventory.kardex.filters.serialNumber')}</label>
          <input id="kardex-serial" type="text" value={serialNumber} onChange={(e) => withPageReset(setSerialNumber)(e.target.value)} maxLength={60} />
        </div>
      </Filters>

      <Panel flush icon={<IconDoc />} title={t('warehouse.inventory.tabKardex')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="qrow">
          <QBox value={text} onChange={setText} />
        </div>
        {rangeInvalid ? (
          <p className="pb ferr" role="alert">
            {t('warehouse.inventory.kardex.errors.fromAfterTo')}
          </p>
        ) : error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.inventory.tabKardex')}
            columns={columns}
            rows={data?.items ?? []}
            rowKey={(r) => r.id ?? 0}
            loading={isLoading}
            page={page}
            pageSize={PAGE_SIZE}
            total={data?.total ?? 0}
            onPage={setPage}
            rowActions={actions}
          />
        )}
      </Panel>
    </>
  )
}

// =====================================================================================================================
// Pestaña Conciliación
// =====================================================================================================================
function ReconciliationTab() {
  const t = useT()
  const lang = useLang()
  const [productPublicId, setProductPublicId] = useState<string | null>(null)
  const { data, isLoading, error, refetch, isFetched } = useInventoryReconciliation(
    { productPublicId: productPublicId ?? undefined },
    { enabled: false },
  )

  const mismatchColumns = useMemo<DataColumn<ReconciliationRowDto>[]>(
    () => [
      { id: 'sku', header: t('warehouse.inventory.reconciliation.columns.sku'), cell: (r) => <span className="ref">{r.sku}</span>, sortValue: (r) => r.sku, card: 'title' },
      { id: 'warehouse', header: t('warehouse.inventory.reconciliation.columns.warehouse'), cell: (r) => r.warehouseCode ?? '', sortValue: (r) => r.warehouseCode },
      { id: 'bin', header: t('warehouse.inventory.reconciliation.columns.bin'), cell: (r) => r.binCode ?? '', sortValue: (r) => r.binCode },
      { id: 'lot', header: t('warehouse.inventory.reconciliation.columns.lot'), cell: (r) => r.lotNumber ?? '', sortValue: (r) => r.lotNumber },
      { id: 'ledger', header: t('warehouse.inventory.reconciliation.columns.ledgerQty'), cell: (r) => r.ledgerQty, sortValue: (r) => r.ledgerQty, align: 'end' },
      { id: 'balance', header: t('warehouse.inventory.reconciliation.columns.balanceQty'), cell: (r) => r.balanceQty, sortValue: (r) => r.balanceQty, align: 'end' },
    ],
    [t],
  )

  return (
    <Panel icon={<IconDoc />} title={t('warehouse.inventory.tabReconciliation')} subtitle={t('warehouse.inventory.reconciliation.subtitle')}>
      <div className="r2">
        <div className="f">
          <label>{t('warehouse.inventory.reconciliation.product')}</label>
          <ProductPicker value={productPublicId} onChange={(publicId) => setProductPublicId(publicId)} />
        </div>
        <Can perm="inventory.adjust">
          <div className="f">
            <label>&nbsp;</label>
            <button type="button" className="btn flow" onClick={() => refetch()} disabled={isLoading}>
              {isLoading ? t('common.loading') : t('warehouse.inventory.reconciliation.run')}
            </button>
          </div>
        </Can>
      </div>
      {error && (
        <p className="ferr" role="alert">
          {error.message}
        </p>
      )}
      {data && (
        <>
          <p>
            {t('warehouse.inventory.reconciliation.checkedAt')}: {formatDateTime(data.checkedAtUtc, lang)} ·{' '}
            {t('warehouse.inventory.reconciliation.balancesChecked', { count: data.balancesChecked ?? 0 })}
          </p>
          {(data.mismatches ?? []).length > 0 ? (
            <DataTable
              label={t('warehouse.inventory.tabReconciliation')}
              columns={mismatchColumns}
              rows={data.mismatches ?? []}
              rowKey={(r) => `${r.productPublicId ?? ''}-${r.binCode ?? ''}-${r.lotNumber ?? ''}`}
              pageSize={25}
            />
          ) : (
            <EmptyState title={t('warehouse.inventory.reconciliation.ok')} />
          )}
        </>
      )}
      {!data && !error && isFetched && !isLoading && <EmptyState title={t('warehouse.inventory.reconciliation.ok')} />}
    </Panel>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function InventoryScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  // La pestaña va en la URL (`?tab=balances|reconciliation`; el Kárdex, la primera, sin parámetro) para poder enlazarla.
  const tab = tabFromParam(params.get('tab'))
  // Solo al montar: los enlaces de Pulso y los reportes abren la pestaña y los filtros indicados en la URL. Esos filtros son
  // de la pestaña con que se abrió: al cambiar de pestaña se descartan (la otra no los hereda y al volver no reaparecen).
  const [initial, setInitial] = useState(() => ({ tab, filters: initialFiltersFromUrl(params) }))
  const initialFor = (key: TabKey) => (initial.tab === key ? initial.filters : NO_INITIAL_FILTERS)

  function changeTab(next: TabKey) {
    if (next === tab) return
    setInitial({ tab: next, filters: NO_INITIAL_FILTERS })
    setParams(next === 'kardex' ? {} : { tab: next }, { replace: true })
  }
  const [adjusting, setAdjusting] = useState(false)
  const [transferring, setTransferring] = useState(false)
  const [genealogyLotId, setGenealogyLotId] = useState<number | null>(null)
  const [serialTrace, setSerialTrace] = useState<{ productPublicId: string; serialNumber: string } | null>(null)

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.inventory.title')}</h1>
          <p>{t('warehouse.inventory.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="inventory.adjust">
            <button type="button" className="btn" onClick={() => setAdjusting(true)}>
              {t('warehouse.inventory.adjust')}
            </button>
            <button type="button" className="btn flow" onClick={() => setTransferring(true)}>
              {t('warehouse.inventory.transfer')}
            </button>
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.inventory.title')}
          value={tab}
          onChange={changeTab}
          tabs={[
            { key: 'kardex', label: t('warehouse.inventory.tabKardex') },
            { key: 'balances', label: t('warehouse.inventory.tabBalances') },
            { key: 'reconciliation', label: t('warehouse.inventory.tabReconciliation') },
          ]}
        />
      </div>

      {tab === 'kardex' && (
        <KardexTab
          initial={initialFor('kardex')}
          onSerialTrace={(productPublicId, serialNumber) => setSerialTrace({ productPublicId, serialNumber })}
        />
      )}
      {tab === 'balances' && (
        <BalancesTab
          initial={initialFor('balances')}
          onGenealogy={setGenealogyLotId}
          onSerialTrace={(productPublicId) => setSerialTrace({ productPublicId, serialNumber: '' })}
        />
      )}
      {tab === 'reconciliation' && <ReconciliationTab />}

      <InventoryAdjustModal open={adjusting} onClose={() => setAdjusting(false)} />
      <InventoryTransferModal open={transferring} onClose={() => setTransferring(false)} />
      <GenealogyModal lotId={genealogyLotId} onClose={() => setGenealogyLotId(null)} />
      {serialTrace && (
        <SerialTraceModal
          key={`${serialTrace.productPublicId}-${serialTrace.serialNumber}`}
          productPublicId={serialTrace.productPublicId}
          initialSerialNumber={serialTrace.serialNumber}
          onClose={() => setSerialTrace(null)}
        />
      )}
    </div>
  )
}
