// Lote A6 — rechazo de una captura de conteo porque el supervisor ya corrigió una línea (decisión del dueño 2026-10-03, cambio 2;
// docs/decisiones-del-dueno-2026-10-03.md "Pendientes"). El servidor responde 409 a `PUT /cycle-counts/{id}/lines/batch` con
//   "La línea ya fue corregida por el supervisor; no se puede volver a capturar. Renglón(es) del lote: 2 (SKU-A), 5 (SKU-B). No se guardó nada."
// (CycleCountRules.CorrectedLineLocked + CycleCountService.CaptureBatchAsync, todo o nada). La cola de salida guarda solo el
// texto (`last_error` = title del ProblemDetails), no el código HTTP, así que el rechazo se reconoce por ese mensaje exacto del
// contrato. Funciones puras: sin base ni red (la consulta del conteo vigente está en countRejectionApi.ts).

/** Mensaje exacto del servidor (CycleCountRules.CorrectedLineLocked). Si cambia allá, hay que cambiarlo aquí. */
export const CORRECTED_LINE_LOCKED = 'La línea ya fue corregida por el supervisor; no se puede volver a capturar.'

const ROWS_PREFIX = 'Renglón(es) del lote: '
const ROWS_SUFFIX = '. No se guardó nada.'
/** Lista completa "n (SKU), n (SKU)": el SKU no puede llevar paréntesis (si los llevara, no se interpreta y se muestra el texto tal cual). */
const ROW_LIST = /^\d{1,4} \([^()]+\)(?:, \d{1,4} \([^()]+\))*$/
const ROW_ITEM = /(\d{1,4}) \(([^()]+)\)/g
/** Más renglones que esto no caben en un lote (el servidor admite 1000 líneas por conteo). */
const MAX_ROWS = 1000

export interface LockedRow {
  /** Número de renglón dentro del lote enviado (1 = el primero). */
  row: number
  sku: string
  /** Lo que capturó el operario en ese renglón (del cuerpo guardado en la cola), si se puede leer. */
  capturedQty: number | null
}

export interface CorrectedLineRejection {
  /** Id del conteo (de la ruta guardada en la cola); null si no se reconoce la ruta. */
  countId: number | null
  /** Renglones bloqueados; null si el mensaje no trae la lista en el formato esperado (se muestra `message` tal cual). */
  rows: LockedRow[] | null
  /** Mensaje del servidor tal cual. */
  message: string
}

/** Lo mínimo de una fila de la cola de salida (kernel/sync/outbox.ts, OutboxRow) que hace falta para clasificarla. */
export interface RejectedRowLike {
  kind: string
  path: string
  body: string
  last_error: string | null
}

/** Id del conteo de una ruta `/api/v1/cycle-counts/{id}/lines/batch`; null si la ruta no es esa. */
export function countIdFromBatchPath(path: string): number | null {
  const m = /^\/api\/v1\/cycle-counts\/(\d{1,10})\/lines\/batch$/.exec(path.trim())
  if (!m) return null
  const id = Number(m[1])
  return Number.isSafeInteger(id) && id > 0 ? id : null
}

/** Id del conteo de una ruta `/api/v1/cycle-counts/{id}/finish` (el cierre que se encola detrás del lote); null si no es esa. */
export function countIdFromFinishPath(path: string): number | null {
  const m = /^\/api\/v1\/cycle-counts\/(\d{1,10})\/finish$/.exec(path.trim())
  if (!m) return null
  const id = Number(m[1])
  return Number.isSafeInteger(id) && id > 0 ? id : null
}

/** true si el mensaje es el 409 de "línea corregida por el supervisor" (con o sin la lista de renglones). */
export function isCorrectedLineLockedMessage(message: string | null | undefined): boolean {
  return typeof message === 'string' && message.trim().startsWith(CORRECTED_LINE_LOCKED)
}

/**
 * Extrae "n (SKU)" del mensaje del lote. Solo si TODO el tramo entre "Renglón(es) del lote: " y ". No se guardó nada." tiene el
 * formato esperado; cualquier otra cosa → null (la pantalla muestra el mensaje tal cual, sin adivinar).
 */
export function parseLockedRows(message: string): Array<{ row: number; sku: string }> | null {
  const text = message.trim()
  if (!text.startsWith(CORRECTED_LINE_LOCKED) || !text.endsWith(ROWS_SUFFIX)) return null
  const rest = text.slice(CORRECTED_LINE_LOCKED.length).trimStart()
  if (!rest.startsWith(ROWS_PREFIX)) return null
  const list = rest.slice(ROWS_PREFIX.length, rest.length - ROWS_SUFFIX.length)
  if (!ROW_LIST.test(list)) return null
  const rows = [...list.matchAll(ROW_ITEM)].map((m) => ({ row: Number(m[1]), sku: m[2].trim() }))
  if (rows.length === 0 || rows.length > MAX_ROWS || rows.some((r) => r.row < 1 || r.sku === '')) return null
  return rows
}

/** Cantidad que el operario mandó en cada renglón del lote (cuerpo `{ lines: [{ countedQty }] }` guardado en la cola). */
function capturedQtys(body: string): Array<number | null> | null {
  try {
    const parsed: unknown = JSON.parse(body)
    if (typeof parsed !== 'object' || parsed === null) return null
    const lines = (parsed as { lines?: unknown }).lines
    if (!Array.isArray(lines)) return null
    return lines.map((l: unknown) => {
      const qty = typeof l === 'object' && l !== null ? (l as { countedQty?: unknown }).countedQty : undefined
      return typeof qty === 'number' && Number.isFinite(qty) ? qty : null
    })
  } catch {
    return null
  }
}

/** Clasifica una fila rechazada: el 409 de captura de conteo (`countBatch`) por línea corregida, o null si es otro rechazo. */
export function classifyCountBatchRejection(row: RejectedRowLike): CorrectedLineRejection | null {
  if (row.kind !== 'countBatch' || !isCorrectedLineLockedMessage(row.last_error)) return null
  const message = (row.last_error ?? '').trim()
  const parsed = parseLockedRows(message)
  const qtys = parsed ? capturedQtys(row.body) : null
  const rows = parsed ? parsed.map((r) => ({ ...r, capturedQty: qtys?.[r.row - 1] ?? null })) : null
  return { countId: countIdFromBatchPath(row.path), rows, message }
}

// ------------------------------------------------------------------ estado vigente del conteo (tras "Actualizar el conteo")

/** Estado del conteo según GET /cycle-counts/{id}: abierto (Pendiente), terminado de contar (Contado, aún sin reconciliar),
 *  cerrado (reconciliado) o que ya no existe (404 o dado de baja). */
export type CountRefreshState = 'open' | 'counted' | 'closed' | 'notFound'

/** Estatus del servidor (CycleCountStatuses) → estado que entiende la pantalla. Un código desconocido se trata como cerrado
 *  (lo más seguro: no invita a volver a capturar). */
export function countRefreshState(statusCode: string | null | undefined, isActive: boolean | null | undefined): CountRefreshState {
  if (isActive === false) return 'notFound'
  switch ((statusCode ?? '').toUpperCase()) {
    case 'OPEN':
      return 'open'
    case 'COUNTED':
      return 'counted'
    default:
      return 'closed'
  }
}

export interface RefreshedLine {
  lineId: number
  sku: string
  productName: string
  binCode: string
  lotNumber: string | null
  /** Valor vigente del servidor (null = sin contar). Nunca la cantidad esperada: esa no se muestra aquí. */
  countedQty: number | null
  wasCorrected: boolean
  correctedByName: string | null
}

export interface RefreshSummary {
  total: number
  corrected: number
  uncounted: number
}

export function summarizeRefreshedLines(lines: RefreshedLine[]): RefreshSummary {
  return {
    total: lines.length,
    corrected: lines.filter((l) => l.wasCorrected).length,
    uncounted: lines.filter((l) => l.countedQty === null).length,
  }
}

/** Claves i18n del aviso de estado (es/en en kernel/i18n). */
export function refreshStateKey(state: CountRefreshState): string {
  return `countRejection.state${state.charAt(0).toUpperCase()}${state.slice(1)}`
}
