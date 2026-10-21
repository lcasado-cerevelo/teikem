// Lote 193/199 — qué documentos de despacho ve el aparato: lo asignado a esta persona y lo que nadie tiene asignado (Lote 199: lo ve cualquiera); con warehouse.app.viewall también lo asignado a otras
// personas. Lógica pura; el permiso lo da el administrador por rol (Sistema → Roles). warehouse.pick.any ya no se consulta.
import { VIEW_ALL } from '../assign/assignScope'

export interface DispatchScope {
  /** warehouse.app.viewall: ve además lo asignado a otras personas. */
  any: boolean
  /** El usuario del aparato (para lo asignado a él). */
  userId: number | null
}

export function dispatchScope(permissions: readonly string[] | null | undefined, userId: number | null | undefined): DispatchScope {
  return { any: permissions?.includes(VIEW_ALL) ?? false, userId: userId ?? null }
}

/** ¿Este documento entra en el alcance? (mío, sin asignar, o cualquiera con ver todo). */
export function noticeInScope(scope: DispatchScope, assignedToUserId: number | null | undefined): boolean {
  return scope.any || assignedToUserId == null || (scope.userId != null && assignedToUserId === scope.userId)
}
