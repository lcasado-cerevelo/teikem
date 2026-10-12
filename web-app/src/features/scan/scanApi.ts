// Lote F-E1 — Datos de la Estación de escaneo (manual 05 §7 y §3.3). Escaneo de salida: `trips.scan` + LTL_GROUND (el servidor
// responde SIEMPRE 200 con un resultado tipado; 400 solo por el código). Pendientes: `trips.view`. Zonas: `fleet.view` (módulo
// CATALOG); ficha de la orden: `orders.view`. Las lecturas secundarias no sacan al usuario de la pantalla (`handleAccessDenied: false`).
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components, paths } from '../../kernel/api/schema'
import { orderKeys } from '../orders/api'
import type { KnownOrder, ScanResult } from './scanRules'

export type UnassignedQuery = NonNullable<paths['/api/v1/trips/unassigned-orders']['get']['parameters']['query']>
export type OutboundScanRequest = components['schemas']['OutboundScanRequest']

export const scanKeys = {
  unassigned: ['/api/v1/trips/unassigned-orders'] as const,
  trips: ['/api/v1/trips'] as const,
}

/** `GET /trips/unassigned-orders`: las órdenes que el escaneo puede asignar. Sin `enabled` (sin `trips.view`) no se pide. */
export function useUnassignedOrders(query: UnassignedQuery, enabled: boolean) {
  return useQuery({
    queryKey: [scanKeys.unassigned[0], query],
    queryFn: () => unwrap(api.GET('/api/v1/trips/unassigned-orders', { params: { query } })),
    enabled,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

/**
 * Lo que la ficha de la orden dice del escaneo (cliente, consignatario, pueblo, estatus), sin pintar nada: completa la fila de la
 * sesión cuando la orden no venía en «Pendientes». Un fallo (403, red) devuelve null: la fila queda con lo que ya tenía.
 */
export async function fetchOrderKnown(qc: QueryClient, publicId: string): Promise<KnownOrder | null> {
  try {
    const o = await qc.fetchQuery({
      queryKey: [orderKeys.detail[0], publicId],
      queryFn: () => unwrap(api.GET('/api/v1/orders/{publicId}', { params: { path: { publicId } } })),
      staleTime: 30_000,
      meta: { handleAccessDenied: false },
    })
    return {
      clientName: o.clientName,
      consigneeName: o.delivery?.name,
      city: o.delivery?.city,
      postalCode: o.delivery?.postalCode,
      statusLabel: o.statusLabel,
    }
  } catch {
    return null
  }
}

/**
 * `POST /scan/outbound`. Cada intento lleva su propia `Idempotency-Key` (dos lecturas del mismo código son dos escaneos).
 * Si el resultado asignó la orden, deja al día pendientes, la ficha de la orden y las rutas.
 */
export function useOutboundScan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: OutboundScanRequest): Promise<ScanResult> =>
      unwrap(api.POST('/api/v1/scan/outbound', { body, headers: { 'Idempotency-Key': crypto.randomUUID() } })),
    onSuccess: (result) => {
      if (result.outcome === 'FOUND_ASSIGNED') {
        void qc.invalidateQueries({ queryKey: scanKeys.unassigned })
        void qc.invalidateQueries({ queryKey: orderKeys.detail })
        void qc.invalidateQueries({ queryKey: orderKeys.list })
        void qc.invalidateQueries({ queryKey: scanKeys.trips })
      }
    },
  })
}
