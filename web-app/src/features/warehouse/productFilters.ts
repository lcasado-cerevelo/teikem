// Lote 12 — filtros de 'Productos e inventario' (lógica pura). Un solo estado (`ProductFilterState`) alimenta la tabla
// (`productListQuery` → GET /products), los KPIs clicables (`?kpi=` en la URL) y los dos reportes PDF: el de inventario
// usa la misma consulta de la tabla; el de ajustes la traslada al Kárdex (`adjustmentsKardexQuery`, tipo ADJUSTMENT).
// `describeProductFilters` arma el bloque "Filtros aplicados" del PDF con nombres, no ids. El KPI viaja al Reporte de
// inventario por `productListQuery` (mismo filtro que la tabla); al de ajustes no (solo un aviso).
import type { GetQuery } from './api'
import type { ProductFilterItem } from './pickers'

/** KPI elegido en el río (filtra la tabla): SKU activos, Unidades totales, Bajo mínimo, Con número de serie. */
export type ProductKpi = 'active' | 'available' | 'low' | 'serial'
export const PRODUCT_KPIS: readonly ProductKpi[] = ['active', 'available', 'low', 'serial']

/** InternalCode del tipo de movimiento 'Ajuste' (catálogo InventoryTxnType, `InventoryTxnTypes.Adjustment` del dominio). */
export const ADJUSTMENT_TXN_TYPE = 'ADJUSTMENT'

export interface ProductFilterState {
  /** publicId de los almacenes (acotan las cantidades de cada fila; no quitan productos). */
  warehouses: readonly string[]
  /** Productos elegidos en el filtro SKU. */
  products: readonly ProductFilterItem[]
  /** Nombre contiene (ya sin espacios sobrantes; vacío = sin filtro). */
  name: string
  /** ids de categoría como texto (el API incluye las subcategorías). */
  categoryIds: readonly string[]
  brands: readonly string[]
  kpi: ProductKpi | null
}

export const EMPTY_PRODUCT_FILTERS: ProductFilterState = { warehouses: [], products: [], name: '', categoryIds: [], brands: [], kpi: null }

/** `?kpi=` de la URL → KPI (valor desconocido = ninguno). */
export function parseKpiParam(value: string | null | undefined): ProductKpi | null {
  return PRODUCT_KPIS.includes(value as ProductKpi) ? (value as ProductKpi) : null
}

/** Clic en un KPI: lo elige, o lo quita si ya era el activo. */
export function toggleKpi(current: ProductKpi | null, clicked: ProductKpi): ProductKpi | null {
  return current === clicked ? null : clicked
}

/** Parámetros del API que aplica cada KPI. */
export function kpiQuery(kpi: ProductKpi | null): GetQuery<'/api/v1/products'> {
  switch (kpi) {
    case 'active':
      return { activeOnly: true }
    // Unidades totales (decisión del 2026-09-30): activos con existencia EN MANO > 0 (Σ QtyOnHand de todas sus posiciones,
    // sin restar lo reservado ni excluir cuarentena), no "con disponible". La cifra del KPI sale de /inventory/balances con
    // activeProductsOnly: suma solo los productos activos, igual que la tabla.
    case 'available':
      return { activeOnly: true, onlyOnHand: true }
    case 'low':
      return { belowMin: true }
    // igual que su cifra (activos con rastreo SERIAL o series): la tabla y el número coinciden
    case 'serial':
      return { activeOnly: true, serialOnly: true }
    default:
      return {}
  }
}

const nonEmpty = <T>(list: readonly T[]): T[] | undefined => (list.length > 0 ? [...list] : undefined)

/** Consulta de la lista de productos (sin `skip`/`take`): la tabla, su Exportar y el Reporte de inventario. */
export function productListQuery(f: ProductFilterState): GetQuery<'/api/v1/products'> {
  const name = f.name.trim()
  return {
    warehousePublicIds: nonEmpty(f.warehouses),
    productPublicIds: nonEmpty(f.products.map((p) => p.publicId)),
    name: name || undefined,
    categoryIds: nonEmpty(f.categoryIds.map(Number)),
    brands: nonEmpty(f.brands),
    ...kpiQuery(f.kpi),
  }
}

/**
 * Los mismos filtros trasladados al Kárdex para el Reporte de ajustes: tipo ADJUSTMENT + almacenes, productos, categorías,
 * marcas y nombre. El KPI no tiene equivalente en movimientos (activo, disponible, bajo mínimo y serie son del producto
 * hoy, no del movimiento): el reporte lo dice en un aviso (`kpiAppliesToAdjustments`).
 */
export function adjustmentsKardexQuery(f: ProductFilterState): GetQuery<'/api/v1/inventory/transactions'> {
  const name = f.name.trim()
  return {
    types: [ADJUSTMENT_TXN_TYPE],
    warehousePublicIds: nonEmpty(f.warehouses),
    productPublicIds: nonEmpty(f.products.map((p) => p.publicId)),
    categoryIds: nonEmpty(f.categoryIds.map(Number)),
    brands: nonEmpty(f.brands),
    name: name || undefined,
  }
}

/** true si hay algún filtro (incluido el KPI). */
export function hasProductFilters(f: ProductFilterState): boolean {
  return f.warehouses.length + f.products.length + f.categoryIds.length + f.brands.length > 0 || f.name.trim() !== '' || f.kpi !== null
}

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Nombres para el bloque de filtros: almacén (publicId → "Código · Nombre") y categoría (id → ruta o nombre). */
export interface ProductFilterNames {
  warehouses: ReadonlyMap<string, string>
  categories: ReadonlyMap<string, string>
}

/**
 * "Filtros aplicados" legibles para un reporte (en el orden de la pantalla): Almacén, SKU, Nombre, Categoría, Marca y
 * la vista del KPI. `report = 'adjustments'` agrega el tipo de movimiento y omite el KPI (no aplica a movimientos).
 * `report = 'barcodes'` (Lote F14, códigos de barras): como el de inventario pero el almacén sin la nota de cantidades (el
 * reporte no tiene cantidades y el almacén no quita productos de la lista).
 * Un id sin nombre conocido se muestra tal cual (nunca se inventa una etiqueta).
 */
export function describeProductFilters(
  f: ProductFilterState,
  names: ProductFilterNames,
  t: Translate,
  report: 'inventory' | 'adjustments' | 'barcodes',
): { label: string; value: string }[] {
  const out: { label: string; value: string }[] = []
  const list = (values: string[]) => values.join(', ')
  if (report === 'adjustments') out.push({ label: t('warehouse.products.reports.filters.type'), value: t('warehouse.products.reports.filters.typeAdjustment') })
  if (f.warehouses.length > 0) {
    const value = list(f.warehouses.map((id) => names.warehouses.get(id) ?? id))
    out.push({
      label: t('warehouse.products.filters.warehouse'),
      value: report === 'inventory' ? `${value} ${t('warehouse.products.reports.filters.warehouseScope')}` : value,
    })
  }
  if (f.products.length > 0) out.push({ label: t('warehouse.products.filters.sku'), value: list(f.products.map((p) => p.label || p.sku || p.publicId)) })
  if (f.name.trim()) out.push({ label: t('warehouse.products.filters.name'), value: t('warehouse.products.reports.filters.contains', { text: f.name.trim() }) })
  if (f.categoryIds.length > 0) {
    out.push({
      label: t('warehouse.products.filters.category'),
      value: `${list(f.categoryIds.map((id) => names.categories.get(id) ?? id))} ${t('warehouse.products.reports.filters.withSubcategories')}`,
    })
  }
  if (f.brands.length > 0) out.push({ label: t('warehouse.products.filters.brand'), value: list([...f.brands]) })
  if (f.kpi && report !== 'adjustments') out.push({ label: t('warehouse.products.reports.filters.view'), value: t(`warehouse.products.kpis.view.${f.kpi}`) })
  return out
}
