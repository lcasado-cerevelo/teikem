// Lote 222: ajustes de envío de las ventas (Ajustes → Operación → Ventas). Ver: sales.view o admin.tenant; cambiar: admin.tenant.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type SalesShippingSettingsDto = components['schemas']['SalesShippingSettingsDto']
export type SalesShippingSettingsRequest = components['schemas']['SalesShippingSettingsRequest']
export const salesSettingsKey = ['/api/v1/sales-orders/settings'] as const

export function useSalesShippingSettings() {
  return useQuery({ queryKey: salesSettingsKey, queryFn: () => unwrap(api.GET('/api/v1/sales-orders/settings')), meta: { handleAccessDenied: false } })
}

export function useSaveSalesShippingSettings() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: SalesShippingSettingsRequest) => unwrap(api.PATCH('/api/v1/sales-orders/settings', { body })),
    onSuccess: (data) => qc.setQueryData(salesSettingsKey, data),
  })
}
