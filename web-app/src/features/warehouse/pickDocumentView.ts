// Lote 173 — cuentas de la fila de «Recolección y empaque» por documento (puras, sin API).
import type { PickDocumentDto } from './api'

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
