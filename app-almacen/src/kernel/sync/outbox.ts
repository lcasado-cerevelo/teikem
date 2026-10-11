// Lote 8A-app — cola de subida (docs/mobile/app-almacen-plan.md §1.2). Cada operación de una pantalla es una sola
// llamada atómica al API (decisión del Lote 8A backend: recibo con `confirm:true`, `collect-and-pack`, conteo por
// lote), así que no hay que re-escribir ids entre pasos de un mismo documento: basta con mandarlas en el orden en que
// se crearon y dejar que la clave de idempotencia (Idempotency-Key) proteja contra un doble envío si la app se cierra
// a mitad de la respuesta. Recibir y Despacho tienen una ruta fija (no dependen de nada que solo se sepa en línea);
// Conteo manda a un id de conteo que el servidor ya asignó en línea al escanear la posición, así que su ruta se arma
// al encolar (`path` explícito) en vez de derivarse del tipo.
import { api, ApiError, unwrap } from '../api/client'
import { recordSkippedFromResult } from '../../features/count/countSkipped'
import { getDb } from '../db/database'
import { projectRow, type Delta } from '../warehouse/balanceProjection'

export type OutboxKind = 'receipt' | 'pack' | 'collect' | 'countBatch' | 'countFinish' | 'transfer' | 'adjust' | 'damage' | 'manualIssue'
type OutboxMethod = 'POST' | 'PUT'

interface EnqueueInput {
  kind: OutboxKind
  body: unknown
  /** Requerido para countBatch/countFinish (la ruta lleva el id del conteo); receipt/pack tienen una ruta fija. */
  path?: string
  /** Efecto exacto que la operación ya puso en los saldos locales (despacho manual): se guarda para deshacerlo o volver a sumarlo igual. */
  projection?: readonly Delta[]
}

// La cuenta de pendientes se lee siempre de la base (nunca queda desincronizada), pero el aviso a quien la muestra
// (Inicio, Sincronización) hay que darlo a mano en cada cambio: encolar, cada fila que termina en runOutbox,
// reintentar, descartar.
const listeners = new Set<() => void>()
function notify(): void {
  listeners.forEach((l) => l())
}
export function subscribeOutbox(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export interface OutboxRow {
  id: number
  idempotency_key: string
  kind: string
  method: string
  path: string
  body: string
  created_at_utc: string
  status: string
  attempts: number
  last_error: string | null
  result_json: string | null
  /** schema v11: efecto guardado en los saldos locales (null = se deriva del cuerpo). */
  projection_json?: string | null
}

let counter = 0
function newIdempotencyKey(): string {
  counter += 1
  return `app-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}-${counter}`
}

const DEFAULT_PATH: Partial<Record<OutboxKind, string>> = {
  receipt: '/api/v1/receipts',
  pack: '/api/v1/pick-batches/collect-and-pack',
  // completar el despacho sin empacar: solo recolecta (el inventario sale; sin orden ni empaque)
  collect: '/api/v1/pick-batches',
  // 2026-10-10 (D2b): Transferir, Ajustar y Daño también van a la cola (el saldo local refleja el efecto de inmediato: kernel/warehouse/balanceProjection.ts)
  transfer: '/api/v1/inventory/transfers/in-warehouse',
  adjust: '/api/v1/inventory/adjustments/quantity',
  damage: '/api/v1/damage-reports',
  // 2026-10-11: «Completar despacho» = despacho manual (salida sin entrega con motivo, documento DMA-#####). `collect` queda solo para las filas que
  // un aparato ya tenga en la cola de antes de esta versión.
  manualIssue: '/api/v1/manual-issues',
}
const METHOD: Record<OutboxKind, OutboxMethod> = {
  receipt: 'POST',
  pack: 'POST',
  collect: 'POST',
  countBatch: 'PUT',
  countFinish: 'POST',
  transfer: 'POST',
  adjust: 'POST',
  damage: 'POST',
  manualIssue: 'POST',
}

/** Encola una operación (kind + cuerpo ya armado); devuelve el id local de la fila. */
export function enqueue(input: EnqueueInput): number {
  const path = input.path ?? DEFAULT_PATH[input.kind]
  if (!path) throw new Error(`enqueue: hace falta 'path' para el tipo '${input.kind}' (no tiene una ruta fija).`)
  const key = newIdempotencyKey()
  const info = getDb().runSync(
    `INSERT INTO outbox (idempotency_key, kind, method, path, body, created_at_utc, status, attempts, projection_json)
     VALUES (?, ?, ?, ?, ?, ?, 'pending', 0, ?)`,
    [key, input.kind, METHOD[input.kind], path, JSON.stringify(input.body), new Date().toISOString(), input.projection ? JSON.stringify(input.projection) : null],
  )
  notify()
  return info.lastInsertRowId
}

/** Estado de una fila de la cola (para avisar al operario si ya se envió, la rechazó el servidor o sigue esperando señal). */
export function outboxStatus(id: number): { status: string; error: string | null } | null {
  const row = getDb().getFirstSync<{ status: string; last_error: string | null }>('SELECT status, last_error FROM outbox WHERE id = ?', [id])
  return row ? { status: row.status, error: row.last_error } : null
}

/** Lo que contestó el servidor a una fila ya enviada (p. ej. el despacho manual con su número DMA); null si no se envió o no se guardó. */
export function outboxResult(id: number): unknown {
  const row = getDb().getFirstSync<{ status: string; result_json: string | null }>('SELECT status, result_json FROM outbox WHERE id = ?', [id])
  if (row?.status !== 'sent' || !row.result_json) return null
  try {
    return JSON.parse(row.result_json) as unknown
  } catch {
    return null
  }
}

export function listOutbox(): OutboxRow[] {
  return getDb().getAllSync<OutboxRow>('SELECT * FROM outbox ORDER BY id ASC')
}

export function countPending(): number {
  const row = getDb().getFirstSync<{ n: number }>("SELECT COUNT(*) AS n FROM outbox WHERE status = 'pending'")
  return row?.n ?? 0
}

export function countRejected(): number {
  const row = getDb().getFirstSync<{ n: number }>("SELECT COUNT(*) AS n FROM outbox WHERE status = 'rejected'")
  return row?.n ?? 0
}

/** Reintenta una operación rechazada (el usuario la revisó y quiere volver a intentarla). */
export function retryRow(id: number): void {
  const row = getDb().getFirstSync<OutboxRow>('SELECT * FROM outbox WHERE id = ?', [id])
  getDb().runSync("UPDATE outbox SET status = 'pending', last_error = NULL WHERE id = ?", [id])
  // una operación rechazada ya había deshecho su efecto en los saldos locales: al reintentarla se vuelve a aplicar
  if (row && row.status === 'rejected') projectRow(row, 1)
  notify()
}

/** Descarta una operación rechazada (no se manda más). */
export function discardRow(id: number): void {
  getDb().runSync('DELETE FROM outbox WHERE id = ?', [id])
  notify()
}

async function sendRow(row: OutboxRow): Promise<unknown> {
  const body: unknown = JSON.parse(row.body)
  const headers = { 'Idempotency-Key': row.idempotency_key }
  // Rutas dinámicas (countBatch/countFinish llevan el id del conteo): openapi-fetch solo tipa rutas conocidas en el
  // contrato, así que aquí se sale del tipado para mandar la ruta guardada tal cual.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const client = api as any
  if (row.method === 'PUT') return unwrap(client.PUT(row.path, { body, headers }))
  return unwrap(client.POST(row.path, { body, headers }))
}

/** Rechazo de negocio (dato inválido o que ya no aplica): no tiene sentido reintentar tal cual. */
const REJECTION_CODES = new Set(['bad_request', 'validation', 'forbidden', 'not_found', 'conflict', 'status_rule'])
/** 2026-10-11: el servidor también rechaza con códigos propios (409 `insufficient_stock` del inventario, 403 `module_disabled`…); por el código
 *  quedaban pendientes para siempre (se reintentaban cada minuto y el saldo local nunca se deshacía). Lo que decide es el estatus HTTP. */
const REJECTION_STATUSES = new Set([400, 403, 404, 409, 422])

/** 2026-10-11: el 409 de la idempotencia del servidor «todavía se está procesando» (IdempotencyRules.InFlightMessage en
 *  src/Teikem.Domain/Security/IdempotencyRules.cs; código `conflict`, como cualquier 409) NO es un rechazo: la misma clave
 *  sigue en curso en el servidor (p. ej. el envío anterior se cortó sin respuesta) y el siguiente intento recibe la respuesta
 *  guardada o se ejecuta de nuevo si quedó abandonada. Se reconoce por el título exacto (el API no lo traduce). */
export const IDEMPOTENCY_IN_FLIGHT_TITLE = 'La operación con esta clave todavía se está procesando.'

function isInFlight(err: ApiError): boolean {
  return err.status === 409 && err.title.trim() === IDEMPOTENCY_IN_FLIGHT_TITLE
}

function isRejection(err: ApiError): boolean {
  if (isInFlight(err)) return false
  return REJECTION_CODES.has(err.code) || REJECTION_STATUSES.has(err.status)
}

export interface RunOutboxResult {
  sent: number
  rejected: number
  stoppedForNetwork: boolean
  remaining: number
}

/** Envía las operaciones pendientes en el orden en que se crearon. Se detiene ante la primera sin red (o 401/429,
 *  que afectan a todas las demás igual) para no gastar batería reintentando en vano; un rechazo de negocio marca esa
 *  fila y sigue con las demás, que pueden ser de otro documento. */
export async function runOutbox(): Promise<RunOutboxResult> {
  const db = getDb()
  const rows = db.getAllSync<OutboxRow>("SELECT * FROM outbox WHERE status = 'pending' ORDER BY id ASC")
  let sent = 0
  let rejected = 0
  let stoppedForNetwork = false

  for (const row of rows) {
    try {
      const result = await sendRow(row)
      db.runSync("UPDATE outbox SET status = 'sent', attempts = attempts + 1, result_json = ? WHERE id = ?", [
        JSON.stringify(result ?? null),
        row.id,
      ])
      sent += 1
      // Lote A7: un lote de conteo con líneas ya corregidas por el supervisor llega 200 con `skippedLines`; la fila queda
      // `sent` y lo omitido se guarda como aviso persistente (Sincronización lo muestra hasta que se descarte).
      if (row.kind === 'countBatch') {
        try {
          recordSkippedFromResult(row.id, row.path, result)
        } catch {
          // el aviso es un extra: si no se pudo guardar, el envío ya quedó enviado y la cola sigue con las demás
        }
      }
    } catch (err) {
      if (!(err instanceof ApiError)) throw err
      if (err.code === 'network') {
        stoppedForNetwork = true
        break
      }
      if (err.status === 401 || err.code === 'rate_limited') {
        db.runSync('UPDATE outbox SET attempts = attempts + 1 WHERE id = ?', [row.id])
        break
      }
      if (isRejection(err)) {
        db.runSync("UPDATE outbox SET status = 'rejected', attempts = attempts + 1, last_error = ? WHERE id = ?", [
          err.title,
          row.id,
        ])
        // el servidor no la aceptó: el efecto que se había puesto en los saldos locales se deshace
        projectRow(row, -1)
        rejected += 1
        continue
      }
      // 5xx, 409 «todavía se está procesando» u otro error transitorio: se cuenta el intento, la fila sigue pendiente (el
      // saldo local no se deshace) y se sigue con las demás (documentos independientes).
      db.runSync('UPDATE outbox SET attempts = attempts + 1, last_error = ? WHERE id = ?', [err.title, row.id])
    }
  }
  if (sent > 0 || rejected > 0) notify()
  return { sent, rejected, stoppedForNetwork, remaining: countPending() }
}
