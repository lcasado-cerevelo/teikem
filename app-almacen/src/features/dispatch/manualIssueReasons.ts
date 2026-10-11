// 2026-10-11 — Motivos del despacho manual en el aparato: copia local de GET /api/v1/manual-issues/reasons (los activos y habilitados para la compañía,
// con el nombre que ella les haya puesto) en la base de la compañía (kv `manualIssueReasons`), para escogerlos sin señal. La baja la sincronización
// (kernel/sync/download.ts) como mucho cada REASONS_REFRESH_MINUTES y SOLO si quien está dentro tiene warehouse.issue según sus permisos guardados (el
// servidor contesta 403 sin él y deja un evento PERMISSION_DENIED: preguntar en cada pasada llenaría la bitácora de seguridad). Un 403 o 404 se salta sin
// borrar la copia (es de la compañía, no del usuario). Si nunca se bajaron, la pantalla ofrece los cinco de fábrica (manualIssueLogic.ts).
import { api, ApiError, unwrap } from '../../kernel/api/client'
import { getCachedPermissions } from '../../kernel/auth/permissions'
import { getSessionState } from '../../kernel/auth/session'
import { getKv, KvKeys, setKv } from '../../kernel/db/kv'
import { WAREHOUSE_ISSUE, type StoredReason } from './manualIssueLogic'

/** Cada cuánto se vuelven a pedir (minutos): cambian muy poco y la sincronización corre cada minuto. */
export const REASONS_REFRESH_MINUTES = 30

interface StoredCopy {
  reasons: StoredReason[]
  fetchedAtUtc: string
}

function readCopy(): StoredCopy | null {
  try {
    const raw = getKv(KvKeys.manualIssueReasons)
    const parsed = raw ? (JSON.parse(raw) as unknown) : null
    if (!parsed || typeof parsed !== 'object' || !Array.isArray((parsed as StoredCopy).reasons)) return null
    return parsed as StoredCopy
  } catch {
    return null
  }
}

/** Los motivos guardados en el aparato; null si nunca se bajaron (→ los de fábrica). */
export function readManualIssueReasons(): StoredReason[] | null {
  return readCopy()?.reasons ?? null
}

/** Una fila de LookupValueDto → lo que guarda el aparato. Los deshabilitados o inactivos no se guardan (el servidor ya no los manda, por si acaso). */
export function mapReason(r: { code?: string | null; label?: string | null; labels?: Record<string, string> | null; sortOrder?: number; isEnabled?: boolean; isActive?: boolean }): StoredReason | null {
  const code = r.code?.trim()
  if (!code || r.isEnabled === false || r.isActive === false) return null
  return { code, label: r.label ?? code, labels: r.labels ?? {}, sortOrder: r.sortOrder ?? 0 }
}

/** ¿Quien está dentro tiene warehouse.issue (según los permisos guardados en el aparato)? Sin saberlo, no. */
function userCanIssue(): boolean {
  const { session } = getSessionState()
  if (!session) return false
  return getCachedPermissions(session.userId)?.includes(WAREHOUSE_ISSUE) ?? false
}

export interface ReasonsDownload {
  resource: 'manualIssueReasons'
  pages: number
  items: number
}

/** Baja los motivos (salvo que la copia tenga menos de REASONS_REFRESH_MINUTES y no se pida `force`). 403 (sin permiso) o 404 (servidor viejo): se
 *  salta y se conserva la copia que hubiera. */
export async function downloadManualIssueReasons(force = false): Promise<ReasonsDownload> {
  if (!userCanIssue()) return { resource: 'manualIssueReasons', pages: 0, items: 0 }
  const copy = readCopy()
  if (!force && copy && Date.now() - new Date(copy.fetchedAtUtc).getTime() < REASONS_REFRESH_MINUTES * 60_000) {
    return { resource: 'manualIssueReasons', pages: 0, items: 0 }
  }
  try {
    const rows = await unwrap(api.GET('/api/v1/manual-issues/reasons'))
    const reasons = (Array.isArray(rows) ? rows : []).map(mapReason).filter((r): r is StoredReason => r !== null)
    setKv(KvKeys.manualIssueReasons, JSON.stringify({ reasons, fetchedAtUtc: new Date().toISOString() } satisfies StoredCopy))
    return { resource: 'manualIssueReasons', pages: 1, items: reasons.length }
  } catch (err) {
    // 403: sin warehouse.issue (o sin el módulo); 404: un servidor anterior al despacho manual. Ninguno es una falla de sincronización.
    if (!(err instanceof ApiError) || (err.status !== 403 && err.status !== 404)) throw err
    return { resource: 'manualIssueReasons', pages: 0, items: 0 }
  }
}
