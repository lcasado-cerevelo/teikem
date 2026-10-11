// 2026-10-11 — Despacho manual (DMA-#####) en la app: reglas puras de «Completar despacho» (sin pantalla, API ni base). Es la salida de inventario SIN
// entrega (muestra, uso interno, retiro del cliente, venta, otro) con un MOTIVO obligatorio del catálogo ManualIssueReason y una NOTA libre opcional de
// hasta 500 caracteres; el servidor le da su número DMA-##### (POST /api/v1/manual-issues, permiso warehouse.issue; docs/lote31-decisiones.md y
// docs/manual/06-inventario-y-almacen.md §7b). Las líneas son las mismas de la recolección (producto, cantidad y posición ya resuelta a su id).
import type { ResolvedPickLine } from './dispatchLogic'

/** Código exacto del permiso del API (PermissionCatalog.WarehouseIssue). */
export const WAREHOUSE_ISSUE = 'warehouse.issue'

/** Máximo de la nota (PickBatchRules: «La nota admite como máximo 500 caracteres.»). */
export const MANUAL_ISSUE_NOTE_MAX = 500

/** Los cinco motivos de fábrica (sección 2026-10-11 de Diseño/logistica-db-update.sql): respaldo mientras el aparato no haya bajado los de la
 *  compañía (que puede renombrarlos o deshabilitarlos). */
export const FACTORY_MANUAL_ISSUE_REASONS = [
  { code: 'SAMPLE', key: 'dispatch.reasonSample' },
  { code: 'INTERNAL_USE', key: 'dispatch.reasonInternalUse' },
  { code: 'CUSTOMER_PICKUP', key: 'dispatch.reasonCustomerPickup' },
  { code: 'SALE', key: 'dispatch.reasonSale' },
  { code: 'OTHER', key: 'dispatch.reasonOther' },
] as const

/** Un motivo tal como se guarda en el aparato (de GET /api/v1/manual-issues/reasons). */
export interface StoredReason {
  code: string
  /** Etiqueta en el idioma de quien lo bajó (respaldo si falta la del idioma de la pantalla). */
  label: string
  /** Etiquetas por idioma ({"es": .., "en": ..}), con el nombre que la compañía le haya puesto. */
  labels: Record<string, string>
  sortOrder: number
}

export interface ReasonOption {
  code: string
  label: string
}

/**
 * Los motivos que se ofrecen: los de la compañía si ya se bajaron alguna vez (aunque sea una lista vacía no se inventa nada: se respeta lo que
 * mandó el servidor), en su orden y en el idioma de la pantalla; si nunca se bajaron (null), los cinco de fábrica traducidos.
 */
export function reasonOptions(stored: readonly StoredReason[] | null, lang: string, t: (key: string) => string): ReasonOption[] {
  if (stored === null) return FACTORY_MANUAL_ISSUE_REASONS.map((r) => ({ code: r.code, label: t(r.key) }))
  return [...stored]
    .sort((a, b) => a.sortOrder - b.sortOrder || a.code.localeCompare(b.code))
    .map((r) => ({ code: r.code, label: r.labels[lang]?.trim() || r.label?.trim() || factoryLabel(r.code, t) || r.code }))
}

function factoryLabel(code: string, t: (key: string) => string): string | null {
  const f = FACTORY_MANUAL_ISSUE_REASONS.find((r) => r.code === code)
  return f ? t(f.key) : null
}

/** La nota pasa el tope del servidor. */
export function noteTooLong(note: string): boolean {
  return note.trim().length > MANUAL_ISSUE_NOTE_MAX
}

/** Qué falta para poder despachar (null = listo). */
export type ManualIssueBlock = 'reason' | 'noteTooLong' | null

export function manualIssueBlock(reasonCode: string | null, note: string): ManualIssueBlock {
  if (!reasonCode?.trim()) return 'reason'
  if (noteTooLong(note)) return 'noteTooLong'
  return null
}

/** Cuerpo de POST /api/v1/manual-issues (ManualIssueCreateRequest): el de la recolección más el motivo y la nota (vacía → null). */
export function buildManualIssueBody(warehousePublicId: string, reasonCode: string, note: string, lines: readonly ResolvedPickLine[]) {
  const trimmed = note.trim()
  return {
    warehousePublicId,
    lines: lines.map((l) => ({ productPublicId: l.productPublicId, quantity: l.quantity, binId: l.fromBinId })),
    reasonCode,
    note: trimmed ? trimmed : null,
  }
}

/** El número DMA de lo que contestó el servidor (PickBatchDto); null si no viene. */
export function manualIssueNumber(result: unknown): string | null {
  if (!result || typeof result !== 'object') return null
  const n = (result as { number?: unknown }).number
  return typeof n === 'string' && n.trim() ? n : null
}

/** Total de unidades y líneas del despacho (para la confirmación). */
export function issueTotals(lines: readonly { quantity: number }[]): { lines: number; qty: number } {
  return { lines: lines.length, qty: Math.round(lines.reduce((s, l) => s + l.quantity, 0) * 1000) / 1000 }
}
