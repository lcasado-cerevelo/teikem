// Lote F14 — reportes de códigos de barras para el conteo (PDF en el cliente con `kernel/ui/barcodeReportPdf`): una fila por
// producto o por posición con su código Code 128, para imprimir la hoja y escanear el papel con el lector del almacén.
// - Productos (desde 'Productos e inventario'): EXACTAMENTE lo que filtra la tabla (`productListQuery`, de a 200 hasta
//   10 000, como Exportar y el Reporte de inventario), AGRUPADOS POR CATEGORÍA [ya no: 2026-10-09 salen corridos en el orden del filtro] (título = ruta de la categoría con su
//   cantidad, por orden natural; "Sin categoría" al final) y dentro de cada grupo por SKU en orden natural (los números en
//   su orden numérico). Cada celda: SKU en negrita y la descripción (nombre del producto); debajo el código con el SKU
//   exacto (la app del lector busca el producto por código de barras o por SKU: el código de barras propio del producto
//   NO se usa aquí). Los inactivos salen si la tabla los muestra, con la marca "Inactivo".
// - Posiciones (desde Ubicaciones y desde la pestaña Posiciones de la ficha del almacén): EXACTAMENTE lo que filtra la tabla
//   (`GET /warehouses/{id}/bins` con la consulta de la pantalla), AGRUPADAS POR EL PRIMER NÚMERO del código (la primera
//   secuencia de dígitos: A01-R01-N1-P01 → 01, R1 → 1, B-06 → 06; se compara como número —2 antes que 10— y 01 y 1 son el
//   mismo grupo, que se titula con el texto del primer código del grupo; sin dígitos —GENERAL— van a "Otras posiciones" al
//   final) y dentro de cada grupo por código completo en orden natural. Cada celda: código de la posición en negrita con su
//   zona y almacén en gris; debajo el código con el código exacto de la posición (la app del lector la busca así).
// Diseño del 2026-10-03: rejilla de 3/2/1 columnas (`kernel/ui/barcodeReportPdf`). Los armadores
// (`buildProductBarcodeReport`, `buildBinBarcodeReport`, `groupProductsForBarcodes`, `groupBinsForBarcodes`) son puros.
import { formatNumber } from '../../kernel/format'
import {
  downloadBarcodeReportPdf,
  type BarcodeColumnsOption,
  type BarcodeReportGroup,
  type BarcodeReportRow,
  type BarcodeReportSpec,
} from '../../kernel/ui/barcodeReportPdf'
import type { ReportFilter } from '../../kernel/ui/reportPdf'
import { exportProducts, exportWarehouseBins, type ProductListItemDto, type WarehouseBinDto, type WarehouseZoneDto } from './api'
import type { ProductReportContext } from './inventoryReports'
import type { BinListQuery } from './locations'
import { describeProductFilters, productListQuery } from './productFilters'

type Translate = (key: string, params?: Record<string, string | number>) => string

const B = 'warehouse.barcodes'

/** Orden natural (números por su valor, sin distinguir mayúsculas ni acentos); empate exacto = orden de código. */
export function naturalCompare(lang: string): (a: string, b: string) => number {
  const collator = new Intl.Collator(lang, { numeric: true, sensitivity: 'base' })
  return (a, b) => collator.compare(a, b) || (a < b ? -1 : a > b ? 1 : 0)
}

// ---------------------------------------------------------------------------------------------------------------------
// Productos
// ---------------------------------------------------------------------------------------------------------------------

/** Fila del reporte de un producto: el código lleva el SKU exacto. */
export function productBarcodeRow(p: Pick<ProductListItemDto, 'sku' | 'name' | 'isActive'>, t: Translate): BarcodeReportRow {
  return {
    value: p.sku ?? '',
    title: p.sku ?? '',
    description: p.name ?? '',
    meta: p.isActive === false ? t(`${B}.inactive`) : null,
  }
}

export interface ProductBarcodeGroup {
  /** Ruta de la categoría (o su nombre); null = sin categoría. */
  category: string | null
  items: ProductListItemDto[]
}

/**
 * Productos agrupados por categoría (por id; título = ruta de `categoryPaths` o, si no está, el nombre del DTO), grupos en
 * orden natural con "Sin categoría" al final y cada grupo por SKU en orden natural.
 */
export function groupProductsForBarcodes(items: readonly ProductListItemDto[], categoryPaths: ReadonlyMap<string, string>, lang: string): ProductBarcodeGroup[] {
  const cmp = naturalCompare(lang)
  const byKey = new Map<string, ProductBarcodeGroup>()
  for (const p of items) {
    const key = p.categoryId != null ? String(p.categoryId) : ''
    const name = key ? categoryPaths.get(key) || p.categoryName?.trim() || key : null
    const group = byKey.get(key)
    if (group) group.items.push(p)
    else byKey.set(key, { category: name, items: [p] })
  }
  const groups = [...byKey.values()].sort((a, b) => (a.category === null ? 1 : b.category === null ? -1 : cmp(a.category, b.category)))
  for (const g of groups) g.items.sort((a, b) => cmp(a.sku ?? '', b.sku ?? ''))
  return groups
}

/** Especificación del reporte de códigos de productos (puro). `truncated` = la lista se cortó en el tope de lectura. */
export function buildProductBarcodeReport(
  items: readonly ProductListItemDto[],
  truncated: boolean,
  ctx: ProductReportContext,
  columns: BarcodeColumnsOption = 'auto',
): BarcodeReportSpec {
  const { t, lang } = ctx
  // pedido del dueño (2026-10-09): sin agrupar por categoría; los códigos salen corridos en el MISMO orden en que el filtro devuelve la lista
  const groups: BarcodeReportGroup[] = [{ title: '', rows: items.map((p) => productBarcodeRow(p, t)) }]
  return {
    title: t(`${B}.productsTitle`),
    subtitle: t(`${B}.productsSubtitle`),
    company: ctx.company,
    user: ctx.user,
    generatedAt: ctx.generatedAt,
    locale: lang,
    filters: describeProductFilters(ctx.filters, ctx.names, t, 'barcodes'),
    groups,
    columns,
    notices: truncated ? [t(`${B}.truncatedProducts`, { count: formatNumber(items.length) })] : [],
    emptyText: t(`${B}.emptyProducts`),
  }
}

/** Lee todos los productos que filtra la tabla y descarga el PDF de códigos. */
export async function generateProductBarcodeReport(
  ctx: ProductReportContext,
  columns: BarcodeColumnsOption = 'auto',
  /** Orden de la tabla (clic en un encabezado) aplicado a todo lo leído; sin él, el orden del filtro. */
  sortItems?: (items: readonly ProductListItemDto[]) => readonly ProductListItemDto[],
): Promise<void> {
  const read = await exportProducts(productListQuery(ctx.filters))
  const items = sortItems ? sortItems(read.items) : read.items
  const truncated = read.truncated
  await downloadBarcodeReportPdf(buildProductBarcodeReport(items, truncated, { ...ctx, generatedAt: ctx.generatedAt ?? new Date() }, columns))
}

// ---------------------------------------------------------------------------------------------------------------------
// Posiciones
// ---------------------------------------------------------------------------------------------------------------------

export interface BinBarcodeContext {
  t: Translate
  lang: string
  company?: string | null
  user?: string | null
  generatedAt?: Date
  /** "Filtros aplicados" ya legibles (los de la barra de la pantalla, más el almacén si la barra no lo tiene). */
  filters: readonly ReportFilter[]
  /** Almacén de las posiciones (código y nombre). */
  warehouse: { code?: string | null; name?: string | null }
  /** Zonas del almacén (para el nombre de la zona de cada posición). */
  zones: readonly Pick<WarehouseZoneDto, 'id' | 'code' | 'name'>[]
}

/** Fila del reporte de una posición: el código lleva el código exacto de la posición; zona y almacén en gris. */
export function binBarcodeRow(b: WarehouseBinDto, ctx: Pick<BinBarcodeContext, 't' | 'warehouse' | 'zones'>): BarcodeReportRow {
  const { t } = ctx
  const zone = ctx.zones.find((z) => z.id === b.zoneId)
  const zoneCode = zone?.code || b.zoneCode || ''
  const zoneName = zone?.name && zone.name !== zoneCode ? zone.name : ''
  const zoneText = zoneCode ? t(`${B}.zone`, { zone: zoneName ? `${zoneCode} (${zoneName})` : zoneCode }) : ''
  const whText = ctx.warehouse.code ? t(`${B}.warehouse`, { warehouse: ctx.warehouse.code }) : ''
  const tags = [b.isProvisional ? t('warehouse.bins.provisional') : '', b.isActive === false ? t(`${B}.inactiveBin`) : '']
  return {
    value: b.code ?? '',
    title: b.code ?? '',
    meta: [zoneText, whText, ...tags].filter(Boolean).join(' · '),
  }
}

/**
 * Primer número del código de una posición: la primera secuencia de dígitos (`text`, tal cual) y su clave numérica sin
 * ceros a la izquierda (`key`: '01' y '1' → '1'). null = el código no tiene dígitos.
 */
export function binFirstNumber(code: string): { key: string; text: string } | null {
  const m = /\d+/.exec(code)
  if (!m) return null
  return { key: m[0].replace(/^0+(?=\d)/, ''), text: m[0] }
}

/** Compara dos claves numéricas sin límite de largo (sin ceros a la izquierda): por largo y luego por dígitos. */
function compareNumericKeys(a: string, b: string): number {
  return a.length - b.length || (a < b ? -1 : a > b ? 1 : 0)
}

export interface BinBarcodeGroup {
  /** Texto del primer número tal cual en el primer código del grupo ('01'); null = "Otras posiciones" (sin dígitos). */
  label: string | null
  bins: WarehouseBinDto[]
}

/**
 * Posiciones agrupadas por el primer número del código (como número: 2 antes que 10; 01 y 1 juntos), cada grupo por
 * código en orden natural y titulado con el texto del primer código; las sin dígitos, en un grupo final.
 */
export function groupBinsForBarcodes(bins: readonly WarehouseBinDto[], lang: string): BinBarcodeGroup[] {
  const cmp = naturalCompare(lang)
  const byKey = new Map<string, WarehouseBinDto[]>()
  const others: WarehouseBinDto[] = []
  for (const b of bins) {
    const n = binFirstNumber(b.code ?? '')
    if (!n) {
      others.push(b)
      continue
    }
    const list = byKey.get(n.key)
    if (list) list.push(b)
    else byKey.set(n.key, [b])
  }
  const groups: BinBarcodeGroup[] = [...byKey.entries()]
    .sort(([a], [b]) => compareNumericKeys(a, b))
    .map(([, list]) => {
      const sorted = [...list].sort((a, b) => cmp(a.code ?? '', b.code ?? ''))
      return { label: binFirstNumber(sorted[0].code ?? '')?.text ?? '', bins: sorted }
    })
  if (others.length > 0) groups.push({ label: null, bins: [...others].sort((a, b) => cmp(a.code ?? '', b.code ?? '')) })
  return groups
}

/** Especificación del reporte de códigos de posiciones (puro). */
export function buildBinBarcodeReport(
  bins: readonly WarehouseBinDto[],
  truncated: boolean,
  ctx: BinBarcodeContext,
  columns: BarcodeColumnsOption = 'auto',
): BarcodeReportSpec {
  const { t } = ctx
  const groups: BarcodeReportGroup[] = groupBinsForBarcodes(bins, ctx.lang).map((g) => {
    const count = formatNumber(g.bins.length)
    return {
      title: g.label === null ? t(`${B}.otherBins`, { count }) : t(`${B}.binGroup`, { group: g.label, count }),
      rows: g.bins.map((b) => binBarcodeRow(b, ctx)),
    }
  })
  return {
    title: t(`${B}.binsTitle`),
    subtitle: t(`${B}.binsSubtitle`),
    company: ctx.company,
    user: ctx.user,
    generatedAt: ctx.generatedAt,
    locale: ctx.lang,
    filters: ctx.filters,
    groups,
    columns,
    notices: truncated ? [t(`${B}.truncatedBins`, { count: formatNumber(bins.length) })] : [],
    emptyText: t(`${B}.emptyBins`),
  }
}

/**
 * Lee todas las posiciones que filtra la tabla (`query` sin `skip`/`take`; null = la combinación de filtros no deja
 * ninguna zona y la tabla está vacía) y descarga el PDF de códigos.
 */
export async function generateBinBarcodeReport(
  warehousePublicId: string,
  query: BinListQuery | null,
  ctx: BinBarcodeContext,
  columns: BarcodeColumnsOption = 'auto',
): Promise<void> {
  const { items, truncated } = query ? await exportWarehouseBins(warehousePublicId, query) : { items: [], truncated: false }
  await downloadBarcodeReportPdf(buildBinBarcodeReport(items, truncated, { ...ctx, generatedAt: ctx.generatedAt ?? new Date() }, columns))
}
