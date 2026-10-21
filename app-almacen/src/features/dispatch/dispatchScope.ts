// Lote 193 — qué documentos de despacho ve el aparato: con el permiso warehouse.pick.any, cualquiera del almacén; sin él, solo los avisos de salida asignados a esta persona
// (las órdenes de venta no se asignan, así que sin el permiso no se ofrecen). Lógica pura; el permiso lo da el administrador por rol (Sistema → Roles).
export const PICK_ANY = 'warehouse.pick.any'

export interface DispatchScope {
  /** warehouse.pick.any: ve y despacha cualquier documento. */
  any: boolean
  /** El usuario del aparato (para lo asignado a él). */
  userId: number | null
}

export function dispatchScope(permissions: readonly string[] | null | undefined, userId: number | null | undefined): DispatchScope {
  return { any: permissions?.includes(PICK_ANY) ?? false, userId: userId ?? null }
}

/** ¿Este aviso de salida entra en el alcance? (asignado a la persona, o cualquiera con el permiso). */
export function noticeInScope(scope: DispatchScope, assignedToUserId: number | null | undefined): boolean {
  return scope.any || (scope.userId != null && assignedToUserId === scope.userId)
}
