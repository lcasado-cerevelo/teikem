// Lote 183 (C4) — reglas puras de «Recibir juntos»: qué avisos se pueden elegir juntos (mismo dueño y mismo almacén), el mínimo y el cuerpo de la solicitud. El servidor repite las reglas.
import type { AsnDto } from './api'

export const MIN_GROUP_DOCUMENTS = 2
export const MAX_GROUP_DOCUMENTS = 20

/** El dueño de lo que llega: el cliente del aviso, o «propio» (el aviso nació de una orden de compra). */
export const asnOwnerKey = (a: AsnDto): string => a.clientPublicId ?? 'OWN'

/** Solo se recibe un aviso esperando, vigente y sin recibo. */
export const isReceivable = (a: AsnDto): boolean => a.isActive !== false && a.statusCode === 'EXPECTED' && !a.receiptPublicId

/** Por qué un aviso no se puede sumar a lo ya elegido: otro dueño u otro almacén (null = se puede). El primero elegido fija el dueño y el almacén. */
export function selectionBlock(a: AsnDto, chosen: readonly AsnDto[]): 'otherOwner' | 'otherWarehouse' | 'full' | null {
  if (chosen.some((c) => c.id === a.id)) return null
  if (chosen.length >= MAX_GROUP_DOCUMENTS) return 'full'
  const first = chosen[0]
  if (!first) return null
  if (asnOwnerKey(a) !== asnOwnerKey(first)) return 'otherOwner'
  if (a.warehousePublicId !== first.warehousePublicId) return 'otherWarehouse'
  return null
}

export interface ReceiptGroupForm {
  carrier: string
  reference: string
  note: string
}

/** El cuerpo de `POST /receipts/groups`: los avisos elegidos y los textos compartidos (vacío = sin dato). */
export function groupRequest(chosen: readonly AsnDto[], form: ReceiptGroupForm) {
  const text = (v: string) => (v.trim() === '' ? undefined : v.trim())
  return { documents: chosen.map((a) => ({ asnId: a.id ?? 0 })), carrier: text(form.carrier), reference: text(form.reference), note: text(form.note) }
}
