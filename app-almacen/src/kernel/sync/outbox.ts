// Lote 8A-app — cola de subida (docs/mobile/app-almacen-plan.md §1.2). Cada operación de una pantalla es una sola
// llamada atómica al API (decisión del Lote 8A backend: recibo con `confirm:true`, `collect-and-pack`, conteo por
// lote), así que no hay que re-escribir ids entre pasos de un mismo documento: basta con mandarlas en el orden en que
// se crearon y dejar que la clave de idempotencia (Idempotency-Key) proteja contra un doble envío si la app se cierra
// a mitad de la respuesta. Recibir y Despacho tienen una ruta fija (no dependen de nada que solo se sepa en línea);
// Conteo manda a un id de conteo que el servidor ya asignó en línea al escanear la posición, así que su ruta se arma
// al encolar (`path` explícito) en vez de derivarse del tipo.
import { api, ApiError, unwrap } from '../api/client'
import { getDb } from '../db/database'

export type OutboxKind = 'receipt' | 'pack' | 'countBatch' | 'countFinish'
type OutboxMethod = 'POST' | 'PUT'

interface EnqueueInput {
  kind: OutboxKind
  body: unknown
  /** Requerido para countBatch/countFinish (la ruta lleva el id del conteo); receipt/pack tienen una ruta fija. */
  path?: string
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
}

let counter = 0
function newIdempotencyKey(): string {
  counter += 1
  return `app-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}-${counter}`
}

const DEFAULT_PATH: Partial<Record<OutboxKind, string>> = {
  receipt: '/api/v1/receipts',
  pack: '/api/v1/pick-batches/collect-and-pack',
}
const METHOD: Record<OutboxKind, OutboxMethod> = { receipt: 'POST', pack: 'POST', countBatch: 'PUT', countFinish: 'POST' }

/** Encola una operación (kind + cuerpo ya armado); devuelve el id local de la fila. */
export function enqueue(input: EnqueueInput): number {
  const path = input.path ?? DEFAULT_PATH[input.kind]
  if (!path) throw new Error(`enqueue: hace falta 'path' para el tipo '${input.kind}' (no tiene una ruta fija).`)
  const key = newIdempotencyKey()
  const info = getDb().runSync(
    `INSERT INTO outbox (idempotency_key, kind, method, path, body, created_at_utc, status, attempts)
     VALUES (?, ?, ?, ?, ?, ?, 'pending', 0)`,
    [key, input.kind, METHOD[input.kind], path, JSON.stringify(input.body), new Date().toISOString()],
  )
  notify()
  return info.lastInsertRowId
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
  getDb().runSync("UPDATE outbox SET status = 'pending', last_error = NULL WHERE id = ?", [id])
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
      if (REJECTION_CODES.has(err.code)) {
        db.runSync("UPDATE outbox SET status = 'rejected', attempts = attempts + 1, last_error = ? WHERE id = ?", [
          err.title,
          row.id,
        ])
        rejected += 1
        continue
      }
      // 5xx u otro error transitorio: se cuenta el intento y se sigue con las demás (documentos independientes).
      db.runSync('UPDATE outbox SET attempts = attempts + 1, last_error = ? WHERE id = ?', [err.title, row.id])
    }
  }
  if (sent > 0 || rejected > 0) notify()
  return { sent, rejected, stoppedForNetwork, remaining: countPending() }
}
