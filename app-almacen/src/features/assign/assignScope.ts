// Lote 199 — qué ve y qué puede hacer el aparato con las asignaciones. Todos ven lo suyo y lo que nadie tiene asignado; con warehouse.app.viewall se ve además lo asignado a otras personas;
// con warehouse.app.reassign (que exige ver todo) se puede reasignar. Lógica pura; el permiso lo da el administrador por rol (Sistema → Roles).
import { useEffect, useState } from 'react'

import { useMyPermissions } from '../../kernel/auth/permissions'
import { fetchAssignees, getCachedAssignees, type Assignee } from './assignApi'

export const VIEW_ALL = 'warehouse.app.viewall'
export const REASSIGN = 'warehouse.app.reassign'

export interface AssignAccess {
  /** Ver lo asignado a otras personas. */
  viewAll: boolean
  /** Reasignar (solo si también ve todo). */
  reassign: boolean
}

export function assignAccess(permissions: readonly string[] | null | undefined): AssignAccess {
  const viewAll = permissions?.includes(VIEW_ALL) ?? false
  return { viewAll, reassign: viewAll && (permissions?.includes(REASSIGN) ?? false) }
}

export function useAssignAccess(): AssignAccess {
  return assignAccess(useMyPermissions())
}

/** A quién pertenece un documento visto desde esta persona. */
export type Owner = 'mine' | 'free' | 'other'

export function ownerOf(assignedToUserId: number | null | undefined, userId: number | null | undefined): Owner {
  if (assignedToUserId == null) return 'free'
  return userId != null && assignedToUserId === userId ? 'mine' : 'other'
}

/** Las personas de la compañía (lo guardado al instante y, con señal, lo actualizado) para mostrar «Asignado a: nombre»; solo se pide cuando `enabled` (con ver todo). */
export function useAssignees(enabled: boolean): Assignee[] {
  const [list, setList] = useState<Assignee[]>(getCachedAssignees())
  useEffect(() => {
    if (!enabled) return undefined
    let alive = true
    void fetchAssignees().then((l) => {
      if (alive) setList(l)
    })
    return () => {
      alive = false
    }
  }, [enabled])
  return list
}
