// Ubicaciones (`/warehouse/locations`, maqueta `ubicaciones()`), Lote 1 (1.3): vista global de las posiciones de un almacén.
// - Almacén elegido arriba (`?warehouse=`; sin él, el primero activo) y "Nueva posición" (warehouse.manage, mismo `BinModal`
//   que la ficha del almacén). Lectura con inventory.view + WMS_LOTSERIAL (por la ruta).
// - Río: un recuadro por zona con lo ocupado vs. la capacidad total de la zona (Σ cupo de sus posiciones, calculado por el
//   API en el listado de zonas); si hay posiciones sin cupo se avisa en el recuadro y, si ninguna tiene cupo, se muestra la
//   existencia total sin porcentaje. Clic en un recuadro = filtro Zona de la tabla (`?zone=<id>`); otro clic en el mismo, o
//   "Limpiar", lo quita. Las barras de los últimos 7 días de la maqueta NO se pintan: dependen de un historial diario que
//   todavía no existe (decisión del dueño del producto, docs/lote1-decisiones.md).
// - Tabla paginada en el servidor (`GET /warehouses/{id}/bins` con `skip`/`take`): Posición, Zona, Cantidad, Producto,
//   Cupo, Ocupación y Estatus (Vacía / Parcial / Llena / Sin cupo, `occupancy` del API). Filtros al servidor: Zona
//   (`zoneIds`), Tipo (se traduce a las zonas de ese tipo), Producto (`productPublicIds`) y Estatus (`occupancy`).
//   Exportar saca todo lo filtrado (`fetchAllPages`). Lógica pura en `locations.ts`.
// - Lote 11: "Asignar cupo" (warehouse.manage) junto a "Nueva posición" abre `BinCapacityModal` (cupo máximo en bloque)
//   sobre el almacén elegido, con la zona de `?zone=` ya puesta; al aplicar se refrescan la tabla y los recuadros.
// - Lote F14: "Códigos de barras" en la cabecera de la tabla: PDF con un código por posición de lo filtrado (misma consulta
//   que la tabla y Exportar), para imprimir y escanear el papel en el conteo (BarcodeReportButtons / barcodeReports.ts).
import { useId, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  EmptyState,
  Filters,
  IconGrid,
  IconWarehouse,
  Panel,
  SearchSelect,
  Spinner,
  type DataColumn,
} from '../../kernel/ui'
import { useWarehouseBins, useWarehouseZones, useWarehouses, type WarehouseBinDto, type WarehouseZoneDto } from './api'
import { BinBarcodeReportButton } from './BarcodeReportButtons'
import { BinCapacityModal } from './BinCapacityModal'
import { BinModal } from './BinModal'
import { formatNumber, useDebounced } from './lineRules'
import {
  BIN_OCCUPANCIES,
  OCCUPANCY_TONE,
  binFillPct,
  binOccupancy,
  binProductCell,
  buildBinListQuery,
  parseZoneParam,
  toggleZoneSelection,
  zoneCapacities,
  type ZoneCapacity,
} from './locations'
import { TextFilter } from './filterControls'
import { ProductMultiFilter, WarehousePicker, type ProductFilterItem } from './pickers'
import '../analytics/pulse.css'
import './warehouse.css'

const PAGE_SIZE = 25
const NO_BINS: WarehouseBinDto[] = []
const NO_ZONES: WarehouseZoneDto[] = []

/** Recuadro del río: una zona con lo ocupado vs. su capacidad; es un botón que filtra la tabla por esa zona. */
function ZoneNode({ item, active, onToggle }: { item: ZoneCapacity; active: boolean; onToggle: () => void }) {
  const t = useT()
  const lang = useLang()
  const fmt = (n: number) => formatNumber(n, lang)
  const { zone, mode, capacity, occupied, pct, binsWithoutCapacity, qtyOnHand } = item
  const name = zone.name || zone.code || ''
  const withoutCapacity =
    mode === 'capacity' && binsWithoutCapacity > 0
      ? binsWithoutCapacity === 1
        ? t('warehouse.locations.binsWithoutCapacityOne')
        : t('warehouse.locations.binsWithoutCapacity', { count: fmt(binsWithoutCapacity) })
      : null
  let aria: string
  if (mode === 'capacity') aria = t('warehouse.locations.zoneAriaCapacity', { zone: name, occupied: fmt(occupied), capacity: fmt(capacity), pct: pct ?? 0 })
  else if (mode === 'noCapacity') aria = t('warehouse.locations.zoneAriaNoCapacity', { zone: name, qty: fmt(qtyOnHand) })
  else aria = t('warehouse.locations.zoneAriaNoBins', { zone: name })
  if (withoutCapacity) aria = `${aria}. ${withoutCapacity}`

  return (
    <button
      type="button"
      className={active ? 'node flow loc-node on' : 'node flow loc-node'}
      aria-pressed={active}
      aria-label={aria}
      title={t('warehouse.locations.zoneFilterHint')}
      onClick={onToggle}
    >
      <span className="ph">
        <IconGrid />
        <span>{name}</span>
        <span className="tag loc-tag">{zone.code}</span>
      </span>
      {mode === 'capacity' ? (
        <>
          <span className="big">
            {fmt(occupied)}
            <span className="loc-of">/{fmt(capacity)}</span>
          </span>
          <span className="sub">{t('warehouse.locations.usedPct', { pct: pct ?? 0 })}</span>
          {/* barra del % real (ocupado / capacidad); no es la serie de 7 días de la maqueta */}
          <span className="loc-bar loc-node-bar" aria-hidden="true">
            <i style={{ width: `${Math.min(100, pct ?? 0)}%` }} />
          </span>
          {withoutCapacity && <span className="sub loc-warn">{withoutCapacity}</span>}
        </>
      ) : mode === 'noCapacity' ? (
        <>
          <span className="big">{fmt(qtyOnHand)}</span>
          <span className="sub">{t('warehouse.locations.noCapacity')}</span>
        </>
      ) : (
        <>
          <span className="big">0</span>
          <span className="sub">{t('warehouse.locations.noBinsInZone')}</span>
        </>
      )}
    </button>
  )
}

/** Barra de ocupación de la fila: % real respecto al cupo de la posición (recortada a 100 si lo excede). */
function FillBar({ pct }: { pct: number }) {
  const t = useT()
  return (
    <span className="loc-fill">
      <span className="loc-bar" role="img" aria-label={t('warehouse.locations.fillAria', { pct })}>
        <i style={{ width: `${Math.min(100, pct)}%` }} />
      </span>
      <span className="mono loc-pct" aria-hidden="true">
        {pct}%
      </span>
    </span>
  )
}

interface BodyProps {
  warehousePublicId: string
  /** Almacén elegido (código y nombre) para el reporte de códigos de barras. */
  warehouse: { code?: string | null; name?: string | null }
  zones: readonly WarehouseZoneDto[]
  zonesLoading: boolean
  zonesError: Error | null
}

/**
 * Río, filtros y tabla de UN almacén. La pantalla la monta con `key={almacén}`: al cambiar de almacén los filtros y la
 * página vuelven a cero y la tabla no enseña, mientras carga, las posiciones del almacén anterior.
 */
function LocationsBody({ warehousePublicId, warehouse, zones, zonesLoading, zonesError }: BodyProps) {
  const t = useT()
  const lang = useLang()
  const [params, setParams] = useSearchParams()
  const [zoneTypes, setZoneTypes] = useState<string[]>([])
  const [products, setProducts] = useState<ProductFilterItem[]>([])
  const [occupancy, setOccupancy] = useState<string[]>([])
  // clic en la fila = editar la posición (mismo BinModal que la pestaña Posiciones de la ficha); solo con warehouse.manage
  const canManage = useCan('warehouse.manage')
  const [editing, setEditing] = useState<WarehouseBinDto | null>(null)
  // Posición: texto al servidor (`search`: código, pasillo, rack, nivel o posición), con retardo para no consultar por tecla
  const [code, setCode] = useState('')
  const search = useDebounced(code.trim(), 300)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)

  // Zona vive en la URL (`?zone=`): la comparten el filtro de arriba y los recuadros del río. Con las zonas cargadas se
  // descartan las que no son de este almacén (enlace viejo).
  const urlZoneIds = useMemo(() => parseZoneParam(params.getAll('zone')), [params])
  const zoneIds = useMemo(
    () => (zonesLoading ? urlZoneIds : urlZoneIds.filter((id) => zones.some((z) => String(z.id) === id))),
    [urlZoneIds, zones, zonesLoading],
  )
  const setZoneIds = (ids: readonly string[]) => {
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.delete('zone')
        for (const id of ids) next.append('zone', id)
        return next
      },
      { replace: true },
    )
    setPage(1)
  }
  /** Cambia un filtro local y vuelve a la página 1. */
  const withPageReset =
    <V,>(set: (v: V) => void) =>
    (v: V) => {
      set(v)
      setPage(1)
    }

  const { query: baseQuery, impossible } = useMemo(
    () => buildBinListQuery({ zoneIds, zoneTypes, productPublicIds: products.map((p) => p.publicId), occupancy }, zones),
    [zoneIds, zoneTypes, products, occupancy, zones],
  )
  const searchQuery = useMemo(() => ({ ...baseQuery, search: search || undefined }), [baseQuery, search])
  const query = useMemo(() => ({ ...searchQuery, skip: (page - 1) * pageSize, take: pageSize }), [searchQuery, page, pageSize])
  const binsQ = useWarehouseBins(warehousePublicId, query, { enabled: !impossible })
  const rows = impossible ? NO_BINS : (binsQ.data?.items ?? NO_BINS)
  const total = impossible ? 0 : (binsQ.data?.total ?? 0)
  const filtered = zoneIds.length + zoneTypes.length + products.length + occupancy.length > 0 || search !== ''

  const capacities = useMemo(() => zoneCapacities(zones), [zones])

  const zoneOptions = useMemo(() => zones.map((z) => ({ value: String(z.id), label: z.code ?? '' })), [zones])
  const typeOptions = useMemo(() => {
    const seen = new Map<string, string>()
    for (const z of zones) if (z.zoneTypeCode && !seen.has(z.zoneTypeCode)) seen.set(z.zoneTypeCode, z.zoneType || z.zoneTypeCode)
    return [...seen].map(([value, label]) => ({ value, label }))
  }, [zones])
  const statusOptions = useMemo(() => BIN_OCCUPANCIES.map((s) => ({ value: s, label: t(`warehouse.locations.occupancy.${s}`) })), [t])

  const clearAll = () => {
    setCode('')
    setZoneTypes([])
    setProducts([])
    setOccupancy([])
    setZoneIds([])
  }

  const columns = useMemo<DataColumn<WarehouseBinDto>[]>(() => {
    const zoneById = new Map(zones.map((z) => [z.id, z]))
    const zoneName = (b: WarehouseBinDto) => zoneById.get(b.zoneId)?.name || b.zoneCode || ''
    const productText = (b: WarehouseBinDto): string | undefined => {
      const cell = binProductCell(b)
      if (cell.kind === 'one') return cell.name
      if (cell.kind === 'many') return t('warehouse.locations.products', { count: formatNumber(cell.count, lang) })
      return undefined
    }
    return [
      {
        id: 'code',
        header: t('warehouse.locations.columns.bin'),
        // Lote F12: una posición creada desde un conteo, discreta, con su chip hasta que el supervisor la confirme
        cell: (b) => (
          <span className="cc-bincell">
            <span className="ref">{b.code}</span>
            {b.isProvisional && (
              <Chip tone="warn" title={t('warehouse.bins.provisionalHelp')}>
                {t('warehouse.bins.provisional')}
              </Chip>
            )}
          </span>
        ),
        sortValue: (b) => b.code,
        exportValue: (b) => (b.isProvisional ? `${b.code ?? ''} (${t('warehouse.bins.provisional')})` : (b.code ?? '')),
        card: 'title',
      },
      { id: 'zone', header: t('warehouse.locations.columns.zone'), cell: (b) => zoneName(b), sortValue: (b) => zoneName(b) },
      {
        id: 'qty',
        header: t('warehouse.locations.columns.qty'),
        cell: (b) => <span className="mono">{b.qtyOnHand ? formatNumber(b.qtyOnHand, lang) : '—'}</span>,
        sortValue: (b) => b.qtyOnHand ?? 0,
        exportValue: (b) => b.qtyOnHand ?? 0,
        align: 'end',
      },
      {
        id: 'product',
        header: t('warehouse.locations.columns.product'),
        cell: (b) => {
          const cell = binProductCell(b)
          if (cell.kind === 'none') return <span className="loc-none">—</span>
          if (cell.kind === 'one')
            return (
              <span className="loc-prod-name" title={[cell.sku, cell.name].filter(Boolean).join(' · ')}>
                {cell.name}
              </span>
            )
          return <span>{productText(b)}</span>
        },
        sortValue: (b) => productText(b),
        exportValue: (b) => productText(b) ?? '',
      },
      {
        id: 'capacity',
        header: t('warehouse.locations.columns.capacity'),
        cell: (b) => <span className="mono">{b.maxCapacityQty != null ? formatNumber(b.maxCapacityQty, lang) : '—'}</span>,
        sortValue: (b) => b.maxCapacityQty ?? undefined,
        exportValue: (b) => b.maxCapacityQty ?? '',
        align: 'end',
      },
      {
        id: 'fill',
        header: t('warehouse.locations.columns.occupancy'),
        cell: (b) => {
          const pct = binFillPct(b.qtyOnHand, b.maxCapacityQty)
          return pct == null ? <span className="loc-none">—</span> : <FillBar pct={pct} />
        },
        sortValue: (b) => binFillPct(b.qtyOnHand, b.maxCapacityQty) ?? undefined,
        exportValue: (b) => binFillPct(b.qtyOnHand, b.maxCapacityQty) ?? '',
      },
      {
        id: 'status',
        header: t('warehouse.locations.columns.status'),
        cell: (b) => {
          const s = binOccupancy(b)
          return <Chip tone={OCCUPANCY_TONE[s]}>{t(`warehouse.locations.occupancy.${s}`)}</Chip>
        },
        sortValue: (b) => t(`warehouse.locations.occupancy.${binOccupancy(b)}`),
        exportValue: (b) => t(`warehouse.locations.occupancy.${binOccupancy(b)}`),
      },
    ]
  }, [t, lang, zones])

  const exportRows = () =>
    impossible
      ? Promise.resolve(NO_BINS)
      : fetchAllPages((skip, take) =>
          unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId: warehousePublicId }, query: { ...searchQuery, skip, take } } })),
        )

  if (zonesError || binsQ.error) {
    return (
      <p className="note ferr" role="alert">
        {(zonesError ?? binsQ.error)?.message || t('errors.generic')}
      </p>
    )
  }

  const loading = !impossible && (binsQ.isLoading || binsQ.isPlaceholderData)

  return (
    <>
      <div className="pulse">
        {zonesLoading ? (
          <Spinner block />
        ) : capacities.length > 0 ? (
          <div className="river loc-river" aria-label={t('warehouse.locations.riverAria')} role="group">
            {capacities.map((c, i) => {
              const id = String(c.zone.id)
              return (
                <ZoneNodeWithPipe
                  key={id}
                  first={i === 0}
                  item={c}
                  active={zoneIds.includes(id)}
                  onToggle={() => setZoneIds(toggleZoneSelection(zoneIds, id))}
                />
              )
            })}
          </div>
        ) : (
          <div className="loc-river">
            <EmptyState icon={<IconWarehouse />} title={t('warehouse.locations.noZones')} />
          </div>
        )}
      </div>

      <Filters onClear={clearAll}>
        <TextFilter
          label={t('warehouse.locations.filters.bin')}
          value={code}
          placeholder={t('warehouse.locations.filters.binPlaceholder')}
          onChange={(v) => {
            setCode(v)
            setPage(1)
          }}
        />
        <SearchSelect label={t('warehouse.locations.filters.zone')} options={zoneOptions} value={zoneIds} onChange={setZoneIds} />
        <SearchSelect label={t('warehouse.locations.filters.type')} options={typeOptions} value={zoneTypes} onChange={withPageReset(setZoneTypes)} />
        <ProductMultiFilter label={t('warehouse.locations.filters.product')} value={products} onChange={withPageReset(setProducts)} />
        <SearchSelect label={t('warehouse.locations.filters.status')} options={statusOptions} value={occupancy} onChange={withPageReset(setOccupancy)} />
      </Filters>

      <Panel
        flush
        icon={<IconGrid />}
        title={t('warehouse.locations.columns.bin')}
        badge={!impossible && binsQ.isLoading ? undefined : total}
        actions={
          // Lote F14: un código de barras por posición de lo filtrado (misma consulta que la tabla y Exportar)
          <BinBarcodeReportButton
            className="btn sm"
            warehousePublicId={warehousePublicId}
            warehouse={warehouse}
            zones={zones}
            query={impossible ? null : searchQuery}
          />
        }
      >
        <DataTable
          label={t('warehouse.locations.tableLabel')}
          onRowClick={canManage ? (b) => setEditing(b) : undefined}
          columns={columns}
          rows={rows}
          rowKey={(b) => b.id ?? 0}
          page={page}
          pageSize={pageSize}
          total={total}
          onPage={setPage}
          onPageSize={(size) => {
            setPageSize(size)
            setPage(1)
          }}
          exportRows={exportRows}
          loading={loading}
          empty={<EmptyState icon={<IconGrid />} title={filtered ? t('warehouse.locations.noMatch') : t('warehouse.locations.noBins')} />}
        />
      </Panel>
      <BinModal publicId={warehousePublicId} zones={zones} bin={editing} open={editing !== null} onClose={() => setEditing(null)} />
    </>
  )
}

/** Recuadro con su tubería delante (salvo el primero), como el río de la maqueta. */
function ZoneNodeWithPipe({ first, ...props }: { item: ZoneCapacity; active: boolean; onToggle: () => void; first: boolean }) {
  return (
    <>
      {!first && <div className="pipe" aria-hidden="true" />}
      <ZoneNode {...props} />
    </>
  )
}

export default function LocationsScreen() {
  const t = useT()
  const pickerId = useId()
  const [params, setParams] = useSearchParams()
  const [creatingBin, setCreatingBin] = useState(false)
  const [settingCapacity, setSettingCapacity] = useState(false)

  // Almacén: el de la URL o, sin él, el primero activo (como la maqueta).
  const warehouses = useWarehouses({ includeInactive: false })
  const urlWarehouse = params.get('warehouse')
  const firstWarehouse = warehouses.data?.[0]?.publicId ?? null
  const warehousePublicId = urlWarehouse || firstWarehouse
  // al cambiar de almacén se descartan los filtros (sus zonas y productos ya no aplican), como en la maqueta
  const selectWarehouse = (publicId: string | null) => {
    const next = new URLSearchParams(params)
    next.delete('zone')
    if (publicId) next.set('warehouse', publicId)
    else next.delete('warehouse')
    setParams(next, { replace: true })
  }

  const zonesQ = useWarehouseZones(warehousePublicId, { includeInactive: false })
  const zones = zonesQ.data ?? NO_ZONES

  let body
  if (warehouses.isLoading) body = <Spinner block />
  else if (!warehousePublicId) body = <EmptyState icon={<IconWarehouse />} title={t('warehouse.locations.noWarehouses')} />
  else
    body = (
      <LocationsBody
        key={warehousePublicId}
        warehousePublicId={warehousePublicId}
        warehouse={warehouses.data?.find((w) => w.publicId === warehousePublicId) ?? {}}
        zones={zones}
        zonesLoading={zonesQ.isLoading}
        zonesError={zonesQ.error}
      />
    )

  return (
    <div className="wrap locations">
      <div className="head">
        <div>
          <h1>{t('warehouse.locations.title')}</h1>
          <p>{t('warehouse.locations.subtitle')}</p>
        </div>
        <div className="act">
          <div className="loc-wh">
            <label htmlFor={pickerId} className="sr-only">
              {t('warehouse.locations.warehouse')}
            </label>
            <WarehousePicker
              id={pickerId}
              value={warehousePublicId}
              onChange={(id) => selectWarehouse(id)}
              placeholder={null}
              filterLabel={t('warehouse.locations.warehouse')}
            />
          </div>
          {warehousePublicId && (
            <Can perm="warehouse.manage">
              <button type="button" className="btn" onClick={() => setSettingCapacity(true)}>
                {t('warehouse.binCapacity.open')}
              </button>
              <button type="button" className="btn flow" onClick={() => setCreatingBin(true)} disabled={zonesQ.isLoading}>
                {t('warehouse.bins.new')}
              </button>
            </Can>
          )}
        </div>
      </div>
      {body}
      {/* mismo modal que la pestaña Posiciones de la ficha del almacén, sobre el almacén elegido arriba */}
      {warehousePublicId && (
        <BinModal publicId={warehousePublicId} zones={zones} bin={null} open={creatingBin} onClose={() => setCreatingBin(false)} />
      )}
      {/* cupo en bloque sobre el almacén elegido; la zona del filtro (`?zone=`) llega ya puesta */}
      {warehousePublicId && (
        <BinCapacityModal
          publicId={warehousePublicId}
          open={settingCapacity}
          onClose={() => setSettingCapacity(false)}
          initial={{ zoneIds: parseZoneParam(params.getAll('zone')) }}
        />
      )}
    </div>
  )
}
