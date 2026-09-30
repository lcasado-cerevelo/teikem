// Lote F6 — panel 'Almacén' de Pulso: tarjetas calculadas en cliente (saldo en mano y disponible, bajo mínimo, recibos
// abiertos, tareas pendientes por tipo y conteos abiertos). Son saldo actual: sin selector de rango.
// Lote F7A: filtro de almacén (a las seis tarjetas) y de categoría o producto (solo En mano, Disponible y Bajo mínimo).
// Lote F8a: sin cambios de comportamiento; salió de `Pulse.tsx` para ser la entrada WAREHOUSE del registro de paneles
// (`pulsePanels.tsx`). Quién lo ve lo decide el servidor (`pulse.warehouse` + `inventory.view` + WMS_LOTSERIAL).
// Reconciliación con la maqueta (fase 4): el saldo se pinta como río (Recibos abiertos → En almacén → Reservado →
// Disponible) con 'Bajo mínimo' como nodo satélite de alerta; Tareas pendientes y Conteos abiertos siguen en tarjetas.
import { useMemo, type CSSProperties, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import { IconAlert } from '../../kernel/ui/icons'
import { Panel } from '../../kernel/ui/Panel'
import { categoryProductTotals } from '../../kernel/ui/categoryTree'
import { PULSE_TASK_TYPES, useWarehouseFilter, useWarehousePulse } from './api'
import { formatValue } from './format'
import { WarehouseFilter } from './WarehouseFilter'
import { IconBox, IconCheckin, IconLayers, IconLock, IconWarehouse } from '../../kernel/ui/screenIcons'

// Tarjetas del panel 'Almacén' (Tareas pendientes y Conteos abiertos): auto-fit para que las dos ocupen todo el ancho;
// una columna a 360 px (min(100%, …) evita que la columna mínima desborde el panel).
const TILE_GRID: CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 200px), 1fr))', gap: 12, marginTop: 12 }
const TILE: CSSProperties = { border: '1px solid var(--line)', borderRadius: 'var(--radius-sm)', padding: '12px 14px', minWidth: 0 }
const TILE_LABEL: CSSProperties = { fontSize: 12.5, color: 'var(--muted)' }
const TILE_VALUE: CSSProperties = { fontSize: 26, fontWeight: 800, lineHeight: 1.2 }

/** Marca 'almacén': la cifra solo recibe el filtro de almacén (no el de categoría o producto). */
function ScopeTag() {
  const t = useT()
  return (
    <span className="dr" title={t('analytics.pulse.warehouse.scopeTagTitle')}>
      {t('analytics.pulse.warehouse.scopeTag')}
    </span>
  )
}

/** Tarjeta del panel 'Almacén': etiqueta (con la marca 'almacén' si solo aplica ese filtro), número y desglose. */
function WarehouseTile({ label, value, scope, children }: { label: string; value: string; scope?: boolean; children?: ReactNode }) {
  return (
    <div role="group" aria-label={label} style={TILE}>
      <div style={TILE_LABEL}>
        {label}
        {scope && (
          <>
            {' '}
            <ScopeTag />
          </>
        )}
      </div>
      <div style={TILE_VALUE}>{value}</div>
      {children}
    </div>
  )
}

/** Nodo del río de Almacén (mismas clases `.node` que el río de indicadores). `tone`: 'flow' (azul) en el flujo
 *  principal; 'alert' (naranja, como los nodos de alerta de la maqueta) o 'idle' (neutro) en el satélite 'Bajo mínimo'. */
function RiverNode({
  label,
  icon,
  value,
  tone,
  satellite,
  scope,
  sub,
  alertText,
}: {
  label: string
  icon: ReactNode
  value: string
  tone: 'flow' | 'alert' | 'idle'
  satellite?: boolean
  /** Marca 'almacén' (solo aplica el filtro de almacén). */
  scope?: boolean
  sub?: ReactNode
  /** Texto para lectores de pantalla cuando el nodo está en alerta (el tono de color no basta). */
  alertText?: string
}) {
  const cls = ['node', tone === 'flow' ? 'flow' : tone === 'alert' ? 'money' : '', satellite ? 'sat' : ''].filter(Boolean).join(' ')
  return (
    <div role="group" aria-label={label} className={cls}>
      <div className="ph">
        {icon}
        <span>{label}</span>
        {scope && <ScopeTag />}
      </div>
      <div className="big">{value}</div>
      {sub != null && <div className="sub">{sub}</div>}
      {alertText != null && <span className="sr-only">{alertText}</span>}
    </div>
  )
}

/** Número de un nodo o tarjeta de almacén: '…' mientras carga, '—' si la consulta falló (p. ej. 403). */
function tileValue(value: number | null | undefined, loading: boolean): string {
  return loading ? '…' : formatValue(value, false)
}

/** Panel 'Almacén' de Pulso. Lote F7A: filtro de almacén (todas las cifras) y de categoría o producto (solo En almacén,
 *  Reservado, Disponible y Bajo mínimo), recordado por usuario. */
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
  // Reservado = en almacén − disponible (mismo cálculo que antes iba en el subtítulo de 'Disponible').
  const reserved = onHand != null && available != null ? onHand - available : null

  // Productos por categoría contando sus subcategorías: las cifras de saldo y bajo mínimo también las incluyen.
  const categoryTotals = useMemo(() => categoryProductTotals(categories.data ?? []), [categories.data])

  // Subtítulo de 'En almacén': la categoría y su cantidad de productos, o el SKU del producto (con su mínimo si tiene).
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

  // Enlaces a Kárdex de movimientos (pestaña Kárdex, sin `tab`, o Saldos, `tab=balances`) con los mismos filtros que las
  // cifras (almacén incluido), para que el destino cuadre con la tarjeta.
  const inventoryLink = (extra: Record<string, string>) => {
    const q = new URLSearchParams(extra)
    if (filter.warehousePublicId) q.set('warehousePublicIds', filter.warehousePublicId)
    return `/warehouse/kardex?${q.toString()}`
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
      <Link className="ref" to={inventoryLink({ product: item.publicId })}>
        {t('analytics.pulse.warehouse.viewKardex', { sku: product?.sku ?? '' })}
      </Link>
    )
  } else {
    belowValue = tileValue(belowMin.data?.total, belowMin.isLoading)
    belowWarn = (belowMin.data?.total ?? 0) > 0
    if (item?.kind === 'category')
      belowLink = (
        <Link className="ref" to={inventoryLink({ tab: 'balances', categoryIds: String(item.id) })}>
          {t('analytics.pulse.warehouse.viewInventory')}
        </Link>
      )
  }

  return (
    <Panel icon={<IconLayers />} title={t('analytics.pulse.warehouse.title')} subtitle={t('analytics.pulse.warehouse.subtitle')}>
      <WarehouseFilter filter={filter} onChange={setFilter} warehouses={warehouses} categories={categories} />
      {/* Río de mercancía: flujo principal con tuberías; 'Bajo mínimo' queda aparte, tras una bifurcación punteada. */}
      <div className="river" role="group" aria-label={t('analytics.pulse.warehouse.river.label')}>
        <RiverNode
          label={t('analytics.pulse.warehouse.openReceipts')}
          icon={<IconCheckin />}
          value={tileValue(openReceipts.data?.total, openReceipts.isLoading)}
          tone="flow"
          scope
        />
        <div className="pipe" aria-hidden="true" />
        <RiverNode
          label={t('analytics.pulse.warehouse.river.inStock')}
          icon={<IconWarehouse />}
          value={tileValue(onHand, balances.isLoading)}
          tone="flow"
          sub={onHandSub}
        />
        <div className="pipe" aria-hidden="true" />
        <RiverNode
          label={t('analytics.pulse.warehouse.river.reserved')}
          icon={<IconLock />}
          value={tileValue(reserved, balances.isLoading)}
          tone="flow"
        />
        <div className="pipe" aria-hidden="true" />
        <RiverNode label={t('analytics.pulse.warehouse.available')} icon={<IconBox />} value={tileValue(available, balances.isLoading)} tone="flow" />
        <div className="river-fork" aria-hidden="true" />
        <RiverNode
          label={t('analytics.pulse.warehouse.belowMin')}
          icon={<IconAlert />}
          value={belowValue}
          tone={belowWarn ? 'alert' : 'idle'}
          satellite
          sub={belowLink}
          alertText={
            belowWarn
              ? t(item?.kind === 'product' ? 'analytics.pulse.warehouse.river.belowMinAlertProduct' : 'analytics.pulse.warehouse.river.belowMinAlert')
              : undefined
          }
        />
      </div>
      <div style={TILE_GRID}>
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
        <WarehouseTile label={t('analytics.pulse.warehouse.openCounts')} value={tileValue(openCounts.data?.total, openCounts.isLoading)} scope />
      </div>
    </Panel>
  )
}
