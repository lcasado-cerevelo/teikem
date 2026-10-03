// Lote A8 — permisos efectivos de quien entró con el PIN (`GET /api/v1/me`, `permissions`), para las pocas decisiones de
// pantalla que el servidor no toma por la app. Hoy solo una: ver las cantidades del sistema en la lista de lo que hay en una
// posición (Consultar). La regla es la del conteo (docs/mobile/mejoras-ux-zebra.md, "lo que NO cambia"): sin `warehouse.count`
// no se ven las cantidades del sistema. En el conteo el servidor ya las omite (`systemQty` vacío); los saldos de
// `/inventory/balances` en cambio las traen siempre, así que aquí la app tiene que saber el permiso.
//
// Se guardan en la base de la compañía (kv `myPermissions`, por usuario) para decidir sin señal. Si nunca se pudieron traer, la
// respuesta es "sin permiso" (no se muestra lo que no se sabe si se puede mostrar).
import { useEffect, useMemo, useState } from 'react'

import { api, unwrap } from '../api/client'
import { getKv, KvKeys, setKv } from '../db/kv'
import { getSessionState } from './session'
import { useSession } from './useSession'

/** Códigos exactos del API (PermissionCatalog). */
export const Perm = {
  /** Conteo informado: ver las cantidades del sistema y cancelar un conteo. Sin él, conteo a ciegas. */
  warehouseCount: 'warehouse.count',
} as const

type Stored = Record<string, { permissions: string[]; fetchedAtUtc: string }>

function readStored(): Stored {
  try {
    const raw = getKv(KvKeys.myPermissions)
    const parsed = raw ? (JSON.parse(raw) as unknown) : null
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Stored) : {}
  } catch {
    return {}
  }
}

/** Permisos guardados de ese usuario en esta compañía; null si nunca se trajeron. */
export function getCachedPermissions(userId: number): string[] | null {
  const entry = readStored()[String(userId)]
  return Array.isArray(entry?.permissions) ? entry.permissions.filter((p): p is string => typeof p === 'string') : null
}

function storePermissions(userId: number, permissions: string[]): void {
  const all = readStored()
  all[String(userId)] = { permissions, fetchedAtUtc: new Date().toISOString() }
  setKv(KvKeys.myPermissions, JSON.stringify(all))
}

/** Trae los permisos de quien está dentro y los guarda. null si no hay sesión o la llamada falló (se conservan los guardados). */
export async function refreshMyPermissions(): Promise<string[] | null> {
  const { device, session } = getSessionState()
  if (!device || !session) return null
  try {
    const me = await unwrap(api.GET('/api/v1/me'))
    const permissions = (me.permissions ?? []).filter((p): p is string => typeof p === 'string')
    storePermissions(session.userId, permissions)
    return permissions
  } catch {
    return null
  }
}

/** Regla del dueño: las cantidades del sistema solo con `warehouse.count`. Sin saber los permisos (null) → no. */
export function canSeeSystemQty(permissions: readonly string[] | null): boolean {
  return permissions?.includes(Perm.warehouseCount) ?? false
}

/** Permisos de quien está dentro: los guardados al instante y, al montar, los del servidor (se vuelve a pintar al llegar). */
export function useMyPermissions(): string[] | null {
  const { session } = useSession()
  const userId = session?.userId ?? null
  // lo que trajo el servidor en esta pantalla (de ese usuario); mientras no llega, lo guardado en el aparato
  const [fresh, setFresh] = useState<{ userId: number; permissions: string[] } | null>(null)
  const cached = useMemo(() => (userId != null ? getCachedPermissions(userId) : null), [userId])
  useEffect(() => {
    if (userId == null) return
    let cancelled = false
    void refreshMyPermissions().then((permissions) => {
      if (!cancelled && permissions) setFresh({ userId, permissions })
    })
    return () => {
      cancelled = true
    }
  }, [userId])
  if (userId == null) return null
  return fresh?.userId === userId ? fresh.permissions : cached
}
