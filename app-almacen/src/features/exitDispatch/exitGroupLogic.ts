// Lote 178 — «Recolectar juntos» en el aparato: varios avisos de salida DEL MISMO cliente se recolectan en una sola ruta guiada (las tareas de todos juntas, en el orden en que se eligieron) y se
// mandan como UNA recolección maestra (POST /pick-batches/groups): cada aviso hace su despacho de siempre y el servidor los reúne bajo un número. Las claves de las tareas empiezan por el id
// de la línea (único), así que las tareas de varios avisos conviven sin chocar. Lógica pura: pruebas en exitGroupLogic.test.ts.
import type { SalesPick, SalesTask } from '../salesDispatch/salesDispatchLogic'
import { buildDispatchLines } from '../salesDispatch/salesDispatchLogic'

export const MIN_GROUP = 2
export const MAX_GROUP = 20

/** Lote 186: una recolección maestra reúne avisos de salida y órdenes de venta del mismo cliente. */
export type GroupKind = 'EXIT_NOTICE' | 'SALES_ORDER'

export interface GroupableNotice {
  id: number
  publicId: string
  number: string
  clientName: string
  /** Ausente = aviso de salida. */
  kind?: GroupKind
}

/**
 * Clave numérica del documento dentro del grupo: el id del aviso, o el id de la orden en negativo (los ids de avisos y de órdenes pueden coincidir). Las líneas de las órdenes también viajan con
 * su id en negativo dentro de la ruta guiada (`realLineId` lo devuelve al del servidor).
 */
export function docKey(d: Pick<GroupableNotice, 'id' | 'kind'>): number {
  return d.kind === 'SALES_ORDER' ? -d.id : d.id
}

export function realLineId(lineId: number): number {
  return Math.abs(lineId)
}

/** Por qué un aviso no se puede elegir ahora (null = se puede): otro cliente que el ya elegido (una maestra es de un solo cliente y destino) o el máximo. */
export function groupBlock(notice: GroupableNotice, chosen: readonly GroupableNotice[]): 'otherClient' | 'max' | null {
  if (chosen.some((c) => docKey(c) === docKey(notice))) return null
  if (chosen.length >= MAX_GROUP) return 'max'
  if (chosen.length > 0 && chosen[0].clientName !== notice.clientName) return 'otherClient'
  return null
}

/** Las líneas del despacho de cada aviso (solo los que tienen algo recogido), con el id de su línea ya en el cuerpo. */
export function buildGroupBody(
  notices: readonly GroupableNotice[],
  tasks: readonly SalesTask[],
  picks: readonly SalesPick[],
  lineNotice: ReadonlyMap<number, number>, // línea (en la ruta) → docKey de su documento
  warehousePublicId: string,
) {
  const lines = buildDispatchLines(tasks, picks)
  const documents = notices
    .map((n) => ({
      kind: (n.kind ?? 'EXIT_NOTICE') as GroupKind,
      publicId: n.publicId,
      lines: lines.filter((l) => lineNotice.get(l.lineId) === docKey(n)).map((l) => ({ lineId: realLineId(l.lineId), quantity: l.quantity, binId: l.binId, lotId: l.lotId })),
    }))
    .filter((d) => d.lines.length > 0)
  return { warehousePublicId, documents }
}

/** ¿Alcanza para mandar? Hacen falta al menos dos avisos con algo recogido (si no, es un despacho normal). */
export function groupReady(body: { documents: readonly unknown[] }): boolean {
  return body.documents.length >= MIN_GROUP
}

/** Número de la recolección maestra (EMP-#####) de la respuesta enviada, o null. */
export function groupNumber(result: unknown): string | null {
  const n = (result as { number?: unknown } | null | undefined)?.number
  return typeof n === 'string' && n.trim() ? n : null
}
