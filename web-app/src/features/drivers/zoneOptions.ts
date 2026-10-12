// Opciones del select de zona de despacho: zonas activas «código · nombre» y, si el chofer tiene una que ya no está en la lista
// (inactiva), esa también, para que el select no quede vacío ni la edición borre la zona sin querer.
import type { DispatchZone } from './api'

export function zoneLabel(z: { code?: string | null; name?: string | null }): string {
  return [z.code, z.name].filter(Boolean).join(' · ')
}

export function zoneOptions(
  zones: readonly DispatchZone[] | undefined,
  current?: { id?: number | null; code?: string | null; name?: string | null } | null,
): { value: string; label: string }[] {
  const list = (zones ?? []).filter((z) => z.isActive).map((z) => ({ value: String(z.id), label: zoneLabel(z) }))
  if (current?.id != null && !list.some((o) => o.value === String(current.id))) list.push({ value: String(current.id), label: zoneLabel(current) })
  return list
}
