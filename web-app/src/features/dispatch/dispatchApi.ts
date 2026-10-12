// Lote F-E2a — Datos de la Sala de despacho (manual 05). Lecturas `trips.view` (módulo LTL_GROUND); escrituras `trips.plan` /
// `trips.optimize` / `trips.dispatch` / `trips.close`. Choferes, vehículos y zonas: `fleet.view` + CATALOG; si falta, la pantalla lo explica.
// Cada intento de una acción lleva su propia `Idempotency-Key`; toda escritura deja al día lista, ficha, sin asignar, despachables y progreso.
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { orderKeys } from '../orders/api'
import { driverKeys } from '../drivers/api'
import { fleetKeys } from '../fleet/api'
import { scanKeys } from '../scan/scanApi'
import type { OptimizationRun, TripDetail, TripPatchRequest, TripCreateRequest } from './dispatchRules'

export const dispatchKeys = {
  list: ['/api/v1/trips'] as const,
  detail: ['/api/v1/trips/{publicId}'] as const,
  dispatchable: ['/api/v1/trips/dispatchable'] as const,
  monitor: ['/api/v1/trips/monitor'] as const,
  runs: ['/api/v1/trips/{publicId}/optimization-runs'] as const,
  unassigned: scanKeys.unassigned,
}

const idempotency = () => ({ 'Idempotency-Key': crypto.randomUUID() })

/** Después de cualquier escritura: todo lo que muestra la pantalla vuelve a pedirse. */
export function invalidateDispatch(qc: QueryClient) {
  for (const key of [dispatchKeys.list, dispatchKeys.detail, dispatchKeys.dispatchable, dispatchKeys.monitor, dispatchKeys.runs, dispatchKeys.unassigned, orderKeys.list, orderKeys.detail]) {
    void qc.invalidateQueries({ queryKey: key })
  }
  void qc.invalidateQueries({ queryKey: fleetKeys.availability })
}

/**
 * Pone la ficha devuelta por una escritura en la caché. Antes se cancela cualquier lectura de esa ficha que ya viaja: si llegara DESPUÉS, pisaría la
 * ficha nueva con una anterior (orden y `rowVersion` viejos) y el siguiente movimiento rápido saldría con la versión vieja (409 espurio).
 */
async function putDetail(qc: QueryClient, trip: TripDetail) {
  await qc.cancelQueries({ queryKey: [dispatchKeys.detail[0], trip.publicId] })
  qc.setQueryData([dispatchKeys.detail[0], trip.publicId], trip)
}

// ---------------------------------------------------------------- lecturas

/** `GET /trips?date=`: las rutas activas del día (arreglo, sin total ni paginación). */
export function useTrips(date: string, enabled: boolean) {
  return useQuery({
    queryKey: [dispatchKeys.list[0], { date }],
    queryFn: () => unwrap(api.GET('/api/v1/trips', { params: { query: { date } } })),
    enabled,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

export function useTrip(publicId: string | null, enabled: boolean) {
  return useQuery({
    queryKey: [dispatchKeys.detail[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/trips/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: enabled && !!publicId,
    meta: { handleAccessDenied: false },
  })
}

/** `GET /trips/dispatchable?date=`: rutas en borrador o planificadas con sus avisos y bloqueantes (`trips.dispatch`). */
export function useDispatchable(date: string, enabled: boolean) {
  return useQuery({
    queryKey: [dispatchKeys.dispatchable[0], { date }],
    queryFn: () => unwrap(api.GET('/api/v1/trips/dispatchable', { params: { query: { date } } })),
    enabled,
    meta: { handleAccessDenied: false },
  })
}

/** `GET /trips/monitor?date=`: solo para la barra de progreso de las tarjetas (las rutas ya despachadas). Si falla, la tarjeta no muestra avance. */
export function useTripProgress(date: string, enabled: boolean) {
  return useQuery({
    queryKey: [dispatchKeys.monitor[0], { date }],
    queryFn: () => unwrap(api.GET('/api/v1/trips/monitor', { params: { query: { date } } })),
    enabled,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

/** Choferes y vehículos con su disponibilidad en la fecha (`GET /fleet/availability`, `fleet.view`). */
export function useAvailability(date: string, enabled: boolean) {
  return useQuery({
    queryKey: fleetKeys.availabilityOf(date, false),
    queryFn: () => unwrap(api.GET('/api/v1/fleet/availability', { params: { query: { date, onlyAvailable: false } } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

// ---------------------------------------------------------------- escrituras

export function useCreateTrip() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: TripCreateRequest) => unwrap(api.POST('/api/v1/trips', { body, headers: idempotency() })),
    onSuccess: async (trip) => {
      await putDetail(qc, trip)
      invalidateDispatch(qc)
    },
  })
}

export function usePatchTrip() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string; body: TripPatchRequest }) =>
      unwrap(api.PATCH('/api/v1/trips/{publicId}', { params: { path: { publicId } }, body, headers: idempotency() })),
    onSuccess: async (trip) => {
      await putDetail(qc, trip)
      invalidateDispatch(qc)
    },
    // un 409 (otro usuario cambió la ruta) también recarga: la pantalla muestra el texto del servidor y los datos nuevos
    onError: () => invalidateDispatch(qc),
  })
}

export function useCancelTrip() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, comment, rowVersion }: { publicId: string; comment?: string; rowVersion?: string | null }) =>
      unwrap(api.DELETE('/api/v1/trips/{publicId}', { params: { path: { publicId } }, body: { comment: comment || null, rowVersion: rowVersion ?? null }, headers: idempotency() })),
    onSuccess: () => invalidateDispatch(qc),
    onError: () => invalidateDispatch(qc),
  })
}

export function useAddOrders() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, orderPublicIds, rowVersion }: { publicId: string; orderPublicIds: string[]; rowVersion?: string | null }) =>
      unwrap(api.POST('/api/v1/trips/{publicId}/orders', { params: { path: { publicId } }, body: { orderPublicIds, rowVersion: rowVersion ?? null }, headers: idempotency() })),
    onSuccess: async (trip) => {
      await putDetail(qc, trip)
      invalidateDispatch(qc)
    },
    onError: () => invalidateDispatch(qc),
  })
}

export function useRemoveOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, orderPublicId, rowVersion }: { publicId: string; orderPublicId: string; rowVersion?: string | null }) =>
      unwrap(
        api.DELETE('/api/v1/trips/{publicId}/orders/{orderPublicId}', {
          params: { path: { publicId, orderPublicId } },
          body: { rowVersion: rowVersion ?? null },
          headers: idempotency(),
        }),
      ),
    onSuccess: async (trip) => {
      await putDetail(qc, trip)
      invalidateDispatch(qc)
    },
    onError: () => invalidateDispatch(qc),
  })
}

export function useSetSequence() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, routeStopIds, rowVersion }: { publicId: string; routeStopIds: number[]; rowVersion?: string | null }) =>
      unwrap(api.PUT('/api/v1/trips/{publicId}/route/sequence', { params: { path: { publicId } }, body: { routeStopIds, rowVersion: rowVersion ?? null }, headers: idempotency() })),
    onSuccess: async (trip) => {
      await putDetail(qc, trip)
      invalidateDispatch(qc)
    },
    onError: () => invalidateDispatch(qc),
  })
}

/** Pin manual de una parada (`PUT /trips/{id}/stops/{routeStopId}/location`, `trips.plan`, ruta abierta): la precisión pasa a MANUAL y el servidor recalcula las ETAs. */
export function useSetStopLocation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, routeStopId, lat, lng, rowVersion }: { publicId: string; routeStopId: number; lat: number; lng: number; rowVersion?: string | null }) =>
      unwrap(
        api.PUT('/api/v1/trips/{publicId}/stops/{routeStopId}/location', {
          params: { path: { publicId, routeStopId } },
          body: { lat, lng, rowVersion: rowVersion ?? null },
          headers: idempotency(),
        }),
      ),
    onSuccess: async (trip) => {
      await putDetail(qc, trip)
      invalidateDispatch(qc)
    },
    onError: () => invalidateDispatch(qc),
  })
}

/** `POST /trips/{id}/optimize`; después pide las corridas para comparar con la anterior (solo si existe). */
export function useOptimizeTrip() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ publicId, rowVersion }: { publicId: string; rowVersion?: string | null }) => {
      const result = await unwrap(api.POST('/api/v1/trips/{publicId}/optimize', { params: { path: { publicId } }, body: { rowVersion: rowVersion ?? null }, headers: idempotency() }))
      let runs: OptimizationRun[] = []
      try {
        runs = await unwrap(api.GET('/api/v1/trips/{publicId}/optimization-runs', { params: { path: { publicId } } }))
      } catch {
        // sin la comparación la optimización sigue siendo válida
      }
      return { result, runs }
    },
    onSuccess: async ({ result }) => {
      if (result.trip?.publicId) await putDetail(qc, result.trip)
      invalidateDispatch(qc)
    },
    onError: () => invalidateDispatch(qc),
  })
}

export function useDispatchBatch() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ tripPublicIds, comment }: { tripPublicIds: string[]; comment?: string }) =>
      unwrap(api.POST('/api/v1/trips/dispatch', { body: { tripPublicIds, comment: comment || null }, headers: idempotency() })),
    onSuccess: () => invalidateDispatch(qc),
  })
}

export function useStartTrip() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, comment, rowVersion }: { publicId: string; comment?: string; rowVersion?: string | null }) =>
      unwrap(api.POST('/api/v1/trips/{publicId}/start', { params: { path: { publicId } }, body: { comment: comment || null, rowVersion: rowVersion ?? null }, headers: idempotency() })),
    onSuccess: () => {
      invalidateDispatch(qc)
      void qc.invalidateQueries({ queryKey: driverKeys.list })
    },
    onError: () => invalidateDispatch(qc),
  })
}

export function useCompleteTrip() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, comment, rowVersion }: { publicId: string; comment?: string; rowVersion?: string | null }) =>
      unwrap(api.POST('/api/v1/trips/{publicId}/complete', { params: { path: { publicId } }, body: { comment: comment || null, rowVersion: rowVersion ?? null }, headers: idempotency() })),
    onSuccess: () => invalidateDispatch(qc),
    onError: () => invalidateDispatch(qc),
  })
}

export function usePlanDay() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { planDate: string; dispatchZoneIds: number[] | null; createEmptyTrips: boolean }) =>
      unwrap(api.POST('/api/v1/trips/plan-day', { body, headers: idempotency() })),
    onSuccess: () => invalidateDispatch(qc),
  })
}

export function useReassignZone() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { planDate: string; dispatchZoneIds: number[]; driverPublicId: string }) =>
      unwrap(api.POST('/api/v1/trips/reassign-zone', { body, headers: idempotency() })),
    onSuccess: () => invalidateDispatch(qc),
  })
}

export type { TripDetail }
