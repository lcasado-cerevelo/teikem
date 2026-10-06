// Tarea 25 — conteo informado al capturar (app). Tras ACEPTAR la cantidad de una línea (con señal) se le pregunta al servidor si coincide con lo esperado,
// sin que el contador lo haya visto antes: MATCH (cerrada), RECOUNT (volver a contar; nunca trae el esperado) o FINAL (ya recontó; cerrada). El servidor
// decide si este usuario puede verlo (403 = no; entonces se captura como siempre) y tras verificar solo acepta la última cifra verificada.
// Sin señal no hay verificación: la línea se captura a ciegas, como antes. Lógica pura + una llamada; la pantalla solo muestra el resultado.
import { api, ApiError, unwrap } from '../../kernel/api/client'
import { isNetworkError } from '../../kernel/api/problem'

export type CheckState = 'MATCH' | 'RECOUNT' | 'FINAL'

export interface CheckResult {
  state: CheckState
  matches: boolean
  countedQty: number
  /** Solo con MATCH/FINAL y si la compañía muestra el número. */
  expectedQty: number | null
}

export type CheckOutcome =
  | { kind: 'result'; result: CheckResult }
  /** El servidor no deja a este usuario ver lo esperado (403): se captura sin verificar. */
  | { kind: 'off' }
  /** Sin señal: se captura sin verificar. */
  | { kind: 'offline' }
  /** La línea ya estaba cerrada o corregida en el servidor (409), u otro rechazo con su mensaje. */
  | { kind: 'rejected'; message: string }

/** POST /cycle-counts/{id}/lines/{lineId}/check con la cantidad que se acaba de aceptar. */
export async function checkCountLine(countId: number, lineId: number, countedQty: number): Promise<CheckOutcome> {
  try {
    const dto = await unwrap(api.POST('/api/v1/cycle-counts/{id}/lines/{lineId}/check', { params: { path: { id: countId, lineId } }, body: { countedQty } }))
    const state = dto.state === 'MATCH' || dto.state === 'FINAL' ? dto.state : 'RECOUNT'
    return { kind: 'result', result: { state, matches: dto.matches ?? false, countedQty: dto.countedQty ?? countedQty, expectedQty: dto.expectedQty ?? null } }
  } catch (err) {
    if (isNetworkError(err)) return { kind: 'offline' }
    if (err instanceof ApiError) {
      if (err.status === 403) return { kind: 'off' }
      return { kind: 'rejected', message: err.title }
    }
    throw err
  }
}

/** ¿La línea ya no se puede cambiar? (MATCH o FINAL: el servidor solo aceptaría la cifra verificada). */
export function isClosedState(state: CheckState | undefined): boolean {
  return state === 'MATCH' || state === 'FINAL'
}

/**
 * Qué mostrar tras verificar. RECOUNT → pedir recontar (sin decir nada del esperado); MATCH → "Coincide" (con el número si viene); FINAL → "contaste X y
 * se esperaba Y" (con número) o "No coincide" (sin número), o "Coincide" si la segunda cifra sí quedó dentro del margen.
 */
export type RevealMessage =
  | { key: 'count.revealRecount'; tone: 'warn'; params: Record<string, never> }
  | { key: 'count.revealMatch' | 'count.revealMatchNumber'; tone: 'ok'; params: { counted: number; expected: number } | Record<string, never> }
  | { key: 'count.revealFinalNumber' | 'count.revealFinalNoNumber'; tone: 'warn'; params: { counted: number; expected: number } | Record<string, never> }

export function revealMessage(result: CheckResult): RevealMessage {
  if (result.state === 'RECOUNT') return { key: 'count.revealRecount', tone: 'warn', params: {} }
  if (result.matches) {
    return result.expectedQty != null
      ? { key: 'count.revealMatchNumber', tone: 'ok', params: { counted: result.countedQty, expected: result.expectedQty } }
      : { key: 'count.revealMatch', tone: 'ok', params: {} }
  }
  return result.expectedQty != null
    ? { key: 'count.revealFinalNumber', tone: 'warn', params: { counted: result.countedQty, expected: result.expectedQty } }
    : { key: 'count.revealFinalNoNumber', tone: 'warn', params: {} }
}

/**
 * Líneas verificadas en esta sesión de la app (lineId → estado). Sirve para no dejar editar ni quitar una línea cerrada y para saber que el contador ya
 * no puede cambiar su cifra; el servidor lo vuelve a exigir al guardar (409), esto solo evita el rechazo.
 */
const checked = new Map<number, CheckState>()
/** Conteos donde el servidor dijo que no se verifica (403): no se vuelve a preguntar en toda esa sesión de conteo. */
const off = new Set<number>()

export function rememberCheck(lineId: number, state: CheckState): void {
  checked.set(lineId, state)
}
export function checkStateOf(lineId: number | null | undefined): CheckState | undefined {
  return lineId == null ? undefined : checked.get(lineId)
}
export function markCountOff(countId: number): void {
  off.add(countId)
}
export function isCountOff(countId: number): boolean {
  return off.has(countId)
}
/** Al terminar o cancelar el conteo se olvida todo lo de esa sesión. */
export function forgetChecks(): void {
  checked.clear()
  off.clear()
}
