// Datos de "Aparatos móviles" (Sistema, Lote F8a P5): GET/POST /api/v1/devices, .../deactivate, .../reactivate,
// .../enroll-code. Todo bajo devices.manage + módulo WMS_LOTSERIAL (lo exige el API; aquí solo se documenta).
// (Archivo separado de `api.ts` porque esa pieza la construye en paralelo P4, "Roles y usuarios", sobre el mismo
// directorio `features/system`.)
//
// El alta no pide código (coincide con el mock aprobado y el plan, loteF8-plan.md §3 P5): `DeviceService.CreateAsync`
// genera uno legible ("AP-XXXXXX") cuando no se manda `code`. Si se manda uno (no lo pide esta pantalla, pero el campo
// existe para otros usos administrativos), el servidor lo respeta tal cual.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type DeviceDto = Schemas['DeviceDto']
export type DeviceCreatedDto = Schemas['DeviceCreatedDto']

const DEVICES_KEY = '/api/v1/devices'

/** Lista de aparatos de la compañía (por defecto solo los activos). */
export function useDevices(includeInactive: boolean) {
  const query = { includeInactive }
  return useQuery({
    queryKey: [DEVICES_KEY, query],
    queryFn: () => unwrap(api.GET('/api/v1/devices', { params: { query } })),
  })
}

export interface CreateDeviceInput {
  name: string
  defaultWarehousePublicId: string | null
}

/** Alta de un aparato: Nombre y almacén por defecto (opcional); código y código de registro llegan en la respuesta. */
export function useCreateDevice() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: CreateDeviceInput) =>
      unwrap(api.POST('/api/v1/devices', { body: { name: input.name, defaultWarehousePublicId: input.defaultWarehousePublicId } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: [DEVICES_KEY] }),
  })
}

/** Desactivar / reactivar un aparato. */
export function useDeviceStatusAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { action: 'deactivate' | 'reactivate'; publicId: string }) =>
      v.action === 'deactivate'
        ? unwrap(api.POST('/api/v1/devices/{publicId}/deactivate', { params: { path: { publicId: v.publicId } } }))
        : unwrap(api.POST('/api/v1/devices/{publicId}/reactivate', { params: { path: { publicId: v.publicId } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: [DEVICES_KEY] }),
  })
}

/** Regenera el código de registro (el anterior deja de servir); se muestra una sola vez. */
export function useRegenerateEnrollCode() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/devices/{publicId}/enroll-code', { params: { path: { publicId } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: [DEVICES_KEY] }),
  })
}
