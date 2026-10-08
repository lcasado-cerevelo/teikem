// 2026-10-07 — varios almacenes por aparato y compañía. La web fija un almacén «por defecto» al registrar el aparato (heartbeat);
// aquí el usuario puede elegir OTRO almacén de la misma compañía para trabajar, sin tocar la web. Todo el trabajo del aparato
// (Recibir, Acomodar, Despacho, Conteo, Consultar y la bajada de posiciones) usa el almacén ACTIVO: el elegido y, si no hay
// elección, el por defecto. La lista de almacenes se guarda en la base de la compañía para poder elegir también sin señal.
import { useSyncExternalStore } from 'react'

import { api, unwrap } from '../api/client'
import { getKv, KvKeys, setKv } from '../db/kv'
import { getSessionState, subscribeSession, updateDeviceIdentity, type DeviceIdentity } from '../auth/session'

export interface WarehouseOption {
  publicId: string
  code: string
  name: string
  receivingMode: string | null
}

export interface ActiveWarehouse {
  publicId: string | null
  /** Nombre para mostrar; null mientras no se haya bajado la lista de almacenes. */
  name: string | null
  /** 'PUTAWAY' | 'DIRECT' | null (sin dato: se trata como «con acomodo»). */
  receivingMode: string | null
  /** true si el usuario lo eligió en el aparato (no es el por defecto de la web). */
  isSelected: boolean
}

/** Lista guardada (lectura sin red). */
export function getCachedWarehouseOptions(): WarehouseOption[] {
  try {
    const raw = getKv(KvKeys.warehouseOptions)
    return raw ? (JSON.parse(raw) as WarehouseOption[]) : []
  } catch {
    return []
  }
}

/** El almacén con el que trabaja el aparato ahora: el elegido, o si no, el por defecto. Función pura sobre la identidad. */
export function resolveActiveWarehouse(device: DeviceIdentity | null, options: WarehouseOption[] = []): ActiveWarehouse {
  if (!device) return { publicId: null, name: null, receivingMode: null, isSelected: false }
  const sel = device.selectedWarehouse
  if (sel) return { publicId: sel.publicId, name: sel.name, receivingMode: sel.receivingMode, isSelected: true }
  const id = device.defaultWarehousePublicId
  const known = id ? options.find((o) => o.publicId === id) : undefined
  return { publicId: id, name: known?.name ?? null, receivingMode: device.defaultWarehouseReceivingMode ?? known?.receivingMode ?? null, isSelected: false }
}

/** Id del almacén activo del aparato (null = ninguno). Para código fuera de React (descargas). */
export function getActiveWarehousePublicId(): string | null {
  return resolveActiveWarehouse(getSessionState().device).publicId
}

/** Hook: el almacén activo; se vuelve a pintar al elegir otro o cuando la web cambia el por defecto. */
export function useActiveWarehouse(): ActiveWarehouse {
  const state = useSyncExternalStore(subscribeSession, getSessionState, getSessionState)
  return resolveActiveWarehouse(state.device, getCachedWarehouseOptions())
}

/** Baja los almacenes activos de la compañía y los guarda. Sin permiso (403) o sin red devuelve lo guardado. Si el almacén elegido
 *  ya no existe o está inactivo, se vuelve al por defecto. */
export async function refreshWarehouseOptions(): Promise<WarehouseOption[]> {
  try {
    const rows = await unwrap(api.GET('/api/v1/warehouses', { params: { query: { includeInactive: false } } }))
    const options = rows
      .filter((w) => w.isActive !== false && w.publicId)
      .map<WarehouseOption>((w) => ({ publicId: w.publicId!, code: w.code ?? '', name: w.name ?? w.code ?? '', receivingMode: w.receivingModeCode ?? null }))
      .sort((a, b) => a.name.localeCompare(b.name))
    setKv(KvKeys.warehouseOptions, JSON.stringify(options))
    const sel = getSessionState().device?.selectedWarehouse
    if (sel) {
      const still = options.find((o) => o.publicId === sel.publicId)
      if (!still) await updateDeviceIdentity({ selectedWarehouse: null })
      else if (still.name !== sel.name || still.receivingMode !== sel.receivingMode)
        await updateDeviceIdentity({ selectedWarehouse: { publicId: still.publicId, name: still.name, receivingMode: still.receivingMode } })
    }
    return options
  } catch {
    return getCachedWarehouseOptions()
  }
}

/** Elige con qué almacén trabaja este aparato. Elegir el por defecto quita la elección. */
export async function selectActiveWarehouse(option: WarehouseOption): Promise<void> {
  const device = getSessionState().device
  if (!device) return
  if (option.publicId === device.defaultWarehousePublicId) await updateDeviceIdentity({ selectedWarehouse: null })
  else await updateDeviceIdentity({ selectedWarehouse: { publicId: option.publicId, name: option.name, receivingMode: option.receivingMode } })
}
