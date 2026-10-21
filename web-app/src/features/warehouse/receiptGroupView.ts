// Lote 183 (C4) — reglas puras de «Recibir juntos»: qué documentos se pueden elegir juntos (mismo dueño y mismo almacén), el mínimo y el cuerpo de la solicitud. El servidor repite las reglas.
// Un documento es un aviso de llegada (de un cliente o nacido de una orden de compra) o una orden de compra (lo propio de la compañía).
import type { AsnDto } from './api'

export const MIN_GROUP_DOCUMENTS = 2
export const MAX_GROUP_DOCUMENTS = 20

export interface GroupCandidate {
  key: string
  kind: 'ASN' | 'PO'
  asnId?: number
  purchaseOrderPublicId?: string
  number: string
  /** El dueño: el cliente, o 'OWN' (lo propio de la compañía). */
  ownerKey: string
  ownerName: string | null
  warehousePublicId: string
  warehouseCode: string
  reference: string | null
}

/** Solo se recibe un aviso esperando, vigente y sin recibo. */
export const isReceivable = (a: AsnDto): boolean => a.isActive !== false && a.statusCode === 'EXPECTED' && !a.receiptPublicId

/** Una orden de compra se recibe enviada o con recibido parcial. */
export const isPoReceivable = (statusCode: string | null | undefined): boolean => statusCode === 'SENT' || statusCode === 'PARTIAL'

export const asnCandidate = (a: AsnDto): GroupCandidate => ({
  key: `A${a.id}`,
  kind: 'ASN',
  asnId: a.id ?? 0,
  number: a.number ?? '',
  ownerKey: a.clientPublicId ?? 'OWN',
  ownerName: a.clientName ?? null,
  warehousePublicId: a.warehousePublicId ?? '',
  warehouseCode: a.warehouseCode ?? '',
  reference: a.reference ?? null,
})

export const poCandidate = (p: { publicId?: string; number?: string; supplierName?: string; warehousePublicId?: string; warehouseCode?: string }): GroupCandidate => ({
  key: `P${p.publicId}`,
  kind: 'PO',
  purchaseOrderPublicId: p.publicId ?? '',
  number: p.number ?? '',
  ownerKey: 'OWN',
  ownerName: null,
  warehousePublicId: p.warehousePublicId ?? '',
  warehouseCode: p.warehouseCode ?? '',
  reference: p.supplierName ?? null,
})

/** Por qué un documento no se puede sumar a lo ya elegido: otro dueño u otro almacén (null = se puede). El primero elegido fija el dueño y el almacén. */
export function selectionBlock(c: GroupCandidate, chosen: readonly GroupCandidate[]): 'otherOwner' | 'otherWarehouse' | 'full' | null {
  if (chosen.some((x) => x.key === c.key)) return null
  if (chosen.length >= MAX_GROUP_DOCUMENTS) return 'full'
  const first = chosen[0]
  if (!first) return null
  if (c.ownerKey !== first.ownerKey) return 'otherOwner'
  if (c.warehousePublicId !== first.warehousePublicId) return 'otherWarehouse'
  return null
}

export interface ReceiptGroupForm {
  carrier: string
  reference: string
  note: string
}

/** El cuerpo de `POST /receipts/groups`: los documentos elegidos y los textos compartidos (vacío = sin dato). */
export function groupRequest(chosen: readonly GroupCandidate[], form: ReceiptGroupForm) {
  const text = (v: string) => (v.trim() === '' ? undefined : v.trim())
  return {
    documents: chosen.map((c) => (c.kind === 'ASN' ? { asnId: c.asnId } : { purchaseOrderPublicId: c.purchaseOrderPublicId })),
    carrier: text(form.carrier),
    reference: text(form.reference),
    note: text(form.note),
  }
}
