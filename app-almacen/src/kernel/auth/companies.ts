// 2026-10-07 — quitar una compañía de este teléfono: borra su registro y su base local (con lo que tuviera sin enviar).
// La base común (`teikem_almacen.db`, registro de antes sin `dbName`) tiene además configuración del teléfono: esa no se borra.
import { countPendingIn, deleteLocalDatabase } from '../db/database'
import { type DeviceIdentity, removeDeviceIdentity } from './session'

/** Capturas de esa compañía que aún no se enviaron (para avisar antes de quitarla). */
export function pendingForCompany(device: DeviceIdentity): number {
  return device.dbName ? countPendingIn(device.dbName) : 0
}

export async function removeCompany(device: DeviceIdentity): Promise<void> {
  await removeDeviceIdentity(device.devicePublicId)
  if (device.dbName) deleteLocalDatabase(device.dbName)
}
