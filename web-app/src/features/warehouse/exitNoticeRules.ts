// Lote 144 — reglas puras del aviso de salida en la pantalla (réplica de lo que valida el servidor, `ExitNoticeRules`; el servidor vuelve a validar todo). Mensajes: claves i18n.
import type { ExitNoticeDto, ExitNoticeLineDto } from './exitNoticeApi'

/** Tope de líneas de un aviso (ExitNoticeRules.MaxLines). */
export const EXIT_NOTICE_MAX_LINES = 200
export const EXIT_NOTICE_STATUSES = ['REQUESTED', 'PARTIAL', 'SHIPPED', 'CANCELLED'] as const

export interface ExitLineDraft {
  productPublicId: string | null
  quantity: number | null
}

/** Clave i18n del error de una cantidad (null = válida): mayor que cero y hasta 3 decimales. */
export function exitQuantityIssue(qty: number | null | undefined): string | null {
  if (qty == null || Number.isNaN(qty) || qty <= 0) return 'warehouse.exitNotices.errors.quantityRequired'
  if (qty >= 99_999_999) return 'warehouse.exitNotices.errors.quantityTooLarge'
  if (Math.round(qty * 1000) / 1000 !== qty) return 'warehouse.exitNotices.errors.quantityDecimals'
  return null
}

/** Líneas con algo pendiente que todavía espera inventario (no cerradas con faltante). */
export function pendingLines(lines: readonly ExitNoticeLineDto[] | null | undefined): ExitNoticeLineDto[] {
  return (lines ?? []).filter((l) => (l.qtyBackordered ?? 0) > 0 && !l.isClosed)
}

/** ¿Se puede cambiar la cantidad de esta línea? Aviso solicitado (`canModify`), línea abierta y sin nada despachado. */
export function canModifyLine(notice: Pick<ExitNoticeDto, 'canModify'>, line: ExitNoticeLineDto): boolean {
  return notice.canModify === true && !line.isClosed && (line.qtyShipped ?? 0) <= 0
}

/** ¿Se puede atender el pendiente (reservar o cerrar con faltante)? Aviso solicitado o parcial y línea con pendiente abierta. */
export function canHandlePending(notice: Pick<ExitNoticeDto, 'statusCode'>, line: ExitNoticeLineDto): boolean {
  return (notice.statusCode === 'REQUESTED' || notice.statusCode === 'PARTIAL') && (line.qtyBackordered ?? 0) > 0 && !line.isClosed
}

/** Cuánto falta por despachar de una línea: reservado + pendiente (una línea cerrada ya no espera lo pendiente). */
export function toShip(line: ExitNoticeLineDto): number {
  return (line.qtyReserved ?? 0) + (line.isClosed ? 0 : (line.qtyBackordered ?? 0))
}
