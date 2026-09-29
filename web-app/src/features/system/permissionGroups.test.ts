import { describe, expect, it } from 'vitest'
import { groupPermissionsByCategory, type PermissionDto } from './permissionGroups'

const P = (code: string, category: string): PermissionDto => ({ id: 0, code, category, label: code })

describe('groupPermissionsByCategory', () => {
  it('agrupa por categoría en el orden de la primera aparición, conservando el orden de los permisos', () => {
    const perms = [P('orders.view', 'ORDERS'), P('warehouse.receive', 'WAREHOUSE'), P('orders.create', 'ORDERS'), P('inventory.view', 'WAREHOUSE')]
    expect(groupPermissionsByCategory(perms)).toEqual([
      { category: 'ORDERS', items: [perms[0], perms[2]] },
      { category: 'WAREHOUSE', items: [perms[1], perms[3]] },
    ])
  })

  it('lista vacía → sin grupos', () => {
    expect(groupPermissionsByCategory([])).toEqual([])
  })
})
