// Datos de Flota (Lote F-D2, manual 04 §1 vehículos, §2.7 zonas, §3 documentos por vencer y §4 disponibilidad). Módulo CATALOG.
// Permisos: ver `fleet.view`; escribir vehículos, documentos y zonas `fleet.manage`. Lecturas con `useQuery` (clave `[ruta, params]`),
// escrituras con `useMutation`; las que devuelven la ficha del vehículo la dejan en caché.
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components, paths } from '../../kernel/api/schema'
import { catalogKeys } from '../../kernel/catalogs'
import { driverKeys } from '../drivers/api'

type Schemas = components['schemas']

export type VehicleListItem = Schemas['VehicleListItemDto']
export type VehicleDetail = Schemas['VehicleDetailDto']
export type VehicleCreateRequest = Schemas['VehicleCreateRequest']
export type VehiclePatchRequest = Schemas['VehiclePatchRequest']
export type VehicleDocument = Schemas['VehicleDocumentDto']
export type VehicleDocumentRequest = Schemas['VehicleDocumentRequest']
export type ExpiringDocument = Schemas['ExpiringDocumentDto']
export type FleetAvailability = Schemas['FleetAvailabilityDto']
export type DriverAvailability = Schemas['DriverAvailabilityDto']
export type VehicleAvailability = Schemas['VehicleAvailabilityDto']
export type AvailabilityIssue = Schemas['AvailabilityIssueDto']
export type DispatchZoneRequest = Schemas['DispatchZoneRequest']
export type VehicleListQuery = NonNullable<paths['/api/v1/vehicles']['get']['parameters']['query']>
export type ExpiringQuery = NonNullable<paths['/api/v1/fleet/expiring-documents']['get']['parameters']['query']>

export const VEHICLE_ENTITY_TYPE = 'VEHICLE'

export const fleetKeys = {
  /** Prefijo de todas las listas de vehículos (también la de las tarjetas resumen). */
  vehicles: ['/api/v1/vehicles'] as const,
  vehiclesOf: (query: VehicleListQuery) => ['/api/v1/vehicles', query] as const,
  detail: (publicId: string) => ['/api/v1/vehicles/{publicId}', publicId] as const,
  expiring: ['/api/v1/fleet/expiring-documents'] as const,
  expiringOf: (query: ExpiringQuery) => ['/api/v1/fleet/expiring-documents', query] as const,
  availability: ['/api/v1/fleet/availability'] as const,
  availabilityOf: (date: string, onlyAvailable: boolean) => ['/api/v1/fleet/availability', { date, onlyAvailable }] as const,
}

// ---- Lecturas ----
/** Lista de vehículos (no pagina: el API devuelve todos, ordenados por código). Se queda con la anterior mientras llega la nueva. */
export function useVehicleList(query: VehicleListQuery) {
  return useQuery({
    queryKey: fleetKeys.vehiclesOf(query),
    queryFn: () => unwrap(api.GET('/api/v1/vehicles', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useVehicle(publicId: string | null) {
  return useQuery({
    queryKey: fleetKeys.detail(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/vehicles/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
  })
}

/** Documentos por vencer (vehículos, licencias y certificaciones). */
export function useExpiringDocuments(query: ExpiringQuery) {
  return useQuery({
    queryKey: fleetKeys.expiringOf(query),
    queryFn: () => unwrap(api.GET('/api/v1/fleet/expiring-documents', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

/** Disponibilidad para despacho de choferes y vehículos en una fecha. */
export function useFleetAvailability(date: string, onlyAvailable: boolean) {
  return useQuery({
    queryKey: fleetKeys.availabilityOf(date, onlyAvailable),
    queryFn: () => unwrap(api.GET('/api/v1/fleet/availability', { params: { query: { ...(date ? { date } : {}), onlyAvailable } } })),
    placeholderData: keepPreviousData,
  })
}

// ---- Escrituras del vehículo ----
/** Lo que cambia cuando se toca un vehículo o sus documentos: listas, tarjetas, documentos por vencer y disponibilidad. */
export function invalidateFleet(qc: QueryClient) {
  void qc.invalidateQueries({ queryKey: fleetKeys.vehicles })
  void qc.invalidateQueries({ queryKey: fleetKeys.expiring })
  void qc.invalidateQueries({ queryKey: fleetKeys.availability })
}

/** Deja la ficha devuelta en caché y refresca lo demás. */
function settleVehicle(qc: QueryClient, dto: VehicleDetail) {
  if (dto.publicId) qc.setQueryData(fleetKeys.detail(dto.publicId), dto)
  invalidateFleet(qc)
}

export function useCreateVehicle() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: VehicleCreateRequest) => unwrap(api.POST('/api/v1/vehicles', { body })),
    onSuccess: (v) => {
      settleVehicle(qc, v)
      if (v.id) void qc.invalidateQueries({ queryKey: catalogKeys.history(VEHICLE_ENTITY_TYPE, v.id) })
    },
  })
}

export function useUpdateVehicle(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: VehiclePatchRequest) => unwrap(api.PATCH('/api/v1/vehicles/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: (v) => settleVehicle(qc, v),
  })
}

/** `POST /vehicles/{id}/status`: ACTIVE ↔ MAINTENANCE (lateral) o INACTIVE (baja definitiva, terminal). */
export function useVehicleStatusChange(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ toCode, comment }: { toCode: string; comment?: string }) =>
      unwrap(api.POST('/api/v1/vehicles/{publicId}/status', { params: { path: { publicId } }, body: { toCode, comment: comment || null } })),
    onSuccess: (v) => {
      settleVehicle(qc, v)
      if (v.id) void qc.invalidateQueries({ queryKey: catalogKeys.history(VEHICLE_ENTITY_TYPE, v.id) })
    },
  })
}

/** Casilla «Activo»: `deactivate` / `reactivate` (204). */
export function useSetVehicleActive(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (active: boolean) => {
      if (active) await unwrap(api.POST('/api/v1/vehicles/{publicId}/reactivate', { params: { path: { publicId } } }))
      else await unwrap(api.POST('/api/v1/vehicles/{publicId}/deactivate', { params: { path: { publicId } } }))
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: fleetKeys.detail(publicId) })
      invalidateFleet(qc)
    },
  })
}

// ---- Documentos del vehículo ----
export function useVehicleDocumentMutations(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = () => {
    void qc.invalidateQueries({ queryKey: fleetKeys.detail(publicId) })
    invalidateFleet(qc)
  }
  return {
    add: useMutation({ mutationFn: (body: VehicleDocumentRequest) => unwrap(api.POST('/api/v1/vehicles/{publicId}/documents', { params: { path: { publicId } }, body })), onSuccess }),
    update: useMutation({
      mutationFn: (v: { id: number; body: VehicleDocumentRequest }) =>
        unwrap(api.PATCH('/api/v1/vehicles/{publicId}/documents/{id}', { params: { path: { publicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/vehicles/{publicId}/documents/{id}/deactivate', { params: { path: { publicId, id } } })),
      onSuccess,
    }),
  }
}

// ---- Zonas de despacho ----
/** Alta, cambio de nombre, inactivar y reactivar. La lista de choferes y sus filtros muestran el nombre de la zona: también se refrescan. */
export function useZoneMutations() {
  const qc = useQueryClient()
  const onSuccess = () => {
    void qc.invalidateQueries({ queryKey: driverKeys.zones })
    void qc.invalidateQueries({ queryKey: driverKeys.list })
  }
  return {
    create: useMutation({ mutationFn: (body: DispatchZoneRequest) => unwrap(api.POST('/api/v1/dispatch-zones', { body })), onSuccess }),
    rename: useMutation({
      mutationFn: (v: { id: number; name: string }) => unwrap(api.PATCH('/api/v1/dispatch-zones/{id}', { params: { path: { id: v.id } }, body: { name: v.name } })),
      onSuccess,
    }),
    /** Lote 129: el nivel de la zona (null = sin nivel). */
    setLevel: useMutation({
      mutationFn: (v: { id: number; zoneGroupId: number | null }) =>
        unwrap(api.PATCH('/api/v1/dispatch-zones/{id}', { params: { path: { id: v.id } }, body: v.zoneGroupId === null ? { clearZoneGroup: true } : { zoneGroupId: v.zoneGroupId } })),
      onSuccess,
    }),
    setActive: useMutation({
      mutationFn: (v: { id: number; active: boolean }) =>
        v.active
          ? unwrap(api.POST('/api/v1/dispatch-zones/{id}/reactivate', { params: { path: { id: v.id } } }))
          : unwrap(api.POST('/api/v1/dispatch-zones/{id}/deactivate', { params: { path: { id: v.id } } })),
      onSuccess,
    }),
  }
}

// ---------------------------------------------------------------- Lote 129: niveles de zona (Isla/Metro…)
export type ZoneGroup = Schemas['ZoneGroupDto']
export type ZoneGroupRequest = Schemas['ZoneGroupRequest']
export const zoneGroupKeys = { all: ['zone-groups'] as const, list: (includeInactive: boolean) => ['zone-groups', { includeInactive }] as const }

/** `GET /zone-groups` (fleet.view). */
export function useZoneGroups(includeInactive = false, enabled = true) {
  return useQuery({ queryKey: zoneGroupKeys.list(includeInactive), queryFn: () => unwrap(api.GET('/api/v1/zone-groups', { params: { query: { includeInactive } } })), enabled })
}

/** Alta, edición, baja y reactivación de niveles (fleet.manage); al cambiar se refrescan las zonas (traen su nivel). */
export function useZoneGroupMutations() {
  const qc = useQueryClient()
  const onSuccess = () => {
    void qc.invalidateQueries({ queryKey: zoneGroupKeys.all })
    void qc.invalidateQueries({ queryKey: driverKeys.zones })
  }
  return {
    create: useMutation({ mutationFn: (body: ZoneGroupRequest) => unwrap(api.POST('/api/v1/zone-groups', { body })), onSuccess }),
    update: useMutation({ mutationFn: (v: { id: number; body: ZoneGroupRequest }) => unwrap(api.PATCH('/api/v1/zone-groups/{id}', { params: { path: { id: v.id } }, body: v.body })), onSuccess }),
    setActive: useMutation({
      mutationFn: (v: { id: number; active: boolean }) =>
        v.active ? unwrap(api.POST('/api/v1/zone-groups/{id}/reactivate', { params: { path: { id: v.id } } })) : unwrap(api.POST('/api/v1/zone-groups/{id}/deactivate', { params: { path: { id: v.id } } })),
      onSuccess,
    }),
    /** `POST /dispatch-zones/assign-orders`: pone la zona de entrega a las órdenes que no la tienen (all = a todas). */
    assignOrders: useMutation({ mutationFn: (all: boolean) => unwrap(api.POST('/api/v1/dispatch-zones/assign-orders', { params: { query: { all } } })), onSuccess: () => void qc.invalidateQueries({ queryKey: unresolvedZoneKeys.all }) }),
  }
}

export type UnresolvedZones = Schemas['UnresolvedZonesDto']
export type UnresolvedZoneRow = Schemas['UnresolvedZoneRowDto']
export const unresolvedZoneKeys = { all: ['unresolved-zones'] as const }

/** `GET /dispatch-zones/unresolved-orders` (fleet.view): por qué los envíos activos siguen sin zona (grupos por código postal y pueblo). */
export function useUnresolvedZones(enabled = true) {
  return useQuery({ queryKey: unresolvedZoneKeys.all, queryFn: () => unwrap(api.GET('/api/v1/dispatch-zones/unresolved-orders', { params: { query: { top: 200 } } })), enabled, meta: { handleAccessDenied: false } })
}

/** `POST /dispatch-zones/{id}/members` (fleet.manage): agrega un código postal o un pueblo a una zona; 409 si choca con otra zona activa. */
export function useAddZoneMember() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { zoneId: number; matchType: 'POSTAL_CODE' | 'MUNICIPALITY'; matchValue: string }) =>
      unwrap(api.POST('/api/v1/dispatch-zones/{id}/members', { params: { path: { id: v.zoneId } }, body: { matchType: v.matchType, matchValue: v.matchValue } })),
    onSuccess: () => void qc.invalidateQueries({ queryKey: unresolvedZoneKeys.all }),
  })
}
