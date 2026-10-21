// Lote 184 — «Recibir juntos» (el camión): el operador abre un camión y recibe varios documentos del MISMO dueño, uno tras otro, como siempre; cada recibo viaja a la cola con la misma llave
// (groupKey) y el servidor los reúne en una recepción maestra. El dueño lo fija el primer documento: el cliente del aviso, o OWN (lo propio: orden de compra o aviso nacido de una). Un documento de otro
// dueño no entra al camión (el servidor también lo rechazaría, pero un envío rechazado en la cola ya no se edita: se avisa aquí).
import { getDb } from '../../kernel/db/database'
import type { LocalDoc } from './localLookup'

export interface Truck {
  groupKey: string
  warehousePublicId: string
  /** null hasta que se recibe el primer documento. */
  ownerKey: string | null
  /** Documentos ya mandados a la cola en este camión. */
  docs: number
}

export const OWN_OWNER = 'OWN'

/** Reglas puras: un documento entra al camión si el dueño aún no está fijado o es el mismo. */
export function truckBlock(truckOwnerKey: string | null, docOwnerKey: string): 'otherOwner' | null {
  return truckOwnerKey === null || truckOwnerKey === docOwnerKey ? null : 'otherOwner'
}

export function getTruck(): Truck | null {
  const r = getDb().getFirstSync<{ group_key: string; warehouse_public_id: string; owner_key: string | null; docs: number }>('SELECT group_key, warehouse_public_id, owner_key, docs FROM receive_truck WHERE id = 1')
  return r ? { groupKey: r.group_key, warehousePublicId: r.warehouse_public_id, ownerKey: r.owner_key, docs: r.docs } : null
}

/** Abre un camión nuevo (reemplaza al anterior si lo hubiera). `groupKey` es un id v4 generado por quien llama. */
export function startTruck(warehousePublicId: string, groupKey: string): void {
  getDb().runSync('INSERT OR REPLACE INTO receive_truck (id, group_key, warehouse_public_id, owner_key, docs, started_at_utc) VALUES (1, ?, ?, NULL, 0, ?)', [groupKey, warehousePublicId, new Date().toISOString()])
}

export function endTruck(): void {
  getDb().runSync('DELETE FROM receive_truck')
}

/** El dueño de un documento de la base local: el cliente del aviso (por nombre; el aviso local no guarda su id), OWN para una orden de compra o un aviso nacido de una. */
export function ownerKeyOf(doc: LocalDoc): string {
  if (doc.kind === 'po' || doc.asnId == null) return OWN_OWNER
  const a = getDb().getFirstSync<{ client_name: string | null; purchase_order_public_id: string | null }>('SELECT client_name, purchase_order_public_id FROM asn WHERE id = ?', [doc.asnId])
  if (!a || a.purchase_order_public_id) return OWN_OWNER
  return `C:${(a.client_name ?? '').trim().toLowerCase()}`
}

/** Fija el dueño del camión con el primer documento. */
export function setTruckOwner(ownerKey: string): void {
  getDb().runSync('UPDATE receive_truck SET owner_key = COALESCE(owner_key, ?) WHERE id = 1', [ownerKey])
}

/** Un documento más mandado a la cola en este camión. */
export function countTruckDoc(): void {
  getDb().runSync('UPDATE receive_truck SET docs = docs + 1 WHERE id = 1')
}

/** ¿Este documento entra al camión en curso? (sin camión: sí). */
export function truckBlockForDoc(truck: Truck | null, doc: LocalDoc): 'otherOwner' | null {
  return truck ? truckBlock(truck.ownerKey, ownerKeyOf(doc)) : null
}
