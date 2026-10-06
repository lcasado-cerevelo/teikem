// Lote F17 (Rentas F-R1) — lógica pura de "Convertir a serie" (servidor: Lote 26, manual 06 §2.1,
// `POST /api/v1/products/{publicId}/convert-to-serial`). Sin React: posiciones con existencia del producto (de los saldos),
// lo que impide convertir (reservas, lote, existencia sin posición o fraccionaria), validación de lo capturado por posición
// (tantas series como unidades en mano, sin repetir, ≤ 80 caracteres, ≤ 500 por posición) y el cuerpo de la solicitud.
// Los problemas son códigos que la pantalla traduce con `t('warehouse.convertSerial.errors.<code>', params)`: en español son
// los mensajes exactos del servidor (SerialConversionRules / SerialRules).
import type { components } from '../../kernel/api/schema'
import { parseSerials } from './lineRules'

type Schemas = components['schemas']
type BalanceDto = Schemas['BalanceDto']
export type ProductSerialConversionRequest = Schemas['ProductSerialConversionRequest']

/** Topes del servidor. */
export const CONVERSION_LIMITS = { serial: 80, perPosition: 500, notes: 300 } as const

/** Una posición con existencia que se convierte: se capturan tantas series como `onHand`. */
export interface ConversionPosition {
  binId: number
  binCode: string
  warehouseCode: string
  zoneCode: string
  onHand: number
}

export interface ConversionIssue {
  code: string
  params?: Record<string, string | number>
}

/** ¿El botón aplica? Producto sin seguimiento (NONE) con existencia en mano. */
export function canConvertToSerial(product: { trackingTypeCode?: string | null; qtyOnHand?: number | null; isOwn?: boolean | null } | null | undefined): boolean {
  if (!product) return false
  return (product.trackingTypeCode ?? 'NONE').toUpperCase() === 'NONE' && (product.qtyOnHand ?? 0) > 0
}

/**
 * Posiciones a convertir y lo que bloquea la conversión, de los saldos del producto (`includeZero=false`): unidades
 * reservadas (409), existencia con lote o sin posición (422) y existencia fraccionaria (422). Orden por código de posición
 * (el del servidor al avisar del primer descuadre).
 */
export function conversionPlan(sku: string, balances: readonly BalanceDto[]): { positions: ConversionPosition[]; blockers: ConversionIssue[] } {
  const positions: ConversionPosition[] = []
  const blockers: ConversionIssue[] = []
  const byBin = new Map<number, ConversionPosition>()
  let reserved = false
  for (const b of balances) {
    const onHand = b.qtyOnHand ?? 0
    if (onHand <= 0) continue
    if ((b.qtyReserved ?? 0) > 0) reserved = true
    if (b.binId == null || b.lotId != null || b.lotNumber) {
      const where = b.binCode ?? b.warehouseCode ?? ''
      blockers.push({ code: 'notInBin', params: { sku, where } })
      continue
    }
    const existing = byBin.get(b.binId)
    if (existing) existing.onHand += onHand
    else {
      const p = { binId: b.binId, binCode: b.binCode ?? String(b.binId), warehouseCode: b.warehouseCode ?? '', zoneCode: b.zoneCode ?? '', onHand }
      byBin.set(b.binId, p)
      positions.push(p)
    }
  }
  for (const p of positions) {
    if (p.onHand !== Math.trunc(p.onHand)) blockers.push({ code: 'fractional', params: { sku, bin: p.binCode, qty: p.onHand } })
  }
  if (reserved) blockers.unshift({ code: 'reserved', params: { sku } })
  positions.sort((a, b) => a.binCode.localeCompare(b.binCode, 'en', { sensitivity: 'base' }) || a.binId - b.binId)
  return { positions, blockers }
}

/** Series capturadas de una posición (una por renglón o separadas por coma o punto y coma). */
export function capturedSerials(text: string | null | undefined): string[] {
  return parseSerials(text)
}

/**
 * Problemas de lo capturado: por posición (conteo, más de 500, serie larga) y las series repetidas entre todas las
 * posiciones (sin distinguir mayúsculas). `first` = el primero en el orden de las posiciones (el que diría el servidor).
 */
export function conversionIssues(
  positions: readonly ConversionPosition[],
  texts: Readonly<Record<number, string>>,
): { byBin: Record<number, ConversionIssue[]>; first: ConversionIssue | null; total: number; captured: number } {
  const byBin: Record<number, ConversionIssue[]> = {}
  const seen = new Map<string, number>()
  let first: ConversionIssue | null = null
  let total = 0
  let captured = 0
  const push = (binId: number, issue: ConversionIssue) => {
    ;(byBin[binId] ??= []).push(issue)
    total++
    first ??= issue
  }
  for (const p of positions) {
    const serials = capturedSerials(texts[p.binId])
    captured += serials.length
    for (const s of serials) {
      const k = s.toUpperCase()
      if (s.length > CONVERSION_LIMITS.serial) push(p.binId, { code: 'tooLong', params: { serial: s } })
      if (seen.has(k)) push(p.binId, { code: 'duplicate', params: { serial: s } })
      else seen.set(k, p.binId)
    }
    if (serials.length > CONVERSION_LIMITS.perPosition) push(p.binId, { code: 'tooMany' })
    if (serials.length !== p.onHand) push(p.binId, { code: 'count', params: { n: p.onHand, bin: p.binCode, m: serials.length } })
  }
  return { byBin, first, total, captured }
}

/** Cuerpo de la solicitud: una posición por renglón con sus series; nota vacía → null (el servidor pone "Conversión a serie"). */
export function conversionBody(
  positions: readonly ConversionPosition[],
  texts: Readonly<Record<number, string>>,
  notes: string,
  rowVersion: string | null | undefined,
): ProductSerialConversionRequest {
  return {
    positions: positions.map((p) => ({ binId: p.binId, serialNumbers: capturedSerials(texts[p.binId]) })),
    notes: notes.trim() || null,
    rowVersion: rowVersion ?? null,
  }
}
