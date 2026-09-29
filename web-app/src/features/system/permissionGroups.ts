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
