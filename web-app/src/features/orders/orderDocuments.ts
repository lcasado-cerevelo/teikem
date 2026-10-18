// Lote F-G2 — documentos imprimibles de la lista de Órdenes (puros: arman el `ReportSpec` del kit; el PDF lo hace `renderReportPdf`),
// en papel CARTA para la impresora de documentos de la estación:
// - Waybill summary: una fila por orden (Empaque, N.º de orden, Factura, Cliente, Consignatario, Pueblo, ZIP, Servicio, Paquetes,
//   Piezas, COD), una sección por cliente con su subtotal (órdenes, piezas y COD) y el total general. Sin precio (D-FG14).
// - Packing list: una sección por orden (empaque · orden · factura · consignatario · dirección) con sus paquetes (tipo, n.º de paquete,
//   descripción, piezas, peso) y su subtotal; total general.
// Las órdenes canceladas o dadas de baja no entran (se avisa en el documento).
import type { ReportSpec, ReportValue } from '../../kernel/ui/reportPdf'
import type { OrderListItemDto } from './api'

type Translate = (key: string, params?: Record<string, string | number>) => string

export interface DocumentContext {
  t: Translate
  locale: string
  company?: string | null
  user?: string | null
  filters: { label: string; value: string }[]
  generatedAt?: Date
}

const live = (o: OrderListItemDto) => o.isActive !== false && (o.status ?? '').toUpperCase() !== 'CANCELLED'
const sum = (xs: number[]) => xs.reduce((a, b) => a + b, 0)
const piecesOf = (o: OrderListItemDto) => o.totalPieces ?? sum((o.packages ?? []).map((p) => p.pieces ?? 0))

function cancelledNotice(all: readonly OrderListItemDto[], t: Translate): string[] {
  const n = all.filter((o) => !live(o)).length
  return n > 0 ? [t('orders.docs.cancelledSkipped', { n })] : []
}

/** Waybill summary por cliente. */
export function waybillSummarySpec(orders: readonly OrderListItemDto[], ctx: DocumentContext): ReportSpec {
  const { t } = ctx
  const rows = orders.filter(live)
  const byClient = new Map<string, OrderListItemDto[]>()
  for (const o of rows) {
    const k = o.clientName ?? ''
    byClient.set(k, [...(byClient.get(k) ?? []), o])
  }
  const sections = [...byClient.entries()]
    .sort((a, b) => a[0].localeCompare(b[0], undefined, { sensitivity: 'base' }))
    .map(([client, list]) => ({
      title: client || '—',
      rows: list.map((o): ReportValue[] => [
        o.packBatchNumber, o.orderNumber, o.clientInvoiceNumber, o.clientName, o.consigneeName, o.consigneeCity, o.consigneePostalCode ?? '',
        o.serviceTypeLabel || o.serviceType, o.packagesSummary, piecesOf(o), o.codAmount ?? 0,
      ]),
      subtotal: [t('orders.docs.subtotal', { n: list.length }), '', '', '', '', '', '', '', '', sum(list.map(piecesOf)), sum(list.map((o) => o.codAmount ?? 0))] as ReportValue[],
    }))
  return {
    title: t('orders.docs.waybillTitle'),
    company: ctx.company,
    user: ctx.user,
    generatedAt: ctx.generatedAt,
    locale: ctx.locale,
    filters: ctx.filters,
    paper: 'letter',
    orientation: 'landscape',
    columns: [
      { header: t('orders.docs.cols.pack'), noWrap: true },
      { header: t('orders.docs.cols.order'), noWrap: true },
      { header: t('orders.docs.cols.invoice') },
      { header: t('orders.docs.cols.client') },
      { header: t('orders.docs.cols.consignee') },
      { header: t('orders.docs.cols.town') },
      { header: t('orders.docs.cols.zip'), noWrap: true },
      { header: t('orders.docs.cols.service') },
      { header: t('orders.docs.cols.packages') },
      { header: t('orders.docs.cols.pieces'), format: 'quantity' },
      { header: t('orders.docs.cols.cod'), format: 'money' },
    ],
    sections,
    totals: [[t('orders.docs.total', { n: rows.length }), '', '', '', '', '', '', '', '', sum(rows.map(piecesOf)), sum(rows.map((o) => o.codAmount ?? 0))]],
    summary: [
      { label: t('orders.docs.sumOrders'), value: String(rows.length) },
      { label: t('orders.docs.sumPieces'), value: String(sum(rows.map(piecesOf))) },
    ],
    notices: cancelledNotice(orders, t),
    emptyText: t('orders.docs.empty'),
  }
}

/** Packing list: una sección por orden con sus paquetes. */
export function packingListSpec(orders: readonly OrderListItemDto[], ctx: DocumentContext, weight: (kg: number) => string = (kg) => String(kg)): ReportSpec {
  const { t } = ctx
  const rows = orders.filter(live)
  const sections = rows.map((o) => {
    const address = [o.consigneeLine1, o.consigneeLine2, [o.consigneeCity, o.consigneeState, o.consigneePostalCode].filter(Boolean).join(' ')].filter(Boolean).join(', ')
    const pkgs = o.packages ?? []
    return {
      title: [o.packBatchNumber, o.orderNumber, o.clientInvoiceNumber && `${t('orders.docs.cols.invoice')} ${o.clientInvoiceNumber}`, o.consigneeName, address].filter(Boolean).join(' · '),
      rows: pkgs.map((p): ReportValue[] => [p.packageTypeLabel || p.packageType || '', (p.pieceNumbers?.length ? p.pieceNumbers.join(', ') : p.packageNumber) ?? '', p.description, p.pieces ?? 0, p.weightKg ?? null]),
      subtotal: [t('orders.docs.packageCount', { n: pkgs.length }), '', '', sum(pkgs.map((p) => p.pieces ?? 0)), sum(pkgs.map((p) => p.weightKg ?? 0))] as ReportValue[],
    }
  })
  const all = rows.flatMap((o) => o.packages ?? [])
  return {
    title: t('orders.docs.packingTitle'),
    company: ctx.company,
    user: ctx.user,
    generatedAt: ctx.generatedAt,
    locale: ctx.locale,
    filters: ctx.filters,
    paper: 'letter',
    orientation: 'portrait',
    columns: [
      { header: t('orders.docs.cols.packageType') },
      { header: t('orders.docs.cols.packageNumber'), noWrap: true },
      { header: t('orders.docs.cols.description') },
      { header: t('orders.docs.cols.pieces'), format: 'quantity' },
      { header: t('orders.docs.cols.weight'), format: 'quantity' },
    ],
    sections,
    totals: [[t('orders.docs.total', { n: rows.length }), '', '', sum(all.map((p) => p.pieces ?? 0)), sum(all.map((p) => p.weightKg ?? 0))]],
    summary: [
      { label: t('orders.docs.sumOrders'), value: String(rows.length) },
      { label: t('orders.docs.sumPackages'), value: String(all.length) },
      { label: t('orders.docs.sumWeight'), value: weight(sum(all.map((p) => p.weightKg ?? 0))) },
    ],
    notices: cancelledNotice(orders, t),
    emptyText: t('orders.docs.empty'),
  }
}
