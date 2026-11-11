// Lote 173 — cuentas de la fila de «Recolección y empaque» por documento (puras, sin API).
import type { PickDocumentDto } from './api'
import { OWN, type CollectLine } from './collectForm'

export interface DocumentProgress {
  /** Pedido; null en un envío (no tiene pedido: solo lo recolectado). */
  ordered: number | null
  shipped: number
  /** Reservado todavía por sacar (lo que falta recolectar de lo ya reservado). */
  toCollect: number
  /** Pedido que ni se sacó ni se reservó (espera inventario). */
  unreserved: number
  /** Sacado sobre pedido, 0 a 100; null si no hay pedido. */
  percent: number | null
}

export function documentProgress(d: Pick<PickDocumentDto, 'ordered' | 'reserved' | 'shipped'>): DocumentProgress {
  const shipped = d.shipped ?? 0
  if (d.ordered == null) return { ordered: null, shipped, toCollect: 0, unreserved: 0, percent: null }
  const reserved = d.reserved ?? 0
  const unreserved = Math.max(0, round3(d.ordered - shipped - reserved))
  const percent = d.ordered > 0 ? Math.min(100, Math.round((shipped / d.ordered) * 100)) : 0
  return { ordered: d.ordered, shipped, toCollect: reserved, unreserved, percent }
}

const round3 = (n: number) => Math.round(n * 1000) / 1000

/**
 * 2026-11-11 (pedido de Luis): del documento al formulario «Recolección». Un renglón del formulario por cada posición (y lote) donde el documento YA reservó lo que hay que sacar,
 * con esa cantidad; si una reserva no trae posición (dato viejo) va una sola línea con lo reservado y la posición la elige el servidor por FEFO. Las series de un producto SERIAL no se
 * conocen aquí: la persona las escanea. Devuelve null si no hay nada reservado por sacar.
 */
export interface CollectPreload {
  /** Cambia en cada pedido para que volver a tocar «Recolectar» del mismo documento vuelva a cargar el formulario. */
  token: number
  documentNumber: string
  warehousePublicId: string | null
  lines: CollectLine[]
}

export function preloadFromDocument(d: Pick<PickDocumentDto, 'number' | 'warehousePublicId' | 'lines'>, token: number): CollectPreload | null {
  const lines: CollectLine[] = []
  for (const l of d.lines ?? []) {
    if (!l.productPublicId || (l.reserved ?? 0) <= 0) continue
    const base = { productPublicId: l.productPublicId, sku: l.sku ?? '', trackingTypeCode: l.trackingTypeCode ?? '', owner: l.ownerClientPublicId ?? OWN, available: null, serialNumbers: '' }
    const locs = (l.locations ?? []).filter((x) => (x.quantity ?? 0) > 0)
    if (locs.length === 0) lines.push({ ...base, quantity: l.reserved ?? null, binId: '', lotId: '' })
    else for (const x of locs) lines.push({ ...base, quantity: x.quantity ?? null, binId: String(x.binId), lotId: x.lotId != null && (base.trackingTypeCode === 'LOT' || base.trackingTypeCode === 'SERIAL') ? String(x.lotId) : '' })
  }
  return lines.length === 0 ? null : { token, documentNumber: d.number ?? '', warehousePublicId: d.warehousePublicId ?? null, lines }
}
