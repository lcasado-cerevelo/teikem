// Reglas puras de «Contabilización de compras / de despachos» (Lote 181, H1): qué admite cada estatus, los permisos de cada tipo y la validación del período. El servidor repite las reglas.
export type AccountingKind = 'PURCHASES' | 'DISPATCHES'

export const ACCOUNTING_STATUSES = ['GENERATED', 'ACCEPTED', 'EXPORTED', 'CANCELLED'] as const

/** Por tipo: el permiso de ver, el de generar/aceptar/cancelar, el de exportar (y configurar plantillas) y el alcance de las plantillas de exportación. */
export const ACCOUNTING_KINDS: Record<AccountingKind, { view: string; manage: string; export: string; scope: string; key: string }> = {
  PURCHASES: { view: 'purchasing.view', manage: 'purchasing.manage', export: 'purchasing.manage', scope: 'PURCHASE_ACCOUNTING', key: 'purchases' },
  DISPATCHES: { view: 'billing.view', manage: 'billing.generate', export: 'billing.export', scope: 'DISPATCH_ACCOUNTING', key: 'dispatches' },
}

/** Solo una generada se acepta. */
export function canAccept(status: string | null | undefined): boolean {
  return status === 'GENERATED'
}

/** Se cancela una generada o aceptada (una exportada ya no). */
export function canCancel(status: string | null | undefined): boolean {
  return status === 'GENERATED' || status === 'ACCEPTED'
}

/** Se exporta una aceptada o exportada (se puede repetir con otra plantilla). */
export function canExport(status: string | null | undefined): boolean {
  return status === 'ACCEPTED' || status === 'EXPORTED'
}

export type NewRunError = 'period' | 'order'

/** Fechas «AAAA-MM-DD» (lo que dan los campos de fecha). */
export function newRunError(from: string, to: string): NewRunError | null {
  if (!from || !to) return 'period'
  if (to < from) return 'order'
  return null
}

/** El período por omisión: los últimos 30 días hasta hoy. */
export function defaultPeriod(today: Date): { from: string; to: string } {
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const start = new Date(today)
  start.setDate(start.getDate() - 30)
  return { from: iso(start), to: iso(today) }
}

/** Cuántas líneas de la vista previa se pintan (el total y el conteo son de todas). */
export const PREVIEW_LINES_SHOWN = 200
