// 2026-10-07 — conteos por posición que ESTE usuario dejó abiertos en el servidor (guardó y siguió después, o cerró la app sin terminar). Inicio los
// usa para bloquear las demás acciones («si hay un conteo pendiente, no se hace nada más») y Conteo los lista con «Continuar». La verdad está en el
// servidor (GET /cycle-counts/page, estatus OPEN y asignado al usuario); aquí se guarda una copia en la base de la compañía para saberlo también sin
// señal, y se corrige en cuanto hay red. Un conteo cuyo cierre ya está en la cola de salida no cuenta como abierto.
import { api, unwrap } from '../../kernel/api/client'
import { getKv, KvKeys, setKv } from '../../kernel/db/kv'
import { listOutbox } from '../../kernel/sync/outbox'

export interface OpenCountHint {
  countId: number
  number: string
  binCode: string
  warehousePublicId: string
  userId: number
  /** Líneas del conteo y cuántas lleva contadas (para mostrar «12 de 33»). */
  lines: number
  counted: number
}

function readAll(): OpenCountHint[] {
  try {
    const raw = getKv(KvKeys.openCountHints)
    return raw ? (JSON.parse(raw) as OpenCountHint[]) : []
  } catch {
    return []
  }
}

function writeAll(list: OpenCountHint[]): void {
  setKv(KvKeys.openCountHints, JSON.stringify(list))
}

/** Los conteos abiertos de este usuario en este almacén (copia guardada; no llama al servidor). */
export function getOpenCountHints(warehousePublicId: string | null, userId: number | null | undefined): OpenCountHint[] {
  if (!warehousePublicId || userId == null) return []
  return readAll().filter((h) => h.warehousePublicId === warehousePublicId && h.userId === userId)
}

export function addOpenCountHint(hint: OpenCountHint): void {
  writeAll([...readAll().filter((h) => h.countId !== hint.countId), hint])
}

export function removeOpenCountHint(countId: number): void {
  writeAll(readAll().filter((h) => h.countId !== countId))
}

/** ¿Hay en la cola de salida un cierre (finish) de ese conteo que todavía no se mandó? */
function finishQueued(countId: number): boolean {
  const suffix = `/cycle-counts/${countId}/finish`
  return listOutbox().some((r) => r.kind === 'countFinish' && r.status === 'pending' && r.path.endsWith(suffix))
}

/** Pregunta al servidor qué conteos tiene abiertos este usuario en el almacén y deja la copia al día. Sin señal (o sin permiso) devuelve la copia. */
export async function refreshOpenCountHints(warehousePublicId: string, userId: number): Promise<OpenCountHint[]> {
  try {
    const page = await unwrap(
      api.GET('/api/v1/cycle-counts/page', {
        params: { query: { warehousePublicIds: [warehousePublicId], status: ['OPEN'], origins: ['MANUAL', 'CHANGES'], take: 200 } },
      }),
    )
    const mine: OpenCountHint[] = (page.items ?? [])
      .filter((c) => c.assignedToUserId === userId && c.binCount === 1 && (c.id ?? 0) > 0 && !finishQueued(c.id ?? 0))
      .map((c) => ({
        countId: c.id ?? 0,
        number: c.number ?? '',
        binCode: c.binCode ?? '',
        warehousePublicId,
        userId,
        lines: c.lineCount ?? 0,
        counted: c.countedLines ?? 0,
      }))
    writeAll([...readAll().filter((h) => !(h.warehousePublicId === warehousePublicId && h.userId === userId)), ...mine])
    return mine
  } catch {
    return getOpenCountHints(warehousePublicId, userId)
  }
}

/** Mensaje del servidor si el cierre (o el lote) de ese conteo fue RECHAZADO al mandarlo; null si no hubo rechazo. Se mira justo después de sincronizar,
 *  para que quien terminó se entere en el momento y no por la pantalla de Sincronización. */
export function rejectionOfCount(countId: number): string | null {
  const marker = `/cycle-counts/${countId}/`
  const row = listOutbox()
    .filter((r) => r.status === 'rejected' && r.path.includes(marker))
    .pop()
  return row ? (row.last_error ?? '') || null : null
}
