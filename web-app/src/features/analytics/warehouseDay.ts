// Lote 15 (P4) — lógica pura de la franja "Almacén hoy" del Pulso: las 4 tarjetas en orden (D1–D3), sus 7 barritas, la regla
// del naranja (la decide el servidor: `countsAlert`, `belowMinAlert`) y a dónde lleva el clic en cada una (D4: el detalle ya
// filtrado con los 7 días y el almacén). Sin React: se prueba sola.
import type { WarehousePulseDays } from './api'

/** Tarjetas de la franja, en el orden de la pantalla (D1: "Productos bajo mínimo" al final y sin gráfico). */
export const WAREHOUSE_DAY_CARDS = ['received', 'outbound', 'countsVariance', 'belowMin'] as const
export type WarehouseDayCardKey = (typeof WAREHOUSE_DAY_CARDS)[number]

/** Tipos de movimiento del Kárdex de cada tarjeta de unidades (D2: salida = recolección + cruce de muelle). */
export const RECEIVED_TXN_TYPES: readonly string[] = ['RECEIPT']
export const OUTBOUND_TXN_TYPES: readonly string[] = ['ISSUE', 'CROSSDOCK']
/** Estatus "Diferencia" del conteo cíclico (Lote 14). */
export const COUNT_VARIANCE_STATUS = 'RECONCILED_VARIANCE'

export interface WarehouseDayPoint {
  /** Día local 'YYYY-MM-DD'. */
  date: string
  value: number
}

export interface WarehouseDayCard {
  key: WarehouseDayCardKey
  /** Número grande: lo de HOY (bajo mínimo: ahora). null = aún no hay datos. */
  today: number | null
  /** Total de los 7 días (texto pequeño); null en "Productos bajo mínimo". */
  total: number | null
  /** Las barritas (un punto por día, el último es hoy); null en "Productos bajo mínimo". */
  points: WarehouseDayPoint[] | null
  /** Borde y número naranja (D3). */
  alert: boolean
  /** Destino del clic (D4). */
  href: string
}

/** Primer y último día de la ventana ('YYYY-MM-DD'); vacíos si aún no hay datos. */
export function warehouseDayRange(dto: WarehousePulseDays | undefined): { from: string; to: string } {
  const days = dto?.days ?? []
  return { from: days[0]?.date ?? '', to: dto?.today ?? days.at(-1)?.date ?? '' }
}

/**
 * Enlace de cada tarjeta (D4), con los 7 días y el almacén elegido:
 * - Unidades recibidas → Kárdex `?types=RECEIPT&from=&to=[&warehousePublicIds=]`.
 * - Unidades de salida → Kárdex `?types=ISSUE&types=CROSSDOCK&from=&to=[&warehousePublicIds=]` (las recolecciones eliminadas
 *   restan en la tarjeta y en el Kárdex salen como ajuste: por eso el Kárdex puede sumar más).
 * - Conteos con diferencia → Conteo cíclico `?status=RECONCILED_VARIANCE[&warehousePublicIds=]` (sin fechas: el filtro
 *   de fechas de la lista es el de ALTA del conteo, no el de cierre, y dejaría fuera un conteo creado antes y cerrado en la
 *   ventana).
 * - Productos bajo mínimo → Productos e inventario `?kpi=low[&warehousePublicIds=]`.
 */
export function warehouseDayHref(key: WarehouseDayCardKey, range: { from: string; to: string }, warehousePublicId: string | null): string {
  const q = new URLSearchParams()
  let path: string
  switch (key) {
    case 'received':
    case 'outbound':
      path = '/warehouse/kardex'
      for (const type of key === 'received' ? RECEIVED_TXN_TYPES : OUTBOUND_TXN_TYPES) q.append('types', type)
      if (range.from) q.set('from', range.from)
      if (range.to) q.set('to', range.to)
      break
    case 'countsVariance':
      path = '/warehouse/cycle-counts'
      q.set('status', COUNT_VARIANCE_STATUS)
      break
    default:
      path = '/warehouse/products'
      q.set('kpi', 'low')
  }
  if (warehousePublicId) q.set('warehousePublicIds', warehousePublicId)
  return `${path}?${q.toString()}`
}

/** Las 4 tarjetas de la franja a partir del DTO (sin DTO: números nulos, sin barritas ni naranja; los enlaces sin fechas). */
export function warehouseDayCards(dto: WarehousePulseDays | undefined, warehousePublicId: string | null): WarehouseDayCard[] {
  const days = dto?.days ?? []
  const range = warehouseDayRange(dto)
  const has = dto != null
  const series = (pick: (d: (typeof days)[number]) => number | undefined): WarehouseDayPoint[] | null =>
    has ? days.map((d) => ({ date: d.date ?? '', value: pick(d) ?? 0 })) : null
  return [
    {
      key: 'received',
      today: has ? (dto.receivedToday ?? 0) : null,
      total: has ? (dto.receivedTotal ?? 0) : null,
      points: series((d) => d.receivedUnits),
      alert: false,
      href: warehouseDayHref('received', range, warehousePublicId),
    },
    {
      key: 'outbound',
      today: has ? (dto.outboundToday ?? 0) : null,
      total: has ? (dto.outboundTotal ?? 0) : null,
      points: series((d) => d.outboundUnits),
      alert: false,
      href: warehouseDayHref('outbound', range, warehousePublicId),
    },
    {
      key: 'countsVariance',
      today: has ? (dto.countsWithVarianceToday ?? 0) : null,
      total: has ? (dto.countsWithVarianceTotal ?? 0) : null,
      points: series((d) => d.countsWithVariance),
      alert: has && dto.countsAlert === true,
      href: warehouseDayHref('countsVariance', range, warehousePublicId),
    },
    {
      key: 'belowMin',
      today: has ? (dto.belowMinProducts ?? 0) : null,
      total: null,
      points: null,
      alert: has && dto.belowMinAlert === true,
      href: warehouseDayHref('belowMin', range, warehousePublicId),
    },
  ]
}
