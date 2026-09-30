// Lote 12 — Reporte de inventario y Reporte de ajustes (PDF en el cliente con `kernel/ui/reportPdf`), con los filtros de
// 'Productos e inventario' (`ProductFilterState`). Se reusan tal cual desde otra pantalla (el botón de Reporte de ajustes
// vuelve en la pantalla de Ajustes del Lote 4): `generateInventoryReport(ctx)` / `generateAdjustmentsReport(ctx)`.
// - Inventario: "inventario al momento" = todos los productos de GET /products con la consulta de la tabla (de a 200 hasta
//   10 000, `exportProducts`), agrupados por categoría con subtotal de unidades y valor, y total general. Valor = Total ×
//   costo de compra del producto (el sistema no guarda costo promedio ni por lote: se dice en el reporte); sin costo = '—'.
// - Ajustes: todos los movimientos ADJUSTMENT del Kárdex con los mismos filtros trasladados (`adjustmentsKardexQuery`),
//   del más reciente al más antiguo, con entradas, salidas y neto.
// Los armadores (`buildInventoryReport`, `buildAdjustmentsReport`) son puros y se prueban sin DOM ni API.
import { parseApiDate } from '../../kernel/api/dates'
import { numberLocale } from '../../kernel/i18n'
import { downloadReportPdf, type ReportSpec, type ReportValue } from '../../kernel/ui/reportPdf'
import { exportInventoryTransactions, exportProducts, type GetQuery, type KardexRowDto, type ProductListItemDto } from './api'
import {
  ADJUSTMENT_TXN_TYPE,
  adjustmentsKardexQuery,
  describeProductFilters,
  productListQuery,
  type ProductFilterNames,
  type ProductFilterState,
} from './productFilters'

type Translate = (key: string, params?: Record<string, string | number>) => string

export interface ProductReportContext {
  t: Translate
  /** Idioma de números y fechas. */
  lang: string
  /** Compañía activa (`me.tenantName`). */
  company?: string | null
  /** Usuario que genera (`me.fullName` o su correo). */
  user?: string | null
  generatedAt?: Date
  filters: ProductFilterState
  names: ProductFilterNames
  /**
   * Lote 14 (hallazgo 22) — Reporte de ajustes con los filtros de otra pantalla (Transferencias y ajustes): la consulta del
   * Kárdex ya armada (fechas, dueño, dirección, motivo…) y sus "Filtros aplicados" ya traducidos. Con ella se ignoran
   * `filters`/`names` (y el aviso del KPI) en ese reporte.
   */
  adjustments?: AdjustmentsReportQuery
}

/** Consulta explícita del Reporte de ajustes: la del Kárdex (se fuerza `types=[ADJUSTMENT]`) y los filtros legibles. */
export interface AdjustmentsReportQuery {
  kardexQuery: GetQuery<'/api/v1/inventory/transactions'>
  filterLabels: { label: string; value: string }[]
}

const R = 'warehouse.products.reports'
/** Motivo de ajuste de sistema del saldo inicial de la migración (AdjustmentReasons.OpeningBalance). */
export const OPENING_BALANCE_REASON = 'OPENING_BALANCE'

/** Cifras del resumen y los avisos: con separador de miles siempre, como las columnas del reporte. */
function formatNumber(n: number, lang: string, opts: Intl.NumberFormatOptions = { maximumFractionDigits: 3 }): string {
  return new Intl.NumberFormat(numberLocale(lang), { useGrouping: 'always', ...opts }).format(n)
}

// ---------------------------------------------------------------------------------------------------------------------
// Reporte de inventario
// ---------------------------------------------------------------------------------------------------------------------

/** Valor de una fila (Total × costo de compra) o null si el producto no tiene costo. */
export function inventoryValue(p: Pick<ProductListItemDto, 'qtyOnHand' | 'purchaseCost'>): number | null {
  if (p.purchaseCost == null) return null
  return (p.qtyOnHand ?? 0) * p.purchaseCost
}

export interface InventoryGroup {
  category: string
  items: ProductListItemDto[]
  available: number
  reserved: number
  onHand: number
  /** Suma de los valores con costo; null si ningún producto del grupo tiene costo. */
  value: number | null
}

/** Productos agrupados por categoría (sin categoría al final), cada grupo ordenado por SKU, con sus sumas. */
export function groupInventoryByCategory(items: readonly ProductListItemDto[], noCategory: string, lang: string): InventoryGroup[] {
  const byName = new Map<string, ProductListItemDto[]>()
  for (const p of items) {
    const key = p.categoryName?.trim() || ''
    const list = byName.get(key)
    if (list) list.push(p)
    else byName.set(key, [p])
  }
  const collator = new Intl.Collator(lang, { numeric: true, sensitivity: 'base' })
  const keys = [...byName.keys()].sort((a, b) => (a === '' ? 1 : b === '' ? -1 : collator.compare(a, b)))
  return keys.map((key) => {
    const list = [...byName.get(key)!].sort((a, b) => collator.compare(a.sku ?? '', b.sku ?? ''))
    let value: number | null = null
    for (const p of list) {
      const v = inventoryValue(p)
      if (v !== null) value = (value ?? 0) + v
    }
    return {
      category: key || noCategory,
      items: list,
      available: list.reduce((acc, p) => acc + (p.qtyAvailable ?? 0), 0),
      reserved: list.reduce((acc, p) => acc + (p.qtyReserved ?? 0), 0),
      onHand: list.reduce((acc, p) => acc + (p.qtyOnHand ?? 0), 0),
      value,
    }
  })
}

/** Especificación del Reporte de inventario (puro). `truncated` = la lista se cortó en el tope de lectura. */
export function buildInventoryReport(allItems: readonly ProductListItemDto[], truncated: boolean, ctx: ProductReportContext): ReportSpec {
  const { t, lang } = ctx
  // inventario al momento = lo que hay: los productos sin existencia (en mano 0) no se listan, solo se cuentan en un aviso
  const items = allItems.filter((p) => (p.qtyOnHand ?? 0) !== 0)
  const withoutStock = allItems.length - items.length
  const dash = '—'
  const groups = groupInventoryByCategory(items, t(`${R}.noCategory`), lang)
  const totals = groups.reduce(
    (acc, g) => ({
      available: acc.available + g.available,
      reserved: acc.reserved + g.reserved,
      onHand: acc.onHand + g.onHand,
      value: g.value === null ? acc.value : (acc.value ?? 0) + g.value,
    }),
    { available: 0, reserved: 0, onHand: 0, value: null as number | null },
  )
  const withoutCost = items.filter((p) => p.purchaseCost == null && (p.qtyOnHand ?? 0) !== 0).length

  const notices: string[] = []
  if (truncated) notices.push(t(`${R}.truncated`, { count: formatNumber(allItems.length, lang) }))
  if (withoutStock > 0) notices.push(t(`${R}.withoutStock`, { count: formatNumber(withoutStock, lang) }))
  if (withoutCost > 0) notices.push(t(`${R}.withoutCost`, { count: formatNumber(withoutCost, lang) }))
  notices.push(t(`${R}.valuationNote`))

  const money = (v: number | null): ReportValue => (v === null ? dash : v)
  return {
    title: t(`${R}.inventoryTitle`),
    subtitle: t(`${R}.inventorySubtitle`),
    company: ctx.company,
    user: ctx.user,
    generatedAt: ctx.generatedAt,
    locale: lang,
    filters: describeProductFilters(ctx.filters, ctx.names, t, 'inventory'),
    columns: [
      { header: t(`${R}.columns.sku`) },
      { header: t(`${R}.columns.product`) },
      { header: t(`${R}.columns.category`) },
      { header: t(`${R}.columns.brand`) },
      { header: t(`${R}.columns.available`), format: 'quantity' },
      { header: t(`${R}.columns.reserved`), format: 'quantity' },
      { header: t(`${R}.columns.total`), format: 'quantity' },
      { header: t(`${R}.columns.unitCost`), format: 'unitCost' },
      { header: t(`${R}.columns.value`), format: 'money' },
    ],
    sections: groups.map((g) => ({
      title: `${g.category} (${formatNumber(g.items.length, lang)})`,
      rows: g.items.map((p) => [
        p.sku ?? '',
        p.name ?? '',
        p.categoryName ?? '',
        p.brand ?? '',
        p.qtyAvailable ?? 0,
        p.qtyReserved ?? 0,
        p.qtyOnHand ?? 0,
        p.purchaseCost ?? dash,
        money(inventoryValue(p)),
      ]),
      subtotal: [t(`${R}.subtotal`, { name: g.category }), null, null, null, g.available, g.reserved, g.onHand, '', money(g.value)],
    })),
    totals: items.length > 0 ? [[t(`${R}.grandTotal`), null, null, null, totals.available, totals.reserved, totals.onHand, '', money(totals.value)]] : [],
    summary: [
      { label: t(`${R}.summary.products`), value: formatNumber(items.length, lang) },
      { label: t(`${R}.summary.onHand`), value: formatNumber(totals.onHand, lang) },
      { label: t(`${R}.summary.available`), value: formatNumber(totals.available, lang) },
      {
        label: t(`${R}.summary.value`),
        value: totals.value === null ? dash : formatNumber(totals.value, lang, { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
        tone: 'money',
      },
    ],
    notices,
    emptyText: t(`${R}.emptyInventory`),
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Reporte de ajustes
// ---------------------------------------------------------------------------------------------------------------------

/** "Almacén / Posición" de un ajuste: el destino si entra, el origen si sale (un ajuste tiene un solo lado). */
export function adjustmentLocation(r: KardexRowDto): string {
  const incoming = (r.signedQuantity ?? r.quantity ?? 0) > 0
  const wh = incoming ? (r.toWarehouseCode ?? r.fromWarehouseCode) : (r.fromWarehouseCode ?? r.toWarehouseCode)
  const bin = incoming ? (r.toBinCode ?? r.fromBinCode) : (r.fromBinCode ?? r.toBinCode)
  const text = [wh, bin].filter(Boolean).join(' / ')
  return text || r.position || ''
}

/** Entradas (suma de lo positivo), salidas (suma de lo negativo, en positivo) y neto. */
export function adjustmentTotals(rows: readonly KardexRowDto[]): { inQty: number; outQty: number; net: number } {
  let inQty = 0
  let outQty = 0
  for (const r of rows) {
    const q = r.signedQuantity ?? 0
    if (q > 0) inQty += q
    else outQty -= q
  }
  return { inQty, outQty, net: inQty - outQty }
}

/** Especificación del Reporte de ajustes (puro). */
export function buildAdjustmentsReport(allRows: readonly KardexRowDto[], truncated: boolean, ctx: ProductReportContext): ReportSpec {
  const { t, lang } = ctx
  // los saldos iniciales de la migración se registran como ajustes, pero no son ajustes de la operación: se excluyen
  const rows = allRows.filter((r) => r.reasonCode !== OPENING_BALANCE_REASON)
  const openingBalances = allRows.length - rows.length
  const dateFmt = new Intl.DateTimeFormat(lang, { dateStyle: 'short', timeStyle: 'short' })
  const when = (iso: string | undefined) => {
    if (!iso) return ''
    const d = parseApiDate(iso)
    return Number.isNaN(d.getTime()) ? '' : dateFmt.format(d)
  }
  const { inQty, outQty, net } = adjustmentTotals(rows)
  const signed = (n: number) => formatNumber(n, lang, { maximumFractionDigits: 3, signDisplay: 'exceptZero' })

  const notices: string[] = []
  if (truncated) notices.push(t(`${R}.truncatedAdjustments`, { count: formatNumber(allRows.length, lang) }))
  if (openingBalances > 0) notices.push(t(`${R}.openingBalancesExcluded`, { count: formatNumber(openingBalances, lang) }))
  if (ctx.filters.kpi && !ctx.adjustments) notices.push(t(`${R}.kpiNotApplied`, { view: t(`warehouse.products.kpis.view.${ctx.filters.kpi}`) }))

  const productCell = (r: KardexRowDto) => {
    const extra = [
      r.lotNumber ? t(`${R}.lot`, { lot: r.lotNumber }) : '',
      r.serialNumber ? t(`${R}.serial`, { serial: r.serialNumber }) : '',
    ].filter(Boolean)
    return [r.productName ?? '', ...extra].join('\n')
  }

  return {
    title: t(`${R}.adjustmentsTitle`),
    subtitle: t(`${R}.adjustmentsSubtitle`),
    company: ctx.company,
    user: ctx.user,
    generatedAt: ctx.generatedAt,
    locale: lang,
    filters: ctx.adjustments ? ctx.adjustments.filterLabels : describeProductFilters(ctx.filters, ctx.names, t, 'adjustments'),
    columns: [
      { header: t(`${R}.columns.date`), noWrap: true },
      { header: t(`${R}.columns.sku`) },
      { header: t(`${R}.columns.product`) },
      { header: t(`${R}.columns.location`) },
      { header: t(`${R}.columns.quantity`), format: 'signed' },
      { header: t(`${R}.columns.reason`) },
      { header: t(`${R}.columns.notes`) },
      { header: t(`${R}.columns.user`) },
    ],
    sections: [
      {
        rows: rows.map((r) => [
          when(r.createdAtUtc),
          r.sku ?? '',
          productCell(r),
          adjustmentLocation(r),
          r.signedQuantity ?? 0,
          r.reason || r.reasonCode || '',
          r.notes ?? '',
          r.userName ?? '',
        ]),
      },
    ],
    totals:
      rows.length > 0
        ? [
            [t(`${R}.totalIn`), null, null, null, inQty, '', '', ''],
            [t(`${R}.totalOut`), null, null, null, -outQty, '', '', ''],
            [t(`${R}.totalNet`), null, null, null, net, '', '', ''],
          ]
        : [],
    summary: [
      { label: t(`${R}.summary.movements`), value: formatNumber(rows.length, lang) },
      { label: t(`${R}.summary.in`), value: signed(inQty) },
      { label: t(`${R}.summary.out`), value: signed(-outQty) },
      { label: t(`${R}.summary.net`), value: signed(net), tone: 'money' },
    ],
    notices,
    emptyText: t(`${R}.emptyAdjustments`),
  }
}

/** Consulta del Kárdex del Reporte de ajustes: la explícita (Lote 14, siempre con tipo ADJUSTMENT) o la trasladada de los
 *  filtros de Productos e inventario. */
export function adjustmentsReportKardexQuery(ctx: Pick<ProductReportContext, 'filters' | 'adjustments'>): GetQuery<'/api/v1/inventory/transactions'> {
  if (ctx.adjustments) return { ...ctx.adjustments.kardexQuery, types: [ADJUSTMENT_TXN_TYPE], skip: undefined, take: undefined }
  return adjustmentsKardexQuery(ctx.filters)
}

// ---------------------------------------------------------------------------------------------------------------------
// Generación (lee el API y descarga el PDF)
// ---------------------------------------------------------------------------------------------------------------------

/** Reporte de inventario con los filtros de la tabla: lee todos los productos y descarga el PDF. */
export async function generateInventoryReport(ctx: ProductReportContext): Promise<void> {
  const { items, truncated } = await exportProducts(productListQuery(ctx.filters))
  await downloadReportPdf(buildInventoryReport(items, truncated, { ...ctx, generatedAt: ctx.generatedAt ?? new Date() }))
}

/** Reporte de ajustes con los filtros de la tabla trasladados al Kárdex: lee todos los ajustes y descarga el PDF. */
export async function generateAdjustmentsReport(ctx: ProductReportContext): Promise<void> {
  const { items, truncated } = await exportInventoryTransactions(adjustmentsReportKardexQuery(ctx))
  await downloadReportPdf(buildAdjustmentsReport(items, truncated, { ...ctx, generatedAt: ctx.generatedAt ?? new Date() }))
}
