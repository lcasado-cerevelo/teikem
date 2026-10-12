// Datos de Mantenimiento y combustible de Flota (Lote F-D3, manual 04 §5 programas, §6 órdenes de trabajo y §7 combustible). Módulo CATALOG.
// Permisos: ver `fleet.view`; escribir `fleet.maintenance`. Lecturas con `useQuery` (clave `[ruta, params]`), escrituras con `useMutation`.
// Las órdenes de trabajo NO devuelven total (el API entrega un arreglo): la lista se lee completa de a 200 con `fetchAllPages` y la tabla ordena
// y pagina en la pantalla, así el orden vale para todo lo filtrado.
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import type { components, paths } from '../../kernel/api/schema'
import { fleetKeys, invalidateFleet } from './api'

type Schemas = components['schemas']

export type MaintenanceSchedule = Schemas['MaintenanceScheduleDto']
export type MaintenanceScheduleRequest = Schemas['MaintenanceScheduleRequest']
export type MaintenanceDue = Schemas['MaintenanceDueDto']
export type WorkOrderListItem = Schemas['WorkOrderListItemDto']
export type WorkOrderDetail = Schemas['WorkOrderDetailDto']
export type WorkOrderCreateRequest = Schemas['WorkOrderCreateRequest']
export type WorkOrderPatchRequest = Schemas['WorkOrderPatchRequest']
export type WorkOrderStatusRequest = Schemas['WorkOrderStatusRequest']
export type MaintenanceTask = Schemas['MaintenanceTaskDto']
export type MaintenanceTaskRequest = Schemas['MaintenanceTaskRequest']
export type FuelLog = Schemas['FuelLogDto']
export type FuelLogPage = Schemas['FuelLogPageDto']
export type FuelLogCreateRequest = Schemas['FuelLogCreateRequest']
export type FuelLogPatchRequest = Schemas['FuelLogPatchRequest']
export type FuelVehicleSummary = Schemas['FuelVehicleSummaryDto']
export type DueQuery = NonNullable<paths['/api/v1/maintenance-schedules/due']['get']['parameters']['query']>
export type WorkOrderQuery = Omit<NonNullable<paths['/api/v1/maintenance-work-orders']['get']['parameters']['query']>, 'skip' | 'take'>
export type FuelQuery = NonNullable<paths['/api/v1/fuel-logs']['get']['parameters']['query']>

export const WORK_ORDER_ENTITY_TYPE = 'WORK_ORDER'
export const WORK_ORDER_STATUS_DOMAIN = 'WorkOrderStatus'

/** Prefijos de caché que mueve una orden o una carga: fichas de vehículo e historiales de estatus. */
const VEHICLE_DETAIL_PREFIX = ['/api/v1/vehicles/{publicId}'] as const
const HISTORY_PREFIX = ['/api/v1/status/history/{entityType}/{entityId}'] as const

export const maintenanceKeys = {
  schedules: ['/api/v1/maintenance-schedules'] as const,
  schedulesOf: (includeInactive: boolean) => ['/api/v1/maintenance-schedules', { includeInactive }] as const,
  due: ['/api/v1/maintenance-schedules/due'] as const,
  dueOf: (query: DueQuery) => ['/api/v1/maintenance-schedules/due', query] as const,
  workOrders: ['/api/v1/maintenance-work-orders'] as const,
  workOrdersOf: (query: WorkOrderQuery) => ['/api/v1/maintenance-work-orders', query] as const,
  workOrder: (publicId: string) => ['/api/v1/maintenance-work-orders/{publicId}', publicId] as const,
  fuel: ['/api/v1/fuel-logs'] as const,
  fuelOf: (query: FuelQuery) => ['/api/v1/fuel-logs', query] as const,
}

// ---- Lecturas ----
/** Programas de mantenimiento (no pagina: el API devuelve todos, por nombre). */
export function useSchedules(includeInactive: boolean) {
  return useQuery({
    queryKey: maintenanceKeys.schedulesOf(includeInactive),
    queryFn: () => unwrap(api.GET('/api/v1/maintenance-schedules', { params: { query: { includeInactive } } })),
    placeholderData: keepPreviousData,
  })
}

/** Estado de cada programa por vehículo (Al día / Por vencer / Vencido / Sin historial), lo más urgente primero. */
export function useDue(query: DueQuery) {
  return useQuery({
    queryKey: maintenanceKeys.dueOf(query),
    queryFn: () => unwrap(api.GET('/api/v1/maintenance-schedules/due', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

/** Lee todas las órdenes que cumplen el filtro (el API no devuelve total: se pide de a 200 hasta recibir una página incompleta). */
export async function readAllWorkOrders(query: WorkOrderQuery): Promise<WorkOrderListItem[]> {
  const result = await fetchAllPages<WorkOrderListItem>(async (skip, take) => ({
    items: await unwrap(api.GET('/api/v1/maintenance-work-orders', { params: { query: { ...query, skip, take } } })),
  }))
  return result.items
}

export function useWorkOrderList(query: WorkOrderQuery) {
  return useQuery({
    queryKey: maintenanceKeys.workOrdersOf(query),
    queryFn: () => readAllWorkOrders(query),
    placeholderData: keepPreviousData,
  })
}

export function useWorkOrder(publicId: string | null) {
  return useQuery({
    queryKey: maintenanceKeys.workOrder(publicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/maintenance-work-orders/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: !!publicId,
  })
}

/** Cargas de combustible de una página (con el resumen por vehículo de TODO el filtro). */
export function useFuelLogs(query: FuelQuery) {
  return useQuery({
    queryKey: maintenanceKeys.fuelOf(query),
    queryFn: () => unwrap(api.GET('/api/v1/fuel-logs', { params: { query } })),
    placeholderData: keepPreviousData,
  })
}

/** Todas las cargas del filtro (para Exportar y para ordenar todo lo filtrado): páginas de 200 con `skip`/`take`. */
export function readAllFuelLogs(query: Omit<FuelQuery, 'skip' | 'take'>) {
  return fetchAllPages<FuelLog>((skip, take) => unwrap(api.GET('/api/v1/fuel-logs', { params: { query: { ...query, skip, take } } })))
}

// ---- Programas ----
function invalidateSchedules(qc: QueryClient) {
  void qc.invalidateQueries({ queryKey: maintenanceKeys.schedules })
  void qc.invalidateQueries({ queryKey: maintenanceKeys.due })
}

export function useScheduleMutations() {
  const qc = useQueryClient()
  const onSuccess = () => invalidateSchedules(qc)
  return {
    create: useMutation({ mutationFn: (body: MaintenanceScheduleRequest) => unwrap(api.POST('/api/v1/maintenance-schedules', { body })), onSuccess }),
    update: useMutation({
      mutationFn: (v: { id: number; body: MaintenanceScheduleRequest }) =>
        unwrap(api.PATCH('/api/v1/maintenance-schedules/{id}', { params: { path: { id: v.id } }, body: v.body })),
      onSuccess,
    }),
    setActive: useMutation({
      mutationFn: (v: { id: number; active: boolean }) =>
        v.active
          ? unwrap(api.POST('/api/v1/maintenance-schedules/{id}/reactivate', { params: { path: { id: v.id } } }))
          : unwrap(api.POST('/api/v1/maintenance-schedules/{id}/deactivate', { params: { path: { id: v.id } } })),
      onSuccess,
    }),
  }
}

// ---- Órdenes de trabajo ----
/**
 * Lo que cambia cuando se toca una orden: la lista, la ficha, el historial, las tarjetas y el panel de vencimientos (cerrar una orden
 * adelanta el último servicio), y —porque iniciar, cerrar o cancelar mueve el estatus y el odómetro del vehículo— los vehículos, la
 * disponibilidad y los documentos por vencer.
 */
function settleWorkOrder(qc: QueryClient, dto: WorkOrderDetail, statusChanged: boolean) {
  if (dto.publicId) qc.setQueryData(maintenanceKeys.workOrder(dto.publicId), dto)
  void qc.invalidateQueries({ queryKey: maintenanceKeys.workOrders })
  if (statusChanged) {
    // historiales (el de la orden y el del vehículo) y fichas de vehículo
    void qc.invalidateQueries({ queryKey: HISTORY_PREFIX })
    void qc.invalidateQueries({ queryKey: VEHICLE_DETAIL_PREFIX })
    invalidateSchedules(qc)
    invalidateFleet(qc)
  }
}

export function useCreateWorkOrder() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: WorkOrderCreateRequest) => unwrap(api.POST('/api/v1/maintenance-work-orders', { body })),
    onSuccess: (dto) => settleWorkOrder(qc, dto, false),
  })
}

export function useUpdateWorkOrder(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: WorkOrderPatchRequest) => unwrap(api.PATCH('/api/v1/maintenance-work-orders/{publicId}', { params: { path: { publicId } }, body })),
    onSuccess: (dto) => settleWorkOrder(qc, dto, false),
  })
}

/** `POST /status`: OPEN → IN_PROGRESS → CLOSED, o CANCELLED. Toca el estatus y el odómetro del vehículo. */
export function useWorkOrderStatusChange(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: WorkOrderStatusRequest) => unwrap(api.POST('/api/v1/maintenance-work-orders/{publicId}/status', { params: { path: { publicId } }, body })),
    onSuccess: (dto) => settleWorkOrder(qc, dto, true),
  })
}

export function useTaskMutations(publicId: string) {
  const qc = useQueryClient()
  const onSuccess = (dto: WorkOrderDetail) => settleWorkOrder(qc, dto, false)
  return {
    add: useMutation({
      mutationFn: (body: MaintenanceTaskRequest) => unwrap(api.POST('/api/v1/maintenance-work-orders/{publicId}/tasks', { params: { path: { publicId } }, body })),
      onSuccess,
    }),
    update: useMutation({
      mutationFn: (v: { id: number; body: MaintenanceTaskRequest }) =>
        unwrap(api.PATCH('/api/v1/maintenance-work-orders/{publicId}/tasks/{taskId}', { params: { path: { publicId, taskId: v.id } }, body: v.body })),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/maintenance-work-orders/{publicId}/tasks/{taskId}/deactivate', { params: { path: { publicId, taskId: id } } })),
      onSuccess,
    }),
  }
}

// ---- Combustible ----
/** Una carga activa sube el odómetro del vehículo y eso mueve el panel de vencimientos: se refrescan los dos. */
export function useFuelMutations() {
  const qc = useQueryClient()
  const onSuccess = () => {
    void qc.invalidateQueries({ queryKey: maintenanceKeys.fuel })
    void qc.invalidateQueries({ queryKey: maintenanceKeys.due })
    void qc.invalidateQueries({ queryKey: fleetKeys.vehicles })
    void qc.invalidateQueries({ queryKey: VEHICLE_DETAIL_PREFIX })
  }
  return {
    create: useMutation({ mutationFn: (body: FuelLogCreateRequest) => unwrap(api.POST('/api/v1/fuel-logs', { body })), onSuccess }),
    update: useMutation({
      mutationFn: (v: { id: number; body: FuelLogPatchRequest }) => unwrap(api.PATCH('/api/v1/fuel-logs/{id}', { params: { path: { id: v.id } }, body: v.body })),
      onSuccess,
    }),
    remove: useMutation({
      mutationFn: (id: number) => unwrap(api.POST('/api/v1/fuel-logs/{id}/deactivate', { params: { path: { id } } })),
      onSuccess,
    }),
  }
}
