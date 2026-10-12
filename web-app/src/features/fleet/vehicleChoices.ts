// Piezas (sin componentes) que comparten las pestañas de Mantenimiento, Órdenes de trabajo y Combustible (Lote F-D3): la lista de vehículos activos como
// opciones de un select (y para escribir la placa en las tablas, que solo traen el código del vehículo) y la celda «placa + código».
import { useMemo } from 'react'
import { useVehicleList, type VehicleListItem } from './api'

const NO_VEHICLES: VehicleListItem[] = []

export interface VehicleChoices {
  /** true mientras llega la primera lista (los formularios esperan para que el select ya traiga el vehículo elegido). */
  isPending: boolean
  vehicles: readonly VehicleListItem[]
  /** «V-01 · HGR-204» por vehículo, para un `<select>`. */
  options: { value: string; label: string }[]
  /** Placa de un vehículo por su publicId ('' si no se conoce). */
  plateOf: (publicId: string | null | undefined) => string
  find: (publicId: string | null | undefined) => VehicleListItem | undefined
}

/** Vehículos activos (comparte la caché de la pantalla: las tarjetas piden la misma lista). */
export function useVehicleChoices(): VehicleChoices {
  const list = useVehicleList({ includeInactive: false })
  const vehicles = list.data ?? NO_VEHICLES
  return useMemo(() => {
    const byId = new Map(vehicles.map((v) => [v.publicId ?? '', v]))
    return {
      isPending: list.isPending,
      vehicles,
      options: vehicles.map((v) => ({ value: v.publicId ?? '', label: [v.code, v.plateNumber].filter(Boolean).join(' · ') })),
      plateOf: (id) => (id ? (byId.get(id)?.plateNumber ?? '') : ''),
      find: (id) => (id ? byId.get(id) : undefined),
    }
  }, [vehicles, list.isPending])
}

/** Texto plano de la celda anterior (ordenar, buscar y exportar). */
export function vehicleText(plate?: string | null, code?: string | null): string {
  return plate && code ? `${plate} (${code})` : plate || code || ''
}
