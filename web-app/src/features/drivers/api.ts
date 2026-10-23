// Datos de Choferes y tarifas (Lote F-D1, manual 04 §2 y §8-§9). Módulo CATALOG. Permisos: ver `fleet.view`; escribir chofer,
// licencias, certificaciones, dispositivos `fleet.manage` (vincular usuario además `admin.users`); tarifas, política de pago y
// viajes pagados `driverpay.view` / `driverpay.manage`. Lecturas con `useQuery` (clave `[ruta, params]`), escrituras con
// `useMutation`; las que devuelven la ficha del chofer la dejan en caché.
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components, paths } from '../../kernel/api/schema'
import { catalogKeys } from '../../kernel/catalogs'

type Schemas = components['schemas']

export type DriverListItem = Schemas['DriverListItemDto']
export type DriverDetail = Schemas['DriverDetailDto']
export type DriverCreateRequest = Schemas['DriverCreateRequest']
export type DriverPatchRequest = Schemas['DriverPatchRequest']
export type DriverLicense = Schemas['DriverLicenseDto']
export type DriverLicenseRequest = Schemas['DriverLicenseRequest']
export type DriverCertification = Schemas['DriverCertificationDto']
export type DriverCertificationRequest = Schemas['DriverCertificationRequest']
export type DriverDevice = Schemas['DriverDeviceDto']
export type DispatchZone = Schemas['DispatchZoneDto']
export type DriverRates = Schemas['DriverRatesDto']
export type DeliveryRate = Schemas['DriverDeliveryRateDto']
export type AttemptRate = Schemas['DriverAttemptRateDto']
export type TripRate = Schemas['DriverTripRateDto']
export type DriverTripType = Schemas['DriverTripTypeDto']
export type PayPolicy = Schemas['DriverPayPolicyDto']
export type PayoutPreview = Schemas['PayoutPreviewDto']
export type PayoutPreviewRequest = Schemas['PayoutPreviewRequest']
export type DriverTrip = Schemas['DriverTripDto']
export type DriverTripCreateRequest = Schemas['DriverTripCreateRequest']
export type UserSummary = Schemas['UserSummaryDto']
export type DriverListQuery = NonNullable<paths['/api/v1/drivers']['get']['parameters']['query']>
export type DriverTripQuery = NonNullable<paths['/api/v1/drivers/{publicId}/trips']['get']['parameters']['query']>

export const DRIVER_ENTITY_TYPE = 'DRIVER'

export const driverKeys = {
  /** Prefijo de todas las listas de choferes (también la de Órdenes: `useDrivers`). */
  list: ['/api/v1/drivers'] as const,
  listOf: (query: DriverListQuery) => ['/api/v1/drivers', query] as const,
  detail: (publicId: string) => ['/api/v1/drivers/{publicId}', publicId] as const,
  zones: ['/api/v1/dispatch-zones'] as const,
  /** Prefijo de las tarifas de un chofer (con y sin historial). */
  rates: (publicId: string) => ['/api/v1/drivers/{publicId}/rates', publicId] as const,
  ratesOf: (publicId: string, includeHistory: boolean) => ['/api/v1/drivers/{publicId}/rates', publicId, { includeHistory }] as const,
  policy: ['/api/v1/driver-pay-policy'] as const,
  preview: ['/api/v1/driver-pay-policy/preview'] as const,
  tripTypes: ['/api/v1/driver-trip-types'] as const,
  trips: (publicId: string) => ['/api/v1/drivers/{publicId}/trips', publicId] as const,
  tripsOf: (publicId: string, query: DriverTripQuery) => ['/api/v1/drivers/{publicId}/trips', publicId, query] as const,
  users: ['/api/v1/users', 'drivers'] as const,
}

// ---- Lecturas ----
/**
 * `GET /api/v1/drivers` (activos; `fleet.view`): para elegir el chofer de una entrega especial (Órdenes). Consulta secundaria: un 403
 * no saca de la pantalla (el diálogo avisa que no puede cargar la lista).
 */
export function useDrivers(enabled: boolean) {
  return useQuery({
    queryKey: [driverKeys.list[0], { includeInactive: false }],
    queryFn: () => unwrap(api.GET('/api/v1/drivers', { params: { query: { includeInactive: false } } })),
    enabled,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

/** Lista maestra de la pantalla (no pagina: el API devuelve todos, ordenados por código). */
export function useDriverList(query: DriverListQuery) {
  return useQuery({
    queryKey: driverKeys.listOf(query),
    queryFn: () => unwrap(api.GET('/api/v1/drivers', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

export function useDriver(publicId: string | null) {
  return useQuery({
    queryKey: driverKeys.detail(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/drivers/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
  })
}

/** Zonas de despacho para el select de la ficha y el filtro de la lista (consulta secundaria). */
export function useDispatchZones(includeInactive = false, enabled = true) {
  return useQuery({
    queryKey: [driverKeys.zones[0], { includeInactive }],
    queryFn: () => unwrap(api.GET('/api/v1/dispatch-zones', { params: { query: { includeInactive } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** Usuarios de la compañía para vincular al chofer (`admin.users`). Solo se pide si `enabled`. */
export function useLinkableUsers(enabled: boolean) {
  return useQuery({
    queryKey: driverKeys.users,
    queryFn: () => unwrap(api.GET('/api/v1/users')),
    enabled,
    retry: false,
    staleTime: 60_000,
    meta: { handleAccessDenied: false },
  })
}

export function useDriverRates(publicId: string | null, includeHistory: boolean, enabled = true) {
  return useQuery({
    queryKey: driverKeys.ratesOf(publicId ?? '', includeHistory),
    queryFn: () => unwrap(api.GET('/api/v1/drivers/{publicId}/rates', { params: { path: { publicId: publicId ?? '' }, query: { includeHistory } } })),
    enabled: !!publicId && enabled,
    // al activar «Ver historial» las tablas siguen a la vista mientras llegan las filas cerradas (no parpadea el panel)
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

export function useDriverTripTypes(enabled = true) {
  return useQuery({
    queryKey: driverKeys.tripTypes,
    queryFn: () => unwrap(api.GET('/api/v1/driver-trip-types')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

export function usePayPolicy(enabled = true) {
  return useQuery({
    queryKey: driverKeys.policy,
    queryFn: () => unwrap(api.GET('/api/v1/driver-pay-policy')),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** Vista previa del pago, calculada por el SERVIDOR (`POST /driver-pay-policy/preview`, no escribe nada). */
export function usePayoutPreview(body: PayoutPreviewRequest | null, enabled = true) {
  return useQuery({
    queryKey: [driverKeys.preview[0], body],
    queryFn: () => unwrap(api.POST('/api/v1/driver-pay-policy/preview', { body: body ?? {} })),
    enabled: enabled && body !== null,
    retry: false,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

export function useDriverTrips(publicId: string | null, query: DriverTripQuery, enabled = true) {
  return useQuery({
    queryKey: driverKeys.tripsOf(publicId ?? '', query),
    queryFn: () => unwrap(api.GET('/api/v1/drivers/{publicId}/trips', { params: { path: { publicId: publicId ?? '' }, query } })),
    enabled: !!publicId && enabled,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

// ---- Escrituras del chofer ----
/** Deja la ficha devuelta en caché y refresca las listas (nombre, zona, estatus). */
function settleDriver(qc: QueryClient, dto: DriverDetail) {
  if (dto.publicId) qc.setQueryData(driverKeys.detail(dto.publicId), dto)
  void qc.invalidateQueries({ queryKey: driverKeys.list })
}

/** Todo lo que cambia al dar de baja a un chofer: ficha, listas, tarifas, viajes y su historial de estatus. */
function invalidateDriver(qc: QueryClient, publicId: string, id?: number | null) {
  void qc.invalidateQueries({ queryKey: driverKeys.detail(publicId) })
  void qc.invalidateQueries({ queryKey: driverKeys.list })
  void qc.invalidateQueries({ queryKey: driverKeys.rates(publicId) })
  void qc.invalidateQueries({ queryKey: driverKeys.trips(publicId) })
  void qc.invalidateQueries({ queryKey: driverKeys.preview })
  if (id) void qc.invalidateQueries({ queryKey: catalogKeys.history(DRIVER_ENTITY_TYPE, id) })
}

export function useCreateDriver() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: DriverCreateRequest) => unwrap(api.POST('/api/v1/drivers', { body })),
    onSuccess: (d) => {
      settleDriver(qc, d)
      if (d.id) void qc.invalidateQueries({ queryKey: catalogKeys.history(DRIVER_ENTITY_TYPE, d.id) })
    },
  })
}

export function useUpdateDriver(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: DriverPatchRequest) => unwrap(api.PATCH('/api/v1/drivers/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: (d) => settleDriver(qc, d),
  })
}

/** `POST /drivers/{id}/status`: ACTIVE ↔ UNAVAILABLE (Disponible / No disponible). */
export function useDriverStatusChange(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ toCode, comment }: { toCode: string; comment?: string }) =>
      unwrap(api.POST('/api/v1/drivers/{publicId}/status', { params: { path: { publicId } }, body: { toCode, comment: comment || null } })),
    onSuccess: (d) => {
      settleDriver(qc, d)
      if (d.id) void qc.invalidateQueries({ queryKey: catalogKeys.history(DRIVER_ENTITY_TYPE, d.id) })
    },
  })
}

/** Casilla «Activo»: `deactivate` / `reactivate` (204). */
export function useSetDriverActive(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (active: boolean) => {
      if (active) await unwrap(api.POST('/api/v1/drivers/{publicId}/reactivate', { params: { path: { publicId } } }))
      else await unwrap(api.POST('/api/v1/drivers/{publicId}/deactivate', { params: { path: { publicId } } }))
    },
    onSuccess: () => invalidateDriver(qc, publicId),
  })
}

/** «Eliminar chofer»: `DELETE` con comentario opcional (204). Baja definitiva, sin borrado físico. */
export function useRetireDriver(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (v: { comment?: string; driverId?: number | null }) => {
      await unwrap(api.DELETE('/api/v1/drivers/{publicId}', { params: { path: { publicId } }, body: { comment: v.comment || null } }))
    },
    onSuccess: (_d, v) => invalidateDriver(qc, publicId, v.driverId),
  })
}

// ---- Licencias, certificaciones y dispositivos (hijas de la ficha) ----
export function useLicenseMutations(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = () => qc.invalidateQueries({ queryKey: driverKeys.detail(publicId) })
  const path = { path: { publicId } }
  return {
    add: useMutation({ mutationFn: (body: DriverLicenseRequest) => unwrap(api.POST('/api/v1/drivers/{publicId}/licenses', { params: path, body })), onSuccess }),
    update: useMutation({
      mutationFn: (v: { id: number; body: DriverLicenseRequest }) => unwrap(api.PATCH('/api/v1/drivers/{publicId}/licenses/{id}', { params: { path: { publicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/drivers/{publicId}/licenses/{id}/deactivate', { params: { path: { publicId, id } } })),
      onSuccess,
    }),
  }
}

export function useCertificationMutations(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = () => qc.invalidateQueries({ queryKey: driverKeys.detail(publicId) })
  const path = { path: { publicId } }
  return {
    add: useMutation({ mutationFn: (body: DriverCertificationRequest) => unwrap(api.POST('/api/v1/drivers/{publicId}/certifications', { params: path, body })), onSuccess }),
    update: useMutation({
      mutationFn: (v: { id: number; body: DriverCertificationRequest }) =>
        unwrap(api.PATCH('/api/v1/drivers/{publicId}/certifications/{id}', { params: { path: { publicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/drivers/{publicId}/certifications/{id}/deactivate', { params: { path: { publicId, id } } })),
      onSuccess,
    }),
  }
}

export function useDeactivateDevice(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.POST('/api/v1/drivers/{publicId}/devices/{id}/deactivate', { params: { path: { publicId, id } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: driverKeys.detail(publicId) }),
  })
}

// ---- Tarifas ----
export function useRateMutations(publicId: string) {
  const qc = useQueryClient()
  // las tarifas cambian la vista previa del pago
  const onSuccess = () => Promise.all([qc.invalidateQueries({ queryKey: driverKeys.rates(publicId) }), qc.invalidateQueries({ queryKey: driverKeys.preview })])
  const path = { path: { publicId } }
  type Update = Schemas['DriverRateUpdateRequest']
  return {
    addDelivery: useMutation({ mutationFn: (body: Schemas['DriverDeliveryRateCreateRequest']) => unwrap(api.POST('/api/v1/drivers/{publicId}/delivery-rates', { params: path, body })), onSuccess }),
    updateDelivery: useMutation({
      mutationFn: (v: { id: number; body: Update }) => unwrap(api.PATCH('/api/v1/drivers/{publicId}/delivery-rates/{id}', { params: { path: { publicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    closeDelivery: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/drivers/{publicId}/delivery-rates/{id}/close', { params: { path: { publicId, id } }, body: {} })),
      onSuccess,
    }),
    setAttempt: useMutation({
      mutationFn: (v: { attemptNumber: number; body: Update }) =>
        unwrap(api.PUT('/api/v1/drivers/{publicId}/attempt-rates/{attemptNumber}', { params: { path: { publicId, attemptNumber: v.attemptNumber } }, body: v.body })),
      onSuccess,
    }),
    closeAttempt: useMutation({
      mutationFn: (attemptNumber: number) => unwrap(api.POST('/api/v1/drivers/{publicId}/attempt-rates/{attemptNumber}/close', { params: { path: { publicId, attemptNumber } }, body: {} })),
      onSuccess,
    }),
    addTrip: useMutation({ mutationFn: (body: Schemas['DriverTripRateCreateRequest']) => unwrap(api.POST('/api/v1/drivers/{publicId}/trip-rates', { params: path, body })), onSuccess }),
    updateTrip: useMutation({
      mutationFn: (v: { id: number; body: Update }) => unwrap(api.PATCH('/api/v1/drivers/{publicId}/trip-rates/{id}', { params: { path: { publicId, id: v.id } }, body: v.body })),
      onSuccess,
    }),
    closeTrip: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/drivers/{publicId}/trip-rates/{id}/close', { params: { path: { publicId, id } }, body: {} })),
      onSuccess,
    }),
  }
}

// ---- Política de pago ----
function usePolicyInvalidation() {
  const qc = useQueryClient()
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: driverKeys.policy }),
      // los niveles de intento y la fórmula también salen en las tarifas de cada chofer y en la vista previa
      qc.invalidateQueries({ queryKey: ['/api/v1/drivers/{publicId}/rates'] }),
      qc.invalidateQueries({ queryKey: driverKeys.preview }),
    ])
}

/** `PATCH /driver-pay-policy`: cambia la fórmula vigente de la compañía. */
export function useUpdatePayPolicy() {
  const onSuccess = usePolicyInvalidation()
  return useMutation({
    mutationFn: (payoutFormula: string) => unwrap(api.PATCH('/api/v1/driver-pay-policy', { body: { payoutFormula } })),
    onSuccess,
  })
}

/** «+ Agregar intento»: sube un nivel de intento de la compañía (tope 20); no crea tarifas. */
export function useAddAttemptLevel() {
  const onSuccess = usePolicyInvalidation()
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/driver-pay-policy/attempt-levels')),
    onSuccess,
  })
}

// ---- Viajes pagados ----
export function useTripMutations(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = () => qc.invalidateQueries({ queryKey: driverKeys.trips(publicId) })
  return {
    create: useMutation({
      mutationFn: (body: DriverTripCreateRequest) => unwrap(api.POST('/api/v1/drivers/{publicId}/trips', { params: { path: { publicId } }, body })),
      onSuccess,
    }),
    cancel: useMutation({
      mutationFn: ({ tripPublicId, comment }: { tripPublicId: string; comment?: string }) =>
        unwrap(api.POST('/api/v1/drivers/{publicId}/trips/{tripPublicId}/cancel', { params: { path: { publicId, tripPublicId } }, body: { comment: comment || null } })),
      onSuccess,
    }),
  }
}

// ---- Avisos al chofer (bandeja de su app; permiso trips.dispatch) ----
export function useSendDriverNotice() {
  return useMutation({
    mutationFn: (body: Schemas['DriverNoticeSendRequest']) => unwrap(api.POST('/api/v1/driver-notices', { body })),
  })
}
