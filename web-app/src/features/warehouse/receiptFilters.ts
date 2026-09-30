// Lote 13 — Recibo (maestro-detalle): lógica pura de la lista de recibos que comparten las pestañas Recibos y 'Acomodo
// pendiente'. Estado de los filtros (`ReceiptFilterState`) → consulta de `GET /api/v1/receipts` (`receiptListQuery`: los
// filtros van todos al API, con `variance[]` SHORT/OVER/NONE y `phase` OPEN/PENDING_PUTAWAY/DONE), el recibo elegido
// (`?receipt=` o el primero: `selectedReceiptId`), el recibo recién creado arriba de todo aunque los filtros lo excluyan
// (`withPinned`) y el origen legible (Orden de compra / Cliente / Ciego / Devolución). Pruebas en receiptFilters.test.ts.
import type { DateRange } from '../../kernel/ui/dateRange'
import type { GetQuery, ReceiptListItemDto } from './api'
import type { ProductFilterItem } from './pickers'

/** Dominio de estatus del recibo (StatusChip, filtro Estatus, historial). */
export const RECEIPT_STATUS_DOMAIN = 'ReceiptStatus'

export type ReceiptPhase = 'OPEN' | 'PENDING_PUTAWAY' | 'DONE'

/** Filtro Diferencia (decisión 2): Faltante / Sobrante / Sin diferencia. */
export const VARIANCE_CODES = ['SHORT', 'OVER', 'NONE'] as const
export type VarianceCode = (typeof VARIANCE_CODES)[number]

/** Estatus de la pestaña 'Acomodo pendiente': confirmados con tareas de acomodo por cerrar. */
export const PUTAWAY_PENDING_STATUSES = ['RECEIVED', 'RECEIVED_VARIANCE'] as const

/** Origen de un recibo (`ReceiptListItemDto.origin`): PO y ASN traen documento (lo esperado viene de él y queda fijo). */
export type ReceiptOrigin = 'PO' | 'ASN' | 'BLIND' | 'RETURN'
const DOCUMENT_ORIGINS: readonly string[] = ['PO', 'ASN']

export interface ReceiptFilterState {
  warehousePublicId: string | null
  status: string[]
  types: string[]
  created: DateRange
  products: ProductFilterItem[]
  variance: string[]
}

export const EMPTY_RECEIPT_FILTERS: ReceiptFilterState = {
  warehousePublicId: null,
  status: [],
  types: [],
  created: { from: '', to: '' },
  products: [],
  variance: [],
}

export interface ReceiptListPaging {
  /** Fase del API (pestaña 'Acomodo pendiente' = PENDING_PUTAWAY); sin ella, todas. */
  phase?: ReceiptPhase
  /** Texto libre del buscador de la lista (ya con su pausa). */
  search?: string
  page: number
  pageSize: number
}

/** Consulta de `GET /api/v1/receipts` con los filtros de la pantalla (vacío = sin el parámetro). */
export function receiptListQuery(f: ReceiptFilterState, paging: ReceiptListPaging): GetQuery<'/api/v1/receipts'> {
  const q: GetQuery<'/api/v1/receipts'> = {}
  if (f.warehousePublicId) q.warehousePublicId = f.warehousePublicId
  if (f.status.length > 0) q.status = [...f.status]
  if (f.types.length > 0) q.types = [...f.types]
  if (f.created.from) q.from = f.created.from
  if (f.created.to) q.to = f.created.to
  if (f.products.length > 0) q.productPublicIds = f.products.map((p) => p.publicId)
  const variance = f.variance.filter((v) => (VARIANCE_CODES as readonly string[]).includes(v))
  if (variance.length > 0) q.variance = variance
  if (paging.phase) q.phase = paging.phase
  const search = paging.search?.trim()
  if (search) q.search = search
  const size = Math.max(1, paging.pageSize)
  q.skip = (Math.max(1, paging.page) - 1) * size
  q.take = size
  return q
}

/** ¿Hay algún filtro puesto? (el botón Limpiar y quitar el recibo fijado). */
export function hasReceiptFilters(f: ReceiptFilterState): boolean {
  return (
    Boolean(f.warehousePublicId) ||
    f.status.length > 0 ||
    f.types.length > 0 ||
    Boolean(f.created.from) ||
    Boolean(f.created.to) ||
    f.products.length > 0 ||
    f.variance.length > 0
  )
}

/**
 * Lista con el recibo fijado primero (el que se acaba de crear): si viene en la página se mueve arriba (con los datos de la
 * página, más frescos); si los filtros lo excluyen, se antepone el que se tiene. Sin fijado, la lista tal cual.
 */
export function withPinned<T extends { publicId?: string | null }>(items: readonly T[], pinned: T | null | undefined): T[] {
  if (!pinned?.publicId) return [...items]
  const inPage = items.find((r) => r.publicId === pinned.publicId)
  return [inPage ?? pinned, ...items.filter((r) => r.publicId !== pinned.publicId)]
}

/** Recibo elegido: el de `?receipt=` (aunque no esté en la página: llega de un enlace); si no, el primero de la lista. */
export function selectedReceiptId(items: readonly { publicId?: string | null }[], param: string | null | undefined): string | null {
  if (param) return param
  return items[0]?.publicId ?? null
}

/** ¿El recibo nació de un aviso de llegada o de una orden de compra? (esperado fijo, sin "Añadir ítem"; decisión 6). */
export function hasDocument(origin: string | null | undefined): boolean {
  return DOCUMENT_ORIGINS.includes((origin ?? '').toUpperCase())
}

/** Origen normalizado para su etiqueta (`warehouse.receipts.originLong.<origen>`); desconocido = Ciego. */
export function receiptOrigin(origin: string | null | undefined): ReceiptOrigin {
  const o = (origin ?? '').toUpperCase()
  return o === 'PO' || o === 'ASN' || o === 'RETURN' ? o : 'BLIND'
}

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Texto de la maqueta a la derecha del título del detalle: "Origen · Orden de compra: PO-…", "Origen · Cliente: Nombre ·
 *  referencia del aviso", "Origen · Ciego" / "Origen · Devolución". */
export function receiptOriginText(
  h: Pick<ReceiptListItemDto, 'origin' | 'originRef' | 'senderName'> | null | undefined,
  t: Translate,
): string {
  const origin = receiptOrigin(h?.origin)
  const label = t(`warehouse.receipts.originLong.${origin}`)
  const ref = origin === 'PO' ? h?.originRef : origin === 'ASN' ? [h?.senderName, h?.originRef].filter(Boolean).join(' · ') : null
  return ref ? t('warehouse.receipts.detail.originWithRef', { origin: label, ref }) : t('warehouse.receipts.detail.origin', { origin: label })
}

/** Remitente de la fila de la lista: proveedor (OC) o cliente (aviso); en ciegos y devoluciones, el tipo. */
export function receiptSender(r: Pick<ReceiptListItemDto, 'senderName' | 'type' | 'typeCode'>): string {
  return r.senderName?.trim() || r.type || r.typeCode || ''
}
