// Lote F-G2 — de las órdenes a los labels (puro). Un label POR PIEZA: la cantidad de cada línea de carga (de la unidad que sea) es el
// número de labels de esa línea. Lote 109: cada pieza lleva SU PROPIO número de paquete, único, en el código de barras (el de `pieceNumbers`; antes el
// de la línea repetido en cada pieza, D-FG17) y «Paquete i de N» con i corrido sobre todas las piezas de la orden y N = sus piezas.
// - Una pieza sin número de paquete no lleva label (se cuenta para avisar).
// - Las órdenes canceladas o dadas de baja no se imprimen (se cuentan para avisar).
// - Waybill = número de conduce (Lote 97; antes el número de orden, D-FG10); factura = factura del cliente; sender = el cliente; consignatario y dirección = la parada de
//   entrega; servicio = su etiqueta; logo = el nombre de la compañía (D-FG11). Nunca el precio.
import type { OrderLabelData } from '../../kernel/ui/orderLabelLayout'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
type ListItem = Schemas['OrderListItemDto']
type Detail = Schemas['OrderDetailDto']

/** Desde 500 labels se avisa que el PDF será grande (se imprimen todos: no hay tope). */
export const LARGE_LABEL_JOB = 500

export interface LabelPackage {
  /** El de la línea (órdenes anteriores al Lote 109, sin piezas con número propio). */
  packageNumber: string | null
  pieces: number
  /** Lote 109: el número único de cada pieza de la línea, en orden. */
  pieceNumbers?: string[]
}

/** Lo que hace falta de una orden para sus labels (sale de la lista o de la ficha). */
export interface LabelOrder {
  publicId: string
  orderNumber: string
  /** Lote 97: número de conduce (único por orden); sale como «Waybill» del label. Sin él (órdenes de una versión vieja) se usa el número de orden. */
  waybillNumber?: string | null
  clientInvoiceNumber: string
  clientName: string
  consigneeName: string
  addressLines: string[]
  serviceLabel: string
  codAmount: number | null
  packages: LabelPackage[]
  cancelled: boolean
}

/** «Pueblo, ST 00949» sin partes vacías. */
export function cityLine(city?: string | null, state?: string | null, zip?: string | null): string {
  const c = (city ?? '').trim()
  const s = (state ?? '').trim()
  const z = (zip ?? '').trim()
  return [c && s ? `${c}, ${s}` : c || s, z].filter(Boolean).join(' ')
}

const isCancelled = (status: string | null | undefined, active: boolean | undefined) => active === false || (status ?? '').toUpperCase() === 'CANCELLED'
const packagesOf = (lines: readonly Schemas['OrderPackageLineDto'][] | null | undefined): LabelPackage[] =>
  (lines ?? []).map((p) => ({ packageNumber: p.packageNumber?.trim() || null, pieces: Math.max(0, Math.floor(p.pieces ?? 0)), pieceNumbers: (p.pieceNumbers ?? []).map((n) => n.trim()).filter(Boolean) }))

export function labelOrderFromList(o: ListItem): LabelOrder {
  return {
    publicId: o.publicId ?? '',
    orderNumber: o.orderNumber ?? '',
    waybillNumber: o.waybillNumber ?? null,
    clientInvoiceNumber: o.clientInvoiceNumber ?? '',
    clientName: o.clientName ?? '',
    consigneeName: o.consigneeName ?? '',
    addressLines: [o.consigneeLine1 ?? '', o.consigneeLine2 ?? '', cityLine(o.consigneeCity, o.consigneeState, o.consigneePostalCode)].map((l) => l.trim()).filter(Boolean),
    serviceLabel: o.serviceTypeLabel || o.serviceType || '',
    codAmount: o.codAmount ?? null,
    packages: packagesOf(o.packages),
    cancelled: isCancelled(o.status, o.isActive),
  }
}

export function labelOrderFromDetail(o: Detail): LabelOrder {
  const d = o.delivery
  return {
    publicId: o.publicId ?? '',
    orderNumber: o.orderNumber ?? '',
    waybillNumber: o.waybillNumber ?? null,
    clientInvoiceNumber: o.clientInvoiceNumber ?? '',
    clientName: o.clientName ?? '',
    consigneeName: d?.name ?? '',
    addressLines: [d?.line1 ?? '', d?.line2 ?? '', cityLine(d?.city, d?.state, d?.postalCode)].map((l) => l.trim()).filter(Boolean),
    serviceLabel: o.serviceTypeLabel || o.serviceType || '',
    codAmount: o.codAmount ?? null,
    packages: packagesOf(o.packages),
    cancelled: isCancelled(o.status, o.isActive),
  }
}

/** Labels de UNA orden (uno por pieza) y cuántas piezas quedaron sin label por no tener número de paquete. */
export function orderLabelsOf(order: LabelOrder, company: string, money: (n: number) => string): { labels: OrderLabelData[]; withoutNumber: number } {
  const total = order.packages.reduce((s, p) => s + p.pieces, 0)
  const labels: OrderLabelData[] = []
  let withoutNumber = 0
  let i = 0
  for (const p of order.packages) {
    for (let k = 0; k < p.pieces; k++) {
      i++
      const number = p.pieceNumbers?.[k] || p.packageNumber   // Lote 109: el de la pieza; sin él, el de la línea (órdenes viejas)
      if (!number) {
        withoutNumber++
        continue
      }
      labels.push({
        company,
        service: order.serviceLabel,
        sender: order.clientName,
        consignee: order.consigneeName,
        addressLines: order.addressLines,
        packageNumber: number,
        packageIndex: i,
        packageCount: total,
        waybill: order.waybillNumber || order.orderNumber,
        invoice: order.clientInvoiceNumber,
        cod: order.codAmount,
        codText: order.codAmount != null && order.codAmount > 0 ? money(order.codAmount) : null,
      })
    }
  }
  return { labels, withoutNumber }
}

export interface LabelJob {
  labels: OrderLabelData[]
  /** Órdenes que sí entraron (no canceladas). */
  orders: number
  cancelled: number
  /** Piezas sin número de paquete (sin label). */
  withoutNumber: number
  large: boolean
}

/**
 * Label de FACTURA: uno por orden (no por pieza) con el número de factura del cliente. Las órdenes canceladas y las que no tienen número de factura no llevan;
 * `withoutInvoice` cuenta estas últimas para avisar. Los demás datos van por si la plantilla de factura del cliente los pide (consignatario, servicio, COD…).
 */
export function buildInvoiceLabelJob(orders: readonly LabelOrder[], company: string, money: (n: number) => string): LabelJob & { withoutInvoice: number } {
  const labels: OrderLabelData[] = []
  let cancelled = 0
  let withoutInvoice = 0
  let count = 0
  for (const o of orders) {
    if (o.cancelled) {
      cancelled++
      continue
    }
    const invoice = o.clientInvoiceNumber.trim()
    if (!invoice) {
      withoutInvoice++
      continue
    }
    count++
    labels.push({
      company,
      service: o.serviceLabel,
      sender: o.clientName,
      consignee: o.consigneeName,
      addressLines: o.addressLines,
      packageNumber: null,
      packageIndex: 1,
      packageCount: Math.max(1, o.packages.reduce((s, p) => s + p.pieces, 0)),
      waybill: o.waybillNumber || o.orderNumber,
      invoice,
      cod: o.codAmount,
      codText: o.codAmount != null && o.codAmount > 0 ? money(o.codAmount) : null,
    })
  }
  return { labels, orders: count, cancelled, withoutNumber: 0, withoutInvoice, large: labels.length >= LARGE_LABEL_JOB }
}

/** Todos los labels de varias órdenes, en el orden recibido. */
export function buildLabelJob(orders: readonly LabelOrder[], company: string, money: (n: number) => string): LabelJob {
  const labels: OrderLabelData[] = []
  let cancelled = 0
  let withoutNumber = 0
  let count = 0
  for (const o of orders) {
    if (o.cancelled) {
      cancelled++
      continue
    }
    count++
    const r = orderLabelsOf(o, company, money)
    labels.push(...r.labels)
    withoutNumber += r.withoutNumber
  }
  return { labels, orders: count, cancelled, withoutNumber, large: labels.length >= LARGE_LABEL_JOB }
}

/** Nombre base del archivo de labels: el número de la orden si es una, si no «labels-N». */
export function labelFileBase(orders: readonly LabelOrder[], n: number): string {
  const live = orders.filter((o) => !o.cancelled)
  return live.length === 1 ? `labels-${live[0].orderNumber || 'orden'}` : `labels-${n}`
}
