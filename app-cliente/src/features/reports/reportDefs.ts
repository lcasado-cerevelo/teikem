// Informes del cliente (2026-10-22, pedido de Luis: el portal no tenía ningún informe). Cada informe sabe qué pide (fechas, búsqueda), cómo trae TODOS sus datos (pagina el servidor de 100 en 100 hasta un tope),
// qué columnas lleva y qué suma. La pantalla los muestra en tabla y los exporta a CSV (se abre en Excel). Solo lectura y siempre del cliente de la sesión: lo garantiza el servidor.
import type { Lang } from '../../kernel/i18n/i18n'
import { listInvoices, listMovements, listOrders, listStock, getValuation } from '../portalApi'

export type ColType = 'text' | 'int' | 'qty' | 'money' | 'date' | 'datetime'
export type CellValue = string | number | null
export type ReportRow = Record<string, CellValue>

export interface ReportColumn {
  key: string
  /** Clave de i18n del encabezado. */
  title: string
  type: ColType
  /** Se suma en la fila de totales. */
  total?: boolean
}

export interface ReportParams {
  /** AAAA-MM-DD (incluida); vacío = sin límite. */
  from: string
  to: string
  search: string
}

export type ReportNeeds = 'transport' | 'warehouse' | 'value'

export interface ReportDef {
  id: string
  group: 'shipments' | 'money' | 'inventory'
  needs: ReportNeeds
  /** Qué filtros usa. */
  uses: { dates: boolean; search: boolean }
  columns: ReportColumn[]
  run: (p: ReportParams, lang: Lang) => Promise<{ rows: ReportRow[]; truncated: boolean }>
}

/** Tope de filas por informe (el servidor entrega 100 por página): más que eso se exporta por partes con fechas. */
export const MAX_ROWS = 5000
const TAKE = 100

/** Trae todas las páginas de una lista (hasta MAX_ROWS). */
export async function pageAll<T>(load: (skip: number, take: number) => Promise<{ items?: T[] | null; total?: number }>): Promise<{ items: T[]; truncated: boolean }> {
  const all: T[] = []
  let total = Infinity
  while (all.length < total && all.length < MAX_ROWS) {
    const page = await load(all.length, TAKE)
    const got = page.items ?? []
    total = page.total ?? got.length
    if (got.length === 0) break
    all.push(...got)
  }
  return { items: all.slice(0, MAX_ROWS), truncated: total > MAX_ROWS }
}

const startOf = (day: string) => (day ? `${day}T00:00:00Z` : undefined)
/** Fin del día incluido: el servidor compara con `toUtc` exclusivo, así que se manda el inicio del día siguiente. */
export function endExclusive(day: string): string | undefined {
  if (!day) return undefined
  const d = new Date(`${day}T00:00:00Z`)
  if (Number.isNaN(d.getTime())) return undefined
  d.setUTCDate(d.getUTCDate() + 1)
  return d.toISOString()
}
const dayOf = (iso: string | null | undefined) => (iso ? iso.slice(0, 10) : '')
const inRange = (iso: string | null | undefined, p: ReportParams) => {
  const d = dayOf(iso)
  if (!d) return !p.from && !p.to
  return (!p.from || d >= p.from) && (!p.to || d <= p.to)
}

async function shipments(p: ReportParams) {
  const { items, truncated } = await pageAll((skip, take) => listOrders({ search: p.search, skip, take, fromUtc: startOf(p.from), toUtc: endExclusive(p.to) }))
  return { items, truncated }
}

const SHIPMENT_COLS: ReportColumn[] = [
  { key: 'date', title: 'orders.col.date', type: 'date' },
  { key: 'waybill', title: 'orders.col.waybill', type: 'text' },
  { key: 'order', title: 'orders.col.order', type: 'text' },
  { key: 'invoice', title: 'invoices.col.number', type: 'text' },
  { key: 'consignee', title: 'orders.col.consignee', type: 'text' },
  { key: 'city', title: 'orders.col.city', type: 'text' },
  { key: 'service', title: 'reports.col.service', type: 'text' },
  { key: 'pieces', title: 'orders.col.pieces', type: 'int', total: true },
  { key: 'cod', title: 'orders.col.cod', type: 'money', total: true },
  { key: 'status', title: 'orders.col.status', type: 'text' },
]

export const REPORTS: ReportDef[] = [
  {
    id: 'shipments',
    group: 'shipments',
    needs: 'transport',
    uses: { dates: true, search: true },
    columns: SHIPMENT_COLS,
    run: async (p) => {
      const { items, truncated } = await shipments(p)
      return {
        truncated,
        rows: items.map((o) => ({ date: dayOf(o.createdAtUtc), waybill: o.waybillNumber ?? '', order: o.orderNumber ?? '', invoice: o.clientInvoiceNumber ?? '', consignee: o.consigneeName ?? '', city: [o.consigneeCity, o.consigneeState].filter(Boolean).join(', '), service: o.serviceTypeLabel ?? '', pieces: o.totalPieces ?? 0, cod: o.codAmount ?? 0, status: o.statusLabel ?? o.status ?? '' })),
      }
    },
  },
  {
    id: 'byStatus',
    group: 'shipments',
    needs: 'transport',
    uses: { dates: true, search: false },
    columns: [
      { key: 'status', title: 'orders.col.status', type: 'text' },
      { key: 'count', title: 'reports.col.shipments', type: 'int', total: true },
      { key: 'pieces', title: 'orders.col.pieces', type: 'int', total: true },
      { key: 'cod', title: 'orders.col.cod', type: 'money', total: true },
    ],
    run: async (p) => {
      const { items, truncated } = await shipments({ ...p, search: '' })
      const by = new Map<string, { status: string; count: number; pieces: number; cod: number }>()
      for (const o of items) {
        const key = o.statusLabel ?? o.status ?? '—'
        const g = by.get(key) ?? { status: key, count: 0, pieces: 0, cod: 0 }
        g.count += 1
        g.pieces += o.totalPieces ?? 0
        g.cod += o.codAmount ?? 0
        by.set(key, g)
      }
      return { truncated, rows: [...by.values()].sort((a, b) => b.count - a.count) }
    },
  },
  {
    id: 'cod',
    group: 'money',
    needs: 'transport',
    uses: { dates: true, search: false },
    columns: [
      { key: 'date', title: 'orders.col.date', type: 'date' },
      { key: 'waybill', title: 'orders.col.waybill', type: 'text' },
      { key: 'consignee', title: 'orders.col.consignee', type: 'text' },
      { key: 'city', title: 'orders.col.city', type: 'text' },
      { key: 'cod', title: 'orders.col.cod', type: 'money', total: true },
      { key: 'status', title: 'orders.col.status', type: 'text' },
    ],
    run: async (p) => {
      const { items, truncated } = await shipments({ ...p, search: '' })
      return {
        truncated,
        rows: items.filter((o) => (o.codAmount ?? 0) > 0).map((o) => ({ date: dayOf(o.createdAtUtc), waybill: o.waybillNumber ?? '', consignee: o.consigneeName ?? '', city: [o.consigneeCity, o.consigneeState].filter(Boolean).join(', '), cod: o.codAmount ?? 0, status: o.statusLabel ?? o.status ?? '' })),
      }
    },
  },
  {
    id: 'invoices',
    group: 'money',
    needs: 'transport',
    uses: { dates: true, search: false },
    columns: [
      { key: 'number', title: 'invoices.col.number', type: 'text' },
      { key: 'issued', title: 'invoices.col.issued', type: 'date' },
      { key: 'due', title: 'invoices.col.due', type: 'date' },
      { key: 'status', title: 'invoices.col.status', type: 'text' },
      { key: 'total', title: 'invoices.col.total', type: 'money', total: true },
      { key: 'paid', title: 'invoices.col.paid', type: 'money', total: true },
      { key: 'balance', title: 'invoices.col.balance', type: 'money', total: true },
    ],
    run: async (p) => {
      const { items, truncated } = await pageAll((skip, take) => listInvoices(undefined, skip, take))
      return {
        truncated,
        // el servidor no filtra por fechas: se filtra por la fecha de emisión aquí
        rows: items.filter((i) => inRange(i.issueDate, p)).map((i) => ({ number: i.number ?? '', issued: dayOf(i.issueDate), due: dayOf(i.dueDate), status: i.statusLabel ?? i.status ?? '', total: i.total ?? 0, paid: i.paid ?? 0, balance: i.status === 'VOID' ? 0 : (i.balance ?? 0) })),
      }
    },
  },
  {
    id: 'stock',
    group: 'inventory',
    needs: 'warehouse',
    uses: { dates: false, search: true },
    columns: [
      { key: 'sku', title: 'warehouse.col.sku', type: 'text' },
      { key: 'product', title: 'warehouse.col.product', type: 'text' },
      { key: 'warehouse', title: 'warehouse.col.warehouse', type: 'text' },
      { key: 'lot', title: 'warehouse.col.lot', type: 'text' },
      { key: 'expires', title: 'warehouse.col.expires', type: 'date' },
      { key: 'onHand', title: 'warehouse.onHand', type: 'qty', total: true },
      { key: 'reserved', title: 'warehouse.reserved', type: 'qty', total: true },
      { key: 'available', title: 'warehouse.available', type: 'qty', total: true },
    ],
    run: async (p) => {
      const { items, truncated } = await pageAll((skip, take) => listStock(p.search, skip, take))
      return { truncated, rows: items.map((i) => ({ sku: i.sku ?? '', product: i.productName ?? '', warehouse: i.warehouseCode ?? '', lot: i.lotNumber ?? '', expires: dayOf(i.expiryDate), onHand: i.onHand ?? 0, reserved: i.reserved ?? 0, available: i.available ?? 0 })) }
    },
  },
  {
    id: 'movements',
    group: 'inventory',
    needs: 'warehouse',
    uses: { dates: true, search: true },
    columns: [
      { key: 'date', title: 'warehouse.col.date', type: 'datetime' },
      { key: 'type', title: 'warehouse.col.type', type: 'text' },
      { key: 'sku', title: 'warehouse.col.sku', type: 'text' },
      { key: 'product', title: 'warehouse.col.product', type: 'text' },
      { key: 'qty', title: 'warehouse.col.qty', type: 'qty', total: true },
      { key: 'warehouse', title: 'warehouse.col.warehouse', type: 'text' },
      { key: 'lot', title: 'warehouse.col.lot', type: 'text' },
      { key: 'reference', title: 'warehouse.col.reference', type: 'text' },
      { key: 'reason', title: 'reports.col.reason', type: 'text' },
    ],
    run: async (p) => {
      const { items, truncated } = await pageAll((skip, take) => listMovements(p.search, skip, take))
      return {
        truncated,
        // el servidor no filtra por fechas: se filtra por la fecha del movimiento aquí
        rows: items.filter((m) => inRange(m.createdAtUtc, p)).map((m) => ({ date: m.createdAtUtc ?? '', type: m.type ?? '', sku: m.sku ?? '', product: m.productName ?? '', qty: m.quantity ?? 0, warehouse: m.warehouseCode ?? '', lot: m.lotNumber ?? '', reference: m.reference ?? '', reason: m.reason ?? '' })),
      }
    },
  },
  {
    id: 'valuation',
    group: 'inventory',
    needs: 'value',
    uses: { dates: false, search: true },
    columns: [
      { key: 'sku', title: 'warehouse.col.sku', type: 'text' },
      { key: 'product', title: 'warehouse.col.product', type: 'text' },
      { key: 'onHand', title: 'warehouse.onHand', type: 'qty', total: true },
      { key: 'unitCost', title: 'warehouse.unitCost', type: 'money' },
      { key: 'value', title: 'warehouse.value', type: 'money', total: true },
    ],
    run: async (p) => {
      const { items, truncated } = await pageAll((skip, take) => getValuation(p.search, skip, take))
      return { truncated, rows: items.map((r) => ({ sku: r.sku ?? '', product: r.productName ?? '', onHand: r.onHand ?? 0, unitCost: r.unitCost ?? null, value: r.value ?? 0 })) }
    },
  },
]

/** Los informes que ofrece la compañía de la sesión (transporte, almacén y, dentro de él, el valor). */
export function availableReports(offer: { transport: boolean; warehouse: boolean; canSeeValue: boolean }): ReportDef[] {
  return REPORTS.filter((r) => (r.needs === 'transport' ? offer.transport : r.needs === 'warehouse' ? offer.warehouse : offer.warehouse && offer.canSeeValue))
}

/** Sumas de las columnas que suman. */
export function totalsOf(columns: readonly ReportColumn[], rows: readonly ReportRow[]): Record<string, number> {
  const out: Record<string, number> = {}
  for (const c of columns) if (c.total) out[c.key] = Math.round(rows.reduce((n, r) => n + (typeof r[c.key] === 'number' ? (r[c.key] as number) : 0), 0) * 1000) / 1000
  return out
}
