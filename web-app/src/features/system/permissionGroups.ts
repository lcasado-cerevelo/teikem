// Lógica pura: agrupar el catálogo de permisos (`GET /api/v1/permissions`) por categoría, en el orden de aparición
// del catálogo del servidor (PermissionCatalog.All), no alfabético. La usan RolesTab (editor de rol) y UsersTab
// (permisos extra de un usuario).
import type { components } from '../../kernel/api/schema'

export type PermissionDto = components['schemas']['PermissionDto']

export interface PermissionGroup {
  category: string
  items: PermissionDto[]
}

/** Agrupa por `category`, conservando el orden de la primera aparición de cada categoría y de sus permisos. */
export function groupPermissionsByCategory(perms: readonly PermissionDto[]): PermissionGroup[] {
  const order: string[] = []
  const map = new Map<string, PermissionDto[]>()
  for (const p of perms) {
    const category = p.category ?? ''
    if (!map.has(category)) {
      map.set(category, [])
      order.push(category)
    }
    map.get(category)!.push(p)
  }
  return order.map((category) => ({ category, items: map.get(category) ?? [] }))
}

const VIEW_MARKERS = [': entrar y ver', ': open and view']

/** ¿Es el permiso de «entrar y ver» de una pantalla del menú? (encabeza a los que la editan, que salen debajo y con sangría). */
export function isScreenPermission(p: Pick<PermissionDto, 'label'>): boolean {
  const label = (p.label ?? '').toLowerCase()
  return VIEW_MARKERS.some((m) => label.endsWith(m))
}

export interface PermissionRow {
  permission: PermissionDto
  /** 'screen' = «entrar y ver» de una pantalla; 'sub' = lo que se hace en la pantalla de arriba; 'plain' = ninguno de los dos. */
  kind: 'screen' | 'sub' | 'plain'
}

/** Marca cada permiso de un grupo: las pantallas con su «entrar y ver» y, debajo y con sangría, lo que se hace en ellas (2026-10-22, Luis: «entrar y ver recibo» y luego «recibir»). */
export function annotatePermissions(items: readonly PermissionDto[]): PermissionRow[] {
  let seenScreen = false
  return items.map((permission) => {
    if (isScreenPermission(permission)) {
      seenScreen = true
      return { permission, kind: 'screen' as const }
    }
    return { permission, kind: seenScreen ? ('sub' as const) : ('plain' as const) }
  })
}
