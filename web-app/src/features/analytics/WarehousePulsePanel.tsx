// Lote F6 — panel 'Almacén' de Pulso: tarjetas calculadas en cliente (saldo en mano y disponible, bajo mínimo, recibos
// abiertos, tareas pendientes por tipo y conteos abiertos). Son saldo actual: sin selector de rango.
// Lote F7A: filtro de almacén (a las seis tarjetas) y de categoría o producto (solo En mano, Disponible y Bajo mínimo).
// Lote F8a: sin cambios de comportamiento; salió de `Pulse.tsx` para ser la entrada WAREHOUSE del registro de paneles
// (`pulsePanels.tsx`). Quién lo ve lo decide el servidor (`pulse.warehouse` + `inventory.view` + WMS_LOTSERIAL).
import { useMemo, type CSSProperties, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import { Panel } from '../../kernel/ui/Panel'
import { categoryProductTotals } from '../../kernel/ui/categoryTree'
import { PULSE_TASK_TYPES, useWarehouseFilter, useWarehousePulse } from './api'
import { formatValue } from './format'
import { WarehouseFilter } from './WarehouseFilter'

// Tarjetas del panel 'Almacén': una columna a 360 px (min(100%, …) evita que la columna mínima desborde el panel).
const TILE_GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 200px), 1fr))', gap: 12 }
// Borde en propiedades separadas: TILE_WARN cambia solo el color y React avisa si se mezcla con el atajo `border`.
const TILE: CSSProperties = { borderWidth: 1, borderStyle: 'solid', borderColor: 'var(--line)', borderRadius: 'var(--radius-sm)', padding: '12px 14px', minWidth: 0 }
const TILE_LABEL: CSSProperties = { fontSize: 12.5, color: 'var(--muted)' }
const TILE_VALUE: CSSProperties = { fontSize: 26, fontWeight: 800, lineHeight: 1.2 }
const TILE_WARN: CSSProperties = { ...TILE, borderColor: 'rgba(230,169,62,.45)' }
const TILE_VALUE_WARN: CSSProperties = { ...TILE_VALUE, color: 'var(--warn)' }
const TILE_SUB: CSSProperties = { fontSize: 12, color: 'var(--muted)', marginTop: 4, minWidth: 0, overflowWrap: 'anywhere' }

/** Tarjeta del panel 'Almacén': etiqueta (con la marca 'almacén' si solo aplica ese filtro), número y subtítulo. */
function WarehouseTile({
  label,
  value,
  scope,
  warn,
  sub,
  children,
}: {
  label: string
  value: string
  /** Marca 'almacén': la tarjeta solo recibe el filtro de almacén (no el de categoría o producto). */
  scope?: boolean
  /** Borde y número en tono de atención (bajo mínimo). */
  warn?: boolean
  sub?: ReactNode
  children?: ReactNode
}) {
  const t = useT()
  return (
    <div role="group" aria-label={label} style={warn ? TILE_WARN : TILE}>
      <div style={TILE_LABEL}>
        {label}
        {scope && (
          <>
            {' '}
            <span className="dr" title={t('analytics.pulse.warehouse.scopeTagTitle')}>
              {t('analytics.pulse.warehouse.scopeTag')}
            </span>
          </>
        )}
      </div>
      <div style={warn ? TILE_VALUE_WARN : TILE_VALUE}>{value}</div>
      {sub != null && <div style={TILE_SUB}>{sub}</div>}
      {children}
    </div>
  )
}

/** Número de una tarjeta de almacén: '…' mientras carga, '—' si la consulta falló (p. ej. 403). */
function tileValue(value: number | null | undefined, loading: boolean): string {
  return loading ? '…' : formatValue(value, false)
}

/** Panel 'Almacén' de Pulso. Lote F7A: filtro de almacén (las seis tarjetas) y de categoría o producto (solo En mano,
 *  Disponible y Bajo mínimo), recordado por usuario. */
export function WarehousePulsePanel() {
  const t = useT()
  const { filter, setFilter, warehouses, categories, category, product, productError } = useWarehouseFilter()
  const item = filter.item
  const { balances, belowMin, productBelowMin, openReceipts, pendingTasks, tasksByType, openCounts } = useWarehousePulse(
    true,
    filter,
    product?.sku,
  )
  const { data: taskTypes = [] } = useLookups('WarehouseTaskType', { includeDisabled: true })
  const typeLabel = (code: string) => taskTypes.find((o) => o.code === code)?.label ?? code

  const onHand = balances.data?.totalOnHand
  const available = balances.data?.totalAvailable

  // Productos por categoría contando sus subcategorías: las cifras de saldo y bajo mínimo también las incluyen.
  const categoryTotals = useMemo(() => categoryProductTotals(categories.data ?? []), [categories.data])

  // Subtítulo de 'En mano': la categoría y su cantidad de productos, o el SKU del producto (con su mínimo si tiene).
  let onHandSub: ReactNode = null
  if (item?.kind === 'category' && category) {
    const count = (category.id != null ? categoryTotals.get(category.id) : undefined) ?? category.productCount ?? 0
    onHandSub =
      count === 1
        ? t('analytics.pulse.warehouse.categorySubOne', { name: category.name ?? '' })
        : t('analytics.pulse.warehouse.categorySub', { name: category.name ?? '', count: formatValue(count, false) })
  } else if (item?.kind === 'product' && product) {
    onHandSub =
      product.minQty != null
        ? t('analytics.pulse.warehouse.productMin', { sku: product.sku ?? '', qty: formatValue(product.minQty, false) })
        : product.sku
  }

  // Enlaces a Inventario con los mismos filtros que las cifras (almacén incluido), para que el destino cuadre con la tarjeta.
  const inventoryLink = (extra: Record<string, string>) => {
    const q = new URLSearchParams(extra)
    if (filter.warehousePublicId) q.set('warehousePublicIds', filter.warehousePublicId)
    return `/warehouse/inventory?${q.toString()}`
  }

  // 'Bajo mínimo': cantidad de productos (todos o de la categoría) o Sí/No del producto elegido.
  let belowValue: string
  let belowWarn: boolean
  let belowLink: ReactNode = null
  if (item?.kind === 'product') {
    if (productBelowMin !== undefined) belowValue = productBelowMin ? t('analytics.pulse.warehouse.yes') : t('analytics.pulse.warehouse.no')
    else belowValue = belowMin.isError || productError ? '—' : '…'
    belowWarn = productBelowMin === true
    belowLink = (
      <Link className="ref" to={inventoryLink({ tab: 'kardex', product: item.publicId })}>
        {t('analytics.pulse.warehouse.viewKardex', { sku: product?.sku ?? '' })}
      </Link>
    )
  } else {
    belowValue = tileValue(belowMin.data?.total, belowMin.isLoading)
    belowWarn = (belowMin.data?.total ?? 0) > 0
    if (item?.kind === 'category')
      belowLink = (
        <Link className="ref" to={inventoryLink({ categoryIds: String(item.id) })}>
          {t('analytics.pulse.warehouse.viewInventory')}
        </Link>
      )
  }

  return (
    <Panel title={t('analytics.pulse.warehouse.title')} subtitle={t('analytics.pulse.warehouse.subtitle')}>
      <WarehouseFilter filter={filter} onChange={setFilter} warehouses={warehouses} categories={categories} />
      <div style={TILE_GRID}>
        <WarehouseTile label={t('analytics.pulse.warehouse.onHand')} value={tileValue(onHand, balances.isLoading)} sub={onHandSub} />
        <WarehouseTile
          label={t('analytics.pulse.warehouse.available')}
          value={tileValue(available, balances.isLoading)}
          sub={
            onHand != null && available != null ? (
              <>
                {t('analytics.pulse.warehouse.reserved')} <b>{formatValue(onHand - available, false)}</b>
              </>
            ) : null
          }
        />
        <WarehouseTile label={t('analytics.pulse.warehouse.belowMin')} value={belowValue} warn={belowWarn} sub={belowLink} />
        <WarehouseTile label={t('analytics.pulse.warehouse.openReceipts')} value={tileValue(openReceipts.data?.total, openReceipts.isLoading)} scope />
        <WarehouseTile label={t('analytics.pulse.warehouse.pendingTasks')} value={tileValue(pendingTasks.data?.total, pendingTasks.isLoading)} scope>
          <ul style={{ listStyle: 'none', margin: '8px 0 0', padding: 0, fontSize: 13 }}>
            {PULSE_TASK_TYPES.map((type, i) => {
              const q = tasksByType[i]
              return (
                <li key={type} style={{ display: 'flex', justifyContent: 'space-between', gap: 8, minWidth: 0 }}>
                  <span style={{ color: 'var(--muted)', overflowWrap: 'anywhere' }}>{typeLabel(type)}</span>
                  <strong>{tileValue(q?.data?.total, q?.isLoading ?? false)}</strong>
                </li>
              )
            })}
          </ul>
        </WarehouseTile>
        <WarehouseTile label={t('analytics.pulse.warehouse.openCounts')} value={tileValue(openCounts.data?.length, openCounts.isLoading)} scope />
      </div>
    </Panel>
  )
}
