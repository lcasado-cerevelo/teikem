// Lote F-G2 — «Informe de faltantes» de la estación de escaneo (maqueta: botón junto a la voz): las órdenes que siguen pendientes por
// escanear con los MISMOS filtros de «Pendientes» (todas las páginas, sin tope), agrupadas por zona de despacho («Sin zona», «Zona
// ambigua»), con Empaque, Orden, Factura, Cliente, Consignatario, Pueblo, ZIP, Piezas y Fecha pedida; y el resumen de pendientes y
// escaneadas en esta sesión. Puro: arma el `ReportSpec`; el PDF lo hace el kit y lo imprime la impresora de documentos de la estación.
import type { ReportSpec, ReportValue } from '../../kernel/ui/reportPdf'
import type { components } from '../../kernel/api/schema'

type UnassignedOrder = components['schemas']['UnassignedOrderDto']
type Translate = (key: string, params?: Record<string, string | number>) => string

export interface MissingContext {
  t: Translate
  locale: string
  company?: string | null
  user?: string | null
  filters: { label: string; value: string }[]
  /** Escaneadas en esta sesión (lista de la sesión bajo el filtro). */
  scanned: number
  /** Fecha pedida ya formateada con los ajustes de la compañía. */
  date: (iso: string | null | undefined) => string
  generatedAt?: Date
}

/** Grupo de una orden: su zona, «Zona ambigua» o «Sin zona». */
export function zoneGroupOf(o: UnassignedOrder, t: Translate): string {
  if (o.zoneAmbiguous) return t('scan.missing.ambiguous')
  return o.zoneCode?.trim() || t('scan.missing.noZone')
}

export function missingReportSpec(orders: readonly UnassignedOrder[], ctx: MissingContext): ReportSpec {
  const { t } = ctx
  const groups = new Map<string, UnassignedOrder[]>()
  for (const o of orders) {
    const g = zoneGroupOf(o, t)
    groups.set(g, [...(groups.get(g) ?? []), o])
  }
  const special = new Set([t('scan.missing.noZone'), t('scan.missing.ambiguous')])
  const keys = [...groups.keys()].sort((a, b) => Number(special.has(a)) - Number(special.has(b)) || a.localeCompare(b, undefined, { numeric: true }))
  const pieces = (list: readonly UnassignedOrder[]) => list.reduce((s, o) => s + (o.pieces ?? 0), 0)
  return {
    title: t('scan.missing.title'),
    company: ctx.company,
    user: ctx.user,
    generatedAt: ctx.generatedAt,
    locale: ctx.locale,
    filters: ctx.filters,
    paper: 'letter',
    orientation: 'landscape',
    columns: [
      { header: t('scan.missing.cols.pack'), noWrap: true },
      { header: t('scan.missing.cols.order'), noWrap: true },
      { header: t('scan.missing.cols.invoice') },
      { header: t('scan.missing.cols.client') },
      { header: t('scan.missing.cols.consignee') },
      { header: t('scan.missing.cols.town') },
      { header: t('scan.missing.cols.zip'), noWrap: true },
      { header: t('scan.missing.cols.pieces'), format: 'quantity' },
      { header: t('scan.missing.cols.requested'), noWrap: true },
    ],
    sections: keys.map((k) => {
      const list = groups.get(k)!
      return {
        title: `${k} (${list.length})`,
        rows: list.map((o): ReportValue[] => [o.packBatchNumber, o.orderNumber, o.clientInvoiceNumber, o.clientName, o.consigneeName ?? '', o.city, o.postalCode ?? '', o.pieces ?? null, ctx.date(o.requestedDate)]),
        subtotal: [t('scan.missing.subtotal', { n: list.length }), '', '', '', '', '', '', pieces(list), ''] as ReportValue[],
      }
    }),
    totals: [[t('scan.missing.total', { n: orders.length }), '', '', '', '', '', '', pieces(orders), '']],
    summary: [
      { label: t('scan.missing.sumPending'), value: String(orders.length), tone: 'money' },
      { label: t('scan.missing.sumScanned'), value: String(ctx.scanned), tone: 'flow' },
    ],
    emptyText: t('scan.missing.empty'),
  }
}
