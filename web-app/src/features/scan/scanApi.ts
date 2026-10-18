// Lote F-E1 — Datos de la Estación de escaneo (manual 05 §7 y §3.3). Escaneo de salida: `trips.scan` + LTL_GROUND (el servidor
// responde SIEMPRE 200 con un resultado tipado; 400 solo por el código). Pendientes: `trips.view`. Zonas: `fleet.view` (módulo
// CATALOG); ficha de la orden: `orders.view`. Las lecturas secundarias no sacan al usuario de la pantalla (`handleAccessDenied: false`).
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components, paths } from '../../kernel/api/schema'
import { orderKeys } from '../orders/api'
import type { KnownOrder, ScanResult } from './scanRules'

export type UnassignedQuery = NonNullable<paths['/api/v1/trips/unassigned-orders']['get']['parameters']['query']>
export type ScanActionRequest = components['schemas']['ScanActionRequest']
export type ScanActionResult = components['schemas']['ScanActionResultDto']
/** Las cuatro acciones de la estación (Lote 107). */
export const SCAN_ACTIONS = ['PICKUP', 'ARRIVE', 'ROUTE', 'DEPART'] as const
export type ScanActionKey = (typeof SCAN_ACTIONS)[number]

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
 * Lote 107: el resultado de la acción se pinta con la misma caja de siempre: aplicada → «found»; ya más adelante o final → «dup»; no encontrada → «notfound»; Enrutar sin ruta → encontrada sin asignar.
 * (Un resultado que ya traiga un outcome del escaneo de salida —FOUND_*, ALREADY_ASSIGNED…— pasa tal cual.)
 */
export function toScanResult(r: ScanActionResult): ScanResult {
  const outcome =
    r.outcome === 'APPLIED' ? 'FOUND_ASSIGNED'
    : r.outcome === 'COUNTED' ? 'FOUND_ASSIGNED'   // Lote 110: la pieza se contó (la orden ya estaba en ese estatus)
    : r.outcome === 'ALREADY_BEYOND' || r.outcome === 'FINAL' || r.outcome === 'DUPLICATE' ? 'ALREADY_ASSIGNED'
    : r.outcome === 'NO_ROUTE' ? 'FOUND_UNASSIGNED'
    : r.outcome === 'NOT_FOUND' || r.outcome === 'AMBIGUOUS' ? 'NOT_FOUND'
    : r.outcome
  return { matchedBy: null, ...r, outcome } as unknown as ScanResult
}

/**
 * `POST /scan/action` (Lote 107): el usuario escoge la acción (recoger, llegó, enrutar, salir) y cada escaneo lleva la orden a ese estatus, brincando los de en medio. Cada intento lleva su propia `Idempotency-Key`.
 * Si el resultado cambió la orden, deja al día pendientes, la ficha de la orden y las rutas.
 */
export function useScanAction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (body: ScanActionRequest): Promise<ScanResult> =>
      toScanResult(await unwrap(api.POST('/api/v1/scan/action', { body, headers: { 'Idempotency-Key': crypto.randomUUID() } }))),
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

/** `POST /scan/close-count` (Lote 110): cierra el conteo de una orden; lo que no se escaneó queda faltante «Por verificar». */
export function useCloseCount() {
  return useMutation({
    mutationFn: (v: { orderPublicId: string; confirm?: boolean }) => unwrap(api.POST('/api/v1/scan/close-count', { body: v.confirm ? { orderPublicId: v.orderPublicId, confirm: true } : { orderPublicId: v.orderPublicId }, headers: { 'Idempotency-Key': crypto.randomUUID() } })),
  })
}
