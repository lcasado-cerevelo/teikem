// Lote F-E2a — Opciones de chofer y vehículo para los selects de la Sala de despacho, con su disponibilidad en la fecha de la ruta
// (`GET /fleet/availability`, `fleet.view` + CATALOG). Sin ese permiso la lista no se pide: la pantalla lo explica en lugar de romper.
import { useMemo } from 'react'
import type { components } from '../../kernel/api/schema'
import { useT } from '../../kernel/i18n'
import { useAvailability } from './dispatchApi'

type Schemas = components['schemas']
export type DriverAvailability = Schemas['DriverAvailabilityDto']
export type VehicleAvailability = Schemas['VehicleAvailabilityDto']

export interface Choice {
  value: string
  label: string
  available: boolean
}

export const NO_CHOICES: readonly Choice[] = []

/** «D-01 · Ana Pérez» — el servidor no manda el nombre vacío; un chofer sin código se ve por su nombre. */
export const driverLabel = (d: Pick<DriverAvailability, 'code' | 'fullName'>) => [d.code, d.fullName].filter(Boolean).join(' · ')
export const vehicleLabel = (v: Pick<VehicleAvailability, 'code' | 'plateNumber'>) => [v.code, v.plateNumber].filter(Boolean).join(' · ')

/**
 * Las opciones de un select: lo disponible y lo no disponible (marcado; elegirlo hace que el servidor conteste con su 409 exacto), y la
 * opción actual aunque ya no esté en la lista (chofer dado de baja, vehículo en otra fecha): el select nunca queda en blanco por error.
 */
export function withCurrent(choices: readonly Choice[], current: { value: string | null | undefined; label: string } | null): Choice[] {
  const list = [...choices]
  if (current?.value && !list.some((c) => c.value === current.value)) list.push({ value: current.value, label: current.label, available: true })
  return list
}

export function useFleetChoices(date: string, enabled: boolean) {
  const t = useT()
  const query = useAvailability(date, enabled)
  return useMemo(() => {
    const mark = (label: string, available: boolean | undefined) => (available === false ? `${label} — ${t('dispatch.choice.unavailable')}` : label)
    const drivers: Choice[] = (query.data?.drivers ?? []).filter((d) => d.publicId).map((d) => ({ value: d.publicId ?? '', label: mark(driverLabel(d), d.available), available: d.available !== false }))
    const vehicles: Choice[] = (query.data?.vehicles ?? []).filter((v) => v.publicId).map((v) => ({ value: v.publicId ?? '', label: mark(vehicleLabel(v), v.available), available: v.available !== false }))
    return { drivers, vehicles, availability: query.data, isPending: enabled && query.isPending, isError: query.isError }
  }, [query.data, query.isPending, query.isError, enabled, t])
}
