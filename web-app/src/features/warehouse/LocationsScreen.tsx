// Ubicaciones (`/warehouse/locations`, maqueta `ubicaciones()`): vista global de las posiciones de un almacén.
// Almacén elegido arriba (se recuerda en la URL `?warehouse=`), río de ocupación por zona (posiciones con existencias /
// posiciones activas), filtros Zona / Tipo / Producto / Estado y tabla Posición, Zona, Cantidad, Producto, Ocupación,
// Estado. Lectura con inventory.view + WMS_LOTSERIAL (por la ruta); "Nueva posición" (warehouse.manage) abre el mismo
// `BinModal` que la ficha del almacén, sobre el almacén elegido. Los datos salen de las zonas y posiciones del
// almacén y de sus saldos (qué productos hay en cada posición); la ficha del almacén conserva su propio resumen.
import { useId, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  DataTable,
  EmptyState,
  Filters,
  IconGrid,
  IconWarehouse,
  Panel,
  QBox,
  SearchSelect,
  Spinner,
  matchesQ,
  type DataColumn,
} from '../../kernel/ui'
import { useWarehouseBins, useWarehouseStockLines, useWarehouseZones, useWarehouses } from './api'
import { BinModal } from './BinModal'
import { formatNumber } from './lineRules'
import {
  BIN_STATES,
  EMPTY_LOCATION_FILTERS,
  buildLocationRows,
  filterLocationRows,
  productOptions,
  productsByBin,
  zoneOccupancy,
  type LocationFilters,
  type LocationRow,
  type ZoneOccupancy,
} from './locations'
import { WarehousePicker } from './pickers'
import '../analytics/pulse.css'
import './warehouse.css'

const PAGE_SIZE = 25
const NO_ROWS: LocationRow[] = []
/** Segmentos de la barra `.spark` de cada nodo de zona (7, como la maqueta). */
const SPARK_SEGMENTS = [0, 1, 2, 3, 4, 5, 6]

/** Nodo del río: una zona con sus posiciones ocupadas / totales y el porcentaje. */
function ZoneNode({ item }: { item: ZoneOccupancy }) {
  const t = useT()
  const { zone, used, total, pct } = item
  return (
    <div className="node flow" role="group" aria-label={t('warehouse.locations.zoneAria', { zone: zone.name ?? zone.code ?? '', used, total, pct })}>
      <div className="ph">
        <IconGrid />
        <span>{zone.name || zone.code}</span>
        <span className="tag loc-tag">{zone.code}</span>
      </div>
      <div className="big">
        {used}
        <span className="loc-of">/{total}</span>
      </div>
      <div className="sub">{t('warehouse.locations.usedPct', { pct })}</div>
      {/* barra de la maqueta: 7 segmentos a la misma altura (= % ocupado); decorativa, el dato ya está en el texto y el aria-label */}
      <div className="spark" aria-hidden="true">
        {SPARK_SEGMENTS.map((i) => (
          <i key={i} style={{ height: `${pct}%` }} />
        ))}
      </div>
    </div>
  )
}

/** Nodo con su tubería delante (salvo el primero), como el río de la maqueta. */
function ZoneNodeWithPipe({ item, first }: { item: ZoneOccupancy; first: boolean }) {
  return (
    <>
      {!first && <div className="pipe" aria-hidden="true" />}
      <ZoneNode item={item} />
    </>
  )
}

/** Barra de ocupación de la fila (relativa a la posición con más unidades: las posiciones no tienen capacidad). */
function FillBar({ fill }: { fill: number }) {
  const t = useT()
  const text = t('warehouse.locations.fillAria', { pct: fill })
  return (
    <div className="loc-bar" role="img" aria-label={text} title={t('warehouse.locations.fillHint')}>
      <i style={{ width: `${fill}%` }} />
    </div>
  )
}

export default function LocationsScreen() {
  const t = useT()
  const lang = useLang()
  const pickerId = useId()
  const [params, setParams] = useSearchParams()
  const [filters, setFilters] = useState<LocationFilters>(EMPTY_LOCATION_FILTERS)
  const [text, setText] = useState('')
  const [creatingBin, setCreatingBin] = useState(false)

  // Almacén: el de la URL o, sin él, el primero activo (como la maqueta).
  const warehouses = useWarehouses({ includeInactive: false })
  const urlWarehouse = params.get('warehouse')
  const firstWarehouse = warehouses.data?.[0]?.publicId ?? null
  const warehousePublicId = urlWarehouse || firstWarehouse
  // al cambiar de almacén se descartan los filtros (sus zonas y productos ya no aplican), como en la maqueta
  const selectWarehouse = (publicId: string | null) => {
    setFilters(EMPTY_LOCATION_FILTERS)
    setText('')
    const next = new URLSearchParams(params)
    if (publicId) next.set('warehouse', publicId)
    else next.delete('warehouse')
    setParams(next, { replace: true })
  }

  const zonesQ = useWarehouseZones(warehousePublicId, { includeInactive: false })
  const binsQ = useWarehouseBins(warehousePublicId, { includeInactive: false })
  const stockQ = useWarehouseStockLines(warehousePublicId)

  const zones = useMemo(() => zonesQ.data ?? [], [zonesQ.data])
  // con keepPreviousData, las posiciones del almacén anterior no se mezclan con las zonas del nuevo
  const bins = useMemo(() => (binsQ.isPlaceholderData ? [] : (binsQ.data ?? [])), [binsQ.data, binsQ.isPlaceholderData])
  const byBin = useMemo(() => productsByBin(stockQ.data?.items ?? []), [stockQ.data])

  const occupancy = useMemo(() => zoneOccupancy(zones, bins), [zones, bins])
  const allRows = useMemo(() => buildLocationRows(bins, zones, byBin), [bins, zones, byBin])
  const rows = useMemo(() => {
    const filtered = filterLocationRows(allRows, filters)
    if (!text.trim()) return filtered
    return filtered.filter((r) => matchesQ(text, r.bin.code, r.bin.zoneCode, r.zoneName, ...r.products.flatMap((p) => [p.sku, p.name])))
  }, [allRows, filters, text])

  const zoneOptions = useMemo(() => zones.map((z) => ({ value: String(z.id), label: z.code ?? '' })), [zones])
  const typeOptions = useMemo(() => {
    const seen = new Map<string, string>()
    for (const z of zones) if (z.zoneTypeCode && !seen.has(z.zoneTypeCode)) seen.set(z.zoneTypeCode, z.zoneType || z.zoneTypeCode)
    return [...seen].map(([value, label]) => ({ value, label }))
  }, [zones])
  const prodOptions = useMemo(() => productOptions(byBin), [byBin])
  const stateOptions = useMemo(() => BIN_STATES.map((s) => ({ value: s, label: t(`warehouse.locations.states.${s}`) })), [t])

  const setFilter = <K extends keyof LocationFilters>(key: K) => (value: string[]) => setFilters((f) => ({ ...f, [key]: value }))

  const columns = useMemo<DataColumn<LocationRow>[]>(
    () => [
      {
        id: 'code',
        header: t('warehouse.locations.columns.bin'),
        cell: (r) => <span className="ref">{r.bin.code}</span>,
        sortValue: (r) => r.bin.code,
        card: 'title',
      },
      { id: 'zone', header: t('warehouse.locations.columns.zone'), cell: (r) => r.zoneName, sortValue: (r) => r.zoneName },
      {
        id: 'qty',
        header: t('warehouse.locations.columns.qty'),
        cell: (r) => <span className="mono">{r.bin.qtyOnHand ? formatNumber(r.bin.qtyOnHand, lang) : '—'}</span>,
        sortValue: (r) => r.bin.qtyOnHand ?? 0,
        align: 'end',
      },
      {
        id: 'product',
        header: t('warehouse.locations.columns.product'),
        cell: (r) => {
          if (r.products.length === 0) return <span className="loc-none">—</span>
          const first = r.products[0]
          return (
            <span className="loc-prod" title={r.products.map((p) => p.name).join(', ')}>
              <span className="loc-prod-name">{first.name}</span>
              {r.products.length > 1 && <span className="tag">+{r.products.length - 1}</span>}
            </span>
          )
        },
        // por el producto que se muestra (el primero); sin productos queda al final
        sortValue: (r) => r.products[0]?.name,
      },
      { id: 'fill', header: t('warehouse.locations.columns.occupancy'), cell: (r) => <FillBar fill={r.fill} />, sortValue: (r) => r.fill },
      {
        id: 'state',
        header: t('warehouse.locations.columns.state'),
        cell: (r) => <Chip tone={r.state === 'EMPTY' ? 'cap' : 'disp'}>{t(`warehouse.locations.states.${r.state}`)}</Chip>,
        sortValue: (r) => t(`warehouse.locations.states.${r.state}`),
      },
    ],
    [t, lang],
  )

  const loadingData = zonesQ.isLoading || binsQ.isLoading || binsQ.isPlaceholderData
  const dataError = zonesQ.error ?? binsQ.error

  let body
  if (warehouses.isLoading) {
    body = <Spinner block />
  } else if (!warehousePublicId) {
    body = <EmptyState icon={<IconWarehouse />} title={t('warehouse.locations.noWarehouses')} />
  } else if (dataError) {
    body = (
      <p className="note ferr" role="alert">
        {dataError.message || t('errors.generic')}
      </p>
    )
  } else {
    body = (
      <>
        <div className="pulse">
          {zonesQ.isLoading ? (
            <Spinner block />
          ) : occupancy.length > 0 ? (
            <div className="river loc-river" aria-label={t('warehouse.locations.riverAria')} role="group">
              {occupancy.map((o, i) => (
                <ZoneNodeWithPipe key={o.zone.id} item={o} first={i === 0} />
              ))}
            </div>
          ) : (
            <div className="loc-river">
              <EmptyState icon={<IconWarehouse />} title={t('warehouse.locations.noZones')} />
            </div>
          )}
        </div>

        <Filters
          onClear={() => {
            setFilters(EMPTY_LOCATION_FILTERS)
            setText('')
          }}
        >
          <SearchSelect label={t('warehouse.locations.filters.zone')} options={zoneOptions} value={filters.zoneIds} onChange={setFilter('zoneIds')} />
          <SearchSelect label={t('warehouse.locations.filters.type')} options={typeOptions} value={filters.zoneTypes} onChange={setFilter('zoneTypes')} />
          <SearchSelect label={t('warehouse.locations.filters.product')} options={prodOptions} value={filters.products} onChange={setFilter('products')} />
          <SearchSelect label={t('warehouse.locations.filters.state')} options={stateOptions} value={filters.states} onChange={setFilter('states')} />
        </Filters>

        {stockQ.data?.truncated && <p className="note">{t('warehouse.locations.truncated', { count: stockQ.data.items.length })}</p>}
        {stockQ.error && <p className="note ferr">{t('warehouse.locations.stockError')}</p>}

        <Panel flush icon={<IconGrid />} title={t('warehouse.locations.columns.bin')} badge={loadingData ? undefined : rows.length}>
          <div className="qrow">
            <QBox value={text} onChange={setText} />
          </div>
          <DataTable
            label={t('warehouse.locations.tableLabel')}
            columns={columns}
            rows={loadingData ? NO_ROWS : rows}
            rowKey={(r) => r.bin.id ?? 0}
            defaultSort={{ id: 'code', desc: false }}
            pageSize={PAGE_SIZE}
            loading={loadingData}
            empty={
              allRows.length === 0 ? (
                <EmptyState icon={<IconGrid />} title={t('warehouse.locations.noBins')} />
              ) : undefined
            }
          />
        </Panel>
      </>
    )
  }

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
            <WarehousePicker id={pickerId} value={warehousePublicId} onChange={(id) => selectWarehouse(id)} placeholder={null} />
          </div>
          {warehousePublicId && (
            <Can perm="warehouse.manage">
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
    </div>
  )
}
