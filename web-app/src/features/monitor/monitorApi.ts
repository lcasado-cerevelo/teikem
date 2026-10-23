// Lote F-E3 — Datos del Monitoreo de ruta. Solo lecturas (`trips.view`, módulo LTL_GROUND): `GET /trips/monitor` (rutas del día con avance, próxima
// llegada y último ping) y `GET /trips/{id}` (paradas con punto, estado y ventanas para el plano). No hay escrituras: el monitoreo es de solo lectura.
// El sondeo lo maneja la pantalla (`useVisibleInterval`), no TanStack: así se pausa con la pestaña oculta y se prueba con temporizadores falsos.
import { useEffect, useRef, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { dispatchKeys } from '../dispatch/dispatchApi'
import { buildMonitorQuery } from './monitorRules'

/** `GET /trips/monitor`. Con error de red conserva el último dato (`data` sigue ahí) y la pantalla avisa. */
export function useMonitor(filters: { date: string; zoneId: string; search: string; includeCompleted: boolean }, enabled: boolean) {
  const query = buildMonitorQuery(filters)
  return useQuery({
    queryKey: [dispatchKeys.monitor[0], 'screen', query],
    queryFn: () => unwrap(api.GET('/api/v1/trips/monitor', { params: { query } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

/** La ruta elegida con sus paradas (misma clave que la Sala de despacho: comparten caché). */
export function useMonitorTrip(publicId: string | null, enabled: boolean) {
  return useQuery({
    queryKey: [dispatchKeys.detail[0], publicId],
    queryFn: () => unwrap(api.GET('/api/v1/trips/{publicId}', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: enabled && !!publicId,
    retry: false,
    meta: { handleAccessDenied: false },
  })
}

/** El recorrido de la ruta (puntos de GPS del chofer, hasta 5,000) y sus avisos de seguimiento (apagó el envío, perdió el permiso, lo reanudó). Se conserva 10 días. */
export function useTripTracking(publicId: string | null, enabled: boolean) {
  return useQuery({
    queryKey: ['monitor', 'tracking', publicId],
    queryFn: () => unwrap(api.GET('/api/v1/trips/{publicId}/tracking', { params: { path: { publicId: publicId ?? '' } } })),
    enabled: enabled && !!publicId,
    retry: false,
    refetchInterval: 60_000,
    meta: { handleAccessDenied: false },
  })
}

/** ¿La pestaña está a la vista? (`visibilitychange`; sin `document` se asume que sí). */
export function usePageVisible(): boolean {
  const [visible, setVisible] = useState(() => typeof document === 'undefined' || document.visibilityState !== 'hidden')
  useEffect(() => {
    const on = () => setVisible(document.visibilityState !== 'hidden')
    document.addEventListener('visibilitychange', on)
    return () => document.removeEventListener('visibilitychange', on)
  }, [])
  return visible
}

/**
 * Llama `run` cada `ms` mientras `enabled` y la pestaña esté visible. Al volver a verla, si pasó más de `ms` desde la última vez, refresca
 * al instante (sin esperar al siguiente ciclo). Oculta no hay temporizador: no se consulta al servidor sin que nadie mire.
 */
export function useVisibleInterval(run: () => void, ms: number, enabled: boolean) {
  const visible = usePageVisible()
  const latest = useRef(run)
  const last = useRef<number | null>(null)
  useEffect(() => {
    latest.current = run
  })
  useEffect(() => {
    if (!enabled || !visible) return
    if (last.current === null) last.current = Date.now()
    if (last.current !== null && Date.now() - last.current > ms) {
      last.current = Date.now()
      latest.current()
    }
    const id = window.setInterval(() => {
      last.current = Date.now()
      latest.current()
    }, ms)
    return () => window.clearInterval(id)
  }, [enabled, visible, ms])
}

/** Reloj de la pantalla: `Date.now()` que se actualiza cada `ms` mientras la pestaña esté visible (para «hace N s»). */
export function useNow(ms = 1000): number {
  const visible = usePageVisible()
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!visible) return
    const id = window.setInterval(() => setNow(Date.now()), ms)
    return () => window.clearInterval(id)
  }, [visible, ms])
  return now
}
