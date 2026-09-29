// Menú completo de la maqueta (Lote F8a P0): 7 grupos, 41 ítems con permiso y módulo, ítems pendientes y `perm: 'a|b'`.
import { describe, expect, it } from 'vitest'
import { permAllowed } from '../kernel/access/accessContext'
import { ModuleKeys } from '../kernel/access/modules'
import { translate } from '../kernel/i18n/i18n'
import { NAV_GROUPS, navSubtitleKey, navTitleKey, routeAllowed, visibleNav } from './navigation'
import { appRoutes, type AppRoute } from './routes'

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
      'Productos',
      'Inventario',
      'Recibo',
      'Tareas de almacén',
      'Conteo cíclico',
      'Recolección y empaque',
      'Proveedores',
      'Órdenes de compra',
      'Citas de muelle',
      'Cruce de muelle',
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
          '/warehouse/products',
          '/warehouse/inventory',
          '/warehouse/receipts',
          '/warehouse/tasks',
          '/warehouse/cycle-counts',
          '/warehouse/pick-batches',
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
