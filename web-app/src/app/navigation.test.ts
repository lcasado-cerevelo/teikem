// Menú completo de la maqueta (Lote F8a P0): 7 grupos, 41 ítems con permiso y módulo, ítems pendientes y `perm: 'a|b'`.
import { describe, expect, it } from 'vitest'
import { permAllowed } from '../kernel/access/accessContext'
import { ModuleKeys } from '../kernel/access/modules'
import { translate } from '../kernel/i18n/i18n'
import { NAV_GROUPS, navSubtitleKey, navTitleKey, routeAllowed, visibleNav } from './navigation'
import { appRoutes, legacyCountSearch, legacyInventorySearch, legacyReceiptSearch, type AppRoute } from './routes'

const ALL_MODULES = new Set<string>(Object.values(ModuleKeys))
const ALL_PERMS = new Set(appRoutes.flatMap((r) => (r.perm ? r.perm.split('|') : [])))
const navRoutes = appRoutes.filter((r) => r.nav)
const byPath = (path: string) => appRoutes.find((r) => r.path === path) as AppRoute
const paths = (groups: ReturnType<typeof visibleNav<AppRoute>>) => groups.map((g) => [g.key, g.items.map((r) => r.path)])

describe('NAV_GROUPS', () => {
  it('son los 7 grupos de la maqueta, en su orden', () => {
    expect(NAV_GROUPS.map((g) => g.key)).toEqual(['ops', 'warehouse', 'money', 'catalog', 'analytics', 'system', 'portal'])
    expect(NAV_GROUPS.map((g) => translate('es', g.labelKey))).toEqual([
      'Operación',
      'Almacén',
      'Contabilidad',
      'Catálogo',
      'Análisis',
      'Sistema',
      'Portal de clientes',
    ])
    expect(NAV_GROUPS.map((g) => translate('en', g.labelKey))).toEqual([
      'Operations',
      'Warehouse',
      'Accounting',
      'Catalog',
      'Analytics',
      'System',
      'Client portal',
    ])
  })
})

describe('menú completo (routes.tsx)', () => {
  it('con todos los permisos y módulos se ven los 7 grupos y los 41 ítems, en el orden de la maqueta', () => {
    const groups = visibleNav(appRoutes, ALL_PERMS, ALL_MODULES)
    expect(groups.map((g) => [g.key, g.items.length])).toEqual([
      ['ops', 5],
      ['warehouse', 11],
      ['money', 5],
      ['catalog', 4],
      ['analytics', 4],
      ['system', 7],
      ['portal', 5],
    ])
    expect(groups.flatMap((g) => g.items.map((r) => translate('es', navTitleKey(r.nav!.key))))).toEqual([
      'Pulso del día',
      'Órdenes',
      'Estación de escaneo',
      'Sala de despacho',
      'Monitoreo de ruta',
      'Almacenes',
      'Posiciones',
      'Productos e inventario',
      'Proveedores',
      'Compras',
      'Recibo',
      'Recolección y empaque',
      'Transferencias y ajustes',
      'Conteo cíclico',
      'Cruce de muelle',
      'Kárdex de movimientos',
      'Contabilización de compras',
      'Contabilización de despachos',
      'Procesar entregas',
      'Facturación',
      'Liquidación a choferes',
      'Clientes y contratos',
      'Consignatarios',
      'Choferes y tarifas',
      'Flota y mantenimiento',
      'Vistas e informes',
      'Campos personalizados',
      'Indicadores',
      'Gráficos',
      'Impresoras y labels',
      'Roles y usuarios',
      'Aparatos móviles',
      'Catálogos de valores',
      'Integraciones / API',
      'Seguridad y auditoría',
      'Ajustes de la compañía',
      'Página principal',
      'Entrada de órdenes',
      'Consignatarios',
      'Configuración',
      'Perfil',
    ])
  })

  it('cada ítem tiene título y subtítulo en español e inglés, y `order` = posición × 10', () => {
    expect(navRoutes).toHaveLength(41)
    for (const r of navRoutes) {
      for (const lang of ['es', 'en'] as const) {
        expect(translate(lang, navTitleKey(r.nav!.key)), `${lang} ${r.path}`).not.toBe(navTitleKey(r.nav!.key))
        expect(translate(lang, navSubtitleKey(r.nav!.key)), `${lang} ${r.path}`).not.toBe(navSubtitleKey(r.nav!.key))
      }
    }
    for (const g of NAV_GROUPS) {
      const orders = navRoutes.filter((r) => r.nav!.group === g.key).map((r) => r.nav!.order)
      expect(orders).toEqual(orders.map((_, i) => (i + 1) * 10))
    }
  })

  it('los ítems sin pantalla usan la pantalla pendiente; los que ya existen no', () => {
    expect(byPath('/ops/dispatch').pending).toBe(true)
    expect(byPath('/catalog/clients').pending).toBe(true)
    expect(byPath('/portal/profile').pending).toBe(true)
    expect(byPath('/orders').pending).toBeUndefined()
    expect(byPath('/warehouse/receipts').pending).toBeUndefined()
    expect(byPath('/system/users').pending).toBeUndefined()
    expect(byPath('/').pending).toBeUndefined()
  })

  it("Lote 14 (D1): 'Transferencias y ajustes' va justo después de Recolección y empaque (Recibo 60 → Recolección 70 → Transferencias y ajustes 80 → Conteo 90); la vieja 'Ajustes de inventario' salió del menú y redirige a Compras", () => {
    const ta = byPath('/warehouse/transfers-adjustments')
    expect(ta).toMatchObject({ perm: 'inventory.view', module: 'WMS_LOTSERIAL', nav: { group: 'warehouse', key: 'transfersAdjustments', order: 80 } })
    expect(ta.pending).toBeUndefined()
    expect(byPath('/warehouse/receipts').nav).toMatchObject({ order: 60 })
    expect(byPath('/warehouse/pick-batches').nav).toMatchObject({ key: 'pickBatches', order: 70 })
    expect(byPath('/warehouse/cycle-counts').nav).toMatchObject({ order: 90 })
    const warehouse = visibleNav(appRoutes, ALL_PERMS, ALL_MODULES).find((g) => g.key === 'warehouse')!.items.map((r) => r.path)
    expect(warehouse.indexOf('/warehouse/pick-batches')).toBe(warehouse.indexOf('/warehouse/receipts') + 1)
    expect(warehouse.indexOf('/warehouse/transfers-adjustments')).toBe(warehouse.indexOf('/warehouse/pick-batches') + 1)
    expect(routeAllowed(ta, new Set(['inventory.view']), new Set(['WMS_LOTSERIAL']))).toBe(true)
    expect(routeAllowed(ta, new Set(['purchasing.view']), new Set(['PURCHASING']))).toBe(false)
    expect(translate('en', navTitleKey('transfersAdjustments'))).toBe('Transfers & adjustments')
    // la dirección vieja existe (enlaces guardados), sin ítem, guarda ni módulo propios: redirige a Compras
    const old = byPath('/warehouse/inventory-adjustments')
    expect(old.nav).toBeUndefined()
    expect(old.perm).toBeUndefined()
    expect(old.module).toBeUndefined()
  })

  it("Fase 8: 'Productos e inventario' es un solo ítem (3.º) y 'Kárdex de movimientos' el último de Almacén; 'Inventario' ya no es ítem", () => {
    const warehouse = visibleNav(appRoutes, ALL_PERMS, ALL_MODULES).find((g) => g.key === 'warehouse')!.items
    expect(warehouse.map((r) => r.path)).toEqual([
      '/warehouse/warehouses',
      '/warehouse/locations',
      '/warehouse/products',
      '/warehouse/suppliers',
      '/warehouse/purchase-orders',
      '/warehouse/receipts',
      '/warehouse/pick-batches',
      '/warehouse/transfers-adjustments',
      '/warehouse/cycle-counts',
      '/warehouse/cross-dock-plans',
      '/warehouse/kardex',
    ])
    expect(translate('en', navTitleKey('products'))).toBe('Products & inventory')
    expect(translate('en', navTitleKey('kardex'))).toBe('Movement ledger')
    expect(byPath('/warehouse/kardex')).toMatchObject({ perm: 'inventory.view', module: 'WMS_LOTSERIAL' })
    expect(byPath('/warehouse/kardex').pending).toBeUndefined()
    // la dirección anterior sigue existiendo (enlaces guardados), sin ítem, guarda ni módulo propios: redirige
    expect(byPath('/warehouse/inventory')).toMatchObject({ path: '/warehouse/inventory' })
    expect(byPath('/warehouse/inventory').nav).toBeUndefined()
    expect(byPath('/warehouse/inventory').perm).toBeUndefined()
  })

  it('legacyInventorySearch: sin pestaña era Saldos (tab=balances); tab=kardex pasa a no llevar parámetro; los filtros se quedan', () => {
    const map = (q: string) => legacyInventorySearch(new URLSearchParams(q)).toString()
    expect(map('')).toBe('tab=balances')
    expect(map('categoryIds=7&warehousePublicIds=W')).toBe('categoryIds=7&warehousePublicIds=W&tab=balances')
    expect(map('tab=kardex&product=P')).toBe('product=P')
    expect(map('tab=reconciliation')).toBe('tab=reconciliation')
  })

  it('legacyReceiptSearch: /warehouse/receipts/:publicId → ?receipt=<publicId> primero; los demás parámetros se quedan', () => {
    const pid = '11111111-1111-1111-1111-111111111111'
    const map = (q: string, publicId?: string) => legacyReceiptSearch(new URLSearchParams(q), { publicId }).toString()
    expect(map('', pid)).toBe(`receipt=${pid}`)
    expect(map('tab=asns&warehouse=W', pid)).toBe(`receipt=${pid}&tab=asns&warehouse=W`)
    // un ?receipt= viejo lo reemplaza el de la ruta; sin publicId la consulta queda igual
    expect(map('receipt=OTRO&x=1', pid)).toBe(`receipt=${pid}&x=1`)
    expect(map('x=1')).toBe('x=1')
  })

  it('Lote 14 (P8): legacyCountSearch: /warehouse/cycle-counts/:id → ?count=<id> primero; los demás parámetros se quedan', () => {
    const map = (q: string, id?: string) => legacyCountSearch(new URLSearchParams(q), { id }).toString()
    expect(map('', '27')).toBe('count=27')
    expect(map('count=3&x=1', '27')).toBe('count=27&x=1')
    expect(map('x=1')).toBe('x=1')
    // la ficha vieja del conteo es una redirección sin ítem, guarda ni módulo propios (la guarda es la de la lista)
    const old = byPath('/warehouse/cycle-counts/:id')
    expect(old.nav).toBeUndefined()
    expect(old.perm).toBeUndefined()
    expect(old.module).toBeUndefined()
    expect(byPath('/warehouse/cycle-counts')).toMatchObject({ perm: 'inventory.view', module: 'WMS_LOTSERIAL', nav: { key: 'cycleCounts', order: 90 } })
  })

  it('la ficha vieja del recibo es una redirección sin ítem, guarda ni módulo propios (la guarda es la de la lista)', () => {
    const old = byPath('/warehouse/receipts/:publicId')
    expect(old.nav).toBeUndefined()
    expect(old.perm).toBeUndefined()
    expect(old.module).toBeUndefined()
    expect(old.pending).toBeUndefined()
  })

  it('un ítem pendiente sin su permiso o sin su módulo no aparece; con los dos, sí', () => {
    const dispatch = byPath('/ops/dispatch')
    expect(dispatch).toMatchObject({ perm: 'trips.dispatch', module: 'LTL_GROUND' })
    const has = (perms: string[], modules: string[]) =>
      visibleNav(appRoutes, new Set(perms), new Set(modules)).some((g) => g.items.includes(dispatch))
    expect(has([], ['LTL_GROUND'])).toBe(false)
    expect(has(['trips.dispatch'], [])).toBe(false)
    expect(has(['trips.dispatch'], ['LTL_GROUND'])).toBe(true)
  })

  it('un grupo cuyos ítems no se pueden ver no se pinta (usuario solo de almacén)', () => {
    const groups = visibleNav(appRoutes, new Set(['inventory.view', 'warehouse.receive']), new Set(['WMS_LOTSERIAL', 'SYSTEM', 'ANALYTICS']))
    expect(paths(groups)).toEqual([
      ['ops', ['/']],
      [
        'warehouse',
        [
          '/warehouse/warehouses',
          '/warehouse/locations',
          '/warehouse/products',
          '/warehouse/receipts',
          '/warehouse/pick-batches',
          '/warehouse/transfers-adjustments',
          '/warehouse/cycle-counts',
          '/warehouse/kardex',
        ],
      ],
    ])
  })
})

describe("perm 'a|b' (cualquiera de los dos)", () => {
  it('permAllowed: sin perm no exige nada; con alternativas alcanza una', () => {
    const perms = new Set(['admin.roles'])
    expect(permAllowed(undefined, perms)).toBe(true)
    expect(permAllowed('', perms)).toBe(true)
    expect(permAllowed('admin.users|admin.roles', perms)).toBe(true)
    expect(permAllowed('admin.users', perms)).toBe(false)
    expect(permAllowed('admin.users|admin.audit', perms)).toBe(false)
  })

  it('Roles y usuarios aparece con admin.users o con admin.roles (y el módulo SYSTEM)', () => {
    const users = byPath('/system/users')
    expect(users.perm).toBe('admin.users|admin.roles')
    const sys = new Set(['SYSTEM'])
    expect(routeAllowed(users, new Set(['admin.users']), sys)).toBe(true)
    expect(routeAllowed(users, new Set(['admin.roles']), sys)).toBe(true)
    expect(routeAllowed(users, new Set(['admin.audit']), sys)).toBe(false)
    expect(routeAllowed(users, new Set(['admin.users']), new Set())).toBe(false)
    expect(paths(visibleNav(appRoutes, new Set(['admin.roles']), sys))).toEqual([
      ['ops', ['/']],
      ['system', ['/system/users']],
    ])
  })
})
