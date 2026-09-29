// Rutas de la aplicación. Una ruta por pantalla, con carga diferida: `lazy(() => import('../features/<modulo>/<Pantalla>'))`
// (la pantalla exporta `default`). `perm` y `module` usan los códigos exactos del API; `nav` la pone en el menú lateral.
// Para agregar una pantalla: añade una entrada a `appRoutes` (internas, dentro del shell) o `publicRoutes` (sin sesión).
// El menú es el de la maqueta completo (7 grupos, 41 ítems, `nav.order` = posición en la maqueta × 10). Un ítem cuya
// pantalla aún no existe se declara con `pending({...})` (pantalla `Placeholder` con su título y subtítulo): al llegar
// la pantalla real se cambia `pending({ ... })` por `{ ..., element: lazy(...) }` sin tocar ruta, permiso, módulo ni orden.
import { lazy, type ComponentType } from 'react'
import { ForbiddenScreen, ModuleOffScreen } from '../kernel/access/AccessScreens'
import { ModuleKeys } from '../kernel/access/modules'
import { NAV_GROUPS, type NavEntry } from './navigation'
import Placeholder from './Placeholder'

export interface AppRoute {
  path: string
  element: ComponentType
  /** Permiso exigido (sin él: pantalla 'Sin permiso'). `a|b` = cualquiera de los dos, como el API. */
  perm?: string
  /** Módulo exigido (apagado: pantalla 'Módulo apagado'). */
  module?: string
  /** Entrada del menú lateral; sin `nav` la ruta existe pero no aparece en el menú. */
  nav?: NavEntry
  /** true = la pantalla todavía es `Placeholder` (llega en un lote posterior). */
  pending?: boolean
}

/** Ruta de un ítem del menú cuya pantalla llega en un lote posterior: `Placeholder` con el título y subtítulo del ítem. */
export function pending(route: Omit<AppRoute, 'element' | 'pending'> & { nav: NavEntry }): AppRoute {
  const { key, group } = route.nav
  const GroupIcon = NAV_GROUPS.find((g) => g.key === group)?.icon
  function PendingScreen() {
    return <Placeholder navKey={key} icon={GroupIcon ? <GroupIcon /> : undefined} />
  }
  return { ...route, element: PendingScreen, pending: true }
}

/** Pantallas sin sesión (fuera del shell). */
export const publicRoutes: readonly AppRoute[] = [
  { path: '/login', element: lazy(() => import('../features/auth/LoginPage')) },
  { path: '/mfa', element: lazy(() => import('../features/auth/MfaPage')) },
  { path: '/select-tenant', element: lazy(() => import('../features/auth/SelectTenantPage')) },
]

/** Pantallas internas (dentro del shell, con sesión). */
export const appRoutes: readonly AppRoute[] = [
  // ===== Operación =====
  { path: '/', element: lazy(() => import('../features/analytics/Pulse')), nav: { group: 'ops', key: 'pulse', order: 10 } },
  { path: '/account', element: lazy(() => import('../features/account/AccountPage')) },

  // Lote F6 — consulta de órdenes (solo lectura).
  {
    path: '/orders',
    element: lazy(() => import('../features/orders/OrderListScreen')),
    perm: 'orders.view',
    module: ModuleKeys.LtlGround,
    nav: { group: 'ops', key: 'orders', order: 20 },
  },
  {
    path: '/orders/:publicId',
    element: lazy(() => import('../features/orders/OrderDetailScreen')),
    perm: 'orders.view',
    module: ModuleKeys.LtlGround,
  },
  // F5
  pending({ path: '/ops/scan', perm: 'trips.scan', module: ModuleKeys.LtlGround, nav: { group: 'ops', key: 'scan', order: 30 } }),
  pending({ path: '/ops/dispatch', perm: 'trips.dispatch', module: ModuleKeys.LtlGround, nav: { group: 'ops', key: 'dispatch', order: 40 } }),
  pending({ path: '/ops/monitor', perm: 'trips.view', module: ModuleKeys.LtlGround, nav: { group: 'ops', key: 'monitor', order: 50 } }),

  // ===== Almacén =====
  // Lote F6 — Almacén e inventario (manual 06). Lecturas con inventory.view + WMS_LOTSERIAL; compras con
  // purchasing.view + PURCHASING; citas y planes de cruce de muelle con inventory.view + CROSSDOCK (las acciones exigen
  // warehouse.crossdock dentro de la pantalla).
  {
    path: '/warehouse/warehouses',
    element: lazy(() => import('../features/warehouse/WarehouseListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'warehouses', order: 10 },
  },
  {
    path: '/warehouse/warehouses/:publicId',
    element: lazy(() => import('../features/warehouse/WarehouseDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
  },
  {
    path: '/warehouse/products',
    element: lazy(() => import('../features/warehouse/ProductListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'products', order: 20 },
  },
  {
    path: '/warehouse/products/:publicId',
    element: lazy(() => import('../features/warehouse/ProductDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
  },
  {
    path: '/warehouse/inventory',
    element: lazy(() => import('../features/warehouse/InventoryScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'inventory', order: 30 },
  },
  {
    path: '/warehouse/receipts',
    element: lazy(() => import('../features/warehouse/ReceiptListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'receipts', order: 40 },
  },
  {
    path: '/warehouse/receipts/:publicId',
    element: lazy(() => import('../features/warehouse/ReceiptDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
  },
  {
    path: '/warehouse/tasks',
    element: lazy(() => import('../features/warehouse/WarehouseTaskListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'warehouseTasks', order: 50 },
  },
  {
    path: '/warehouse/cycle-counts',
    element: lazy(() => import('../features/warehouse/CycleCountListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'cycleCounts', order: 60 },
  },
  {
    // la ficha del conteo (GET /cycle-counts/{id}) exige warehouse.count; la lista solo inventory.view
    path: '/warehouse/cycle-counts/:id',
    element: lazy(() => import('../features/warehouse/CycleCountDetailScreen')),
    perm: 'warehouse.count',
    module: ModuleKeys.WmsLotSerial,
  },
  {
    path: '/warehouse/pick-batches',
    element: lazy(() => import('../features/warehouse/PickBatchListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'pickBatches', order: 70 },
  },
  {
    path: '/warehouse/pick-batches/:publicId',
    element: lazy(() => import('../features/warehouse/PickBatchDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
  },
  {
    path: '/warehouse/suppliers',
    element: lazy(() => import('../features/warehouse/SupplierListScreen')),
    perm: 'purchasing.view',
    module: ModuleKeys.Purchasing,
    nav: { group: 'warehouse', key: 'suppliers', order: 80 },
  },
  {
    path: '/warehouse/purchase-orders',
    element: lazy(() => import('../features/warehouse/PurchaseOrderListScreen')),
    perm: 'purchasing.view',
    module: ModuleKeys.Purchasing,
    nav: { group: 'warehouse', key: 'purchaseOrders', order: 90 },
  },
  {
    path: '/warehouse/purchase-orders/:publicId',
    element: lazy(() => import('../features/warehouse/PurchaseOrderDetailScreen')),
    perm: 'purchasing.view',
    module: ModuleKeys.Purchasing,
  },
  {
    path: '/warehouse/dock-appointments',
    element: lazy(() => import('../features/warehouse/DockAppointmentListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.CrossDock,
    nav: { group: 'warehouse', key: 'dockAppointments', order: 100 },
  },
  {
    path: '/warehouse/cross-dock-plans',
    element: lazy(() => import('../features/warehouse/CrossDockPlanListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.CrossDock,
    nav: { group: 'warehouse', key: 'crossDockPlans', order: 110 },
  },
  {
    path: '/warehouse/cross-dock-plans/:id',
    element: lazy(() => import('../features/warehouse/CrossDockPlanDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.CrossDock,
  },

  // ===== Contabilidad (7C) =====
  pending({ path: '/money/purchases', perm: 'purchasing.view', module: ModuleKeys.Purchasing, nav: { group: 'money', key: 'purchaseAccounting', order: 10 } }),
  pending({ path: '/money/dispatch', perm: 'billing.export', module: ModuleKeys.LtlGround, nav: { group: 'money', key: 'dispatchAccounting', order: 20 } }),
  pending({ path: '/money/cod', perm: 'cod.view', module: ModuleKeys.Cod, nav: { group: 'money', key: 'cod', order: 30 } }),
  pending({ path: '/money/billing', perm: 'billing.generate', module: ModuleKeys.LtlGround, nav: { group: 'money', key: 'billing', order: 40 } }),
  pending({ path: '/money/driver-pay', perm: 'driverpay.view', module: ModuleKeys.LtlGround, nav: { group: 'money', key: 'driverPay', order: 50 } }),

  // ===== Catálogo (F2, F4) =====
  pending({ path: '/catalog/clients', perm: 'clients.read', module: ModuleKeys.Catalog, nav: { group: 'catalog', key: 'clients', order: 10 } }),
  pending({ path: '/catalog/consignees', perm: 'locations.read', module: ModuleKeys.Catalog, nav: { group: 'catalog', key: 'consignees', order: 20 } }),
  pending({ path: '/catalog/drivers', perm: 'fleet.view', module: ModuleKeys.Catalog, nav: { group: 'catalog', key: 'drivers', order: 30 } }),
  pending({ path: '/catalog/fleet', perm: 'fleet.view', module: ModuleKeys.Catalog, nav: { group: 'catalog', key: 'fleet', order: 40 } }),

  // ===== Análisis (F8b; Indicadores y Gráficos: F8a P3) =====
  pending({ path: '/analytics/reports', perm: 'analytics.view', module: ModuleKeys.Analytics, nav: { group: 'analytics', key: 'reports', order: 10 } }),
  pending({ path: '/analytics/custom-fields', perm: 'admin.customfields', module: ModuleKeys.CustomFields, nav: { group: 'analytics', key: 'customFields', order: 20 } }),
  { path: '/analytics/indicators', element: lazy(() => import('../features/analytics/IndicatorsPage')), perm: 'analytics.view', module: ModuleKeys.Analytics, nav: { group: 'analytics', key: 'indicators', order: 30 } },
  { path: '/analytics/charts', element: lazy(() => import('../features/analytics/ChartsPage')), perm: 'analytics.view', module: ModuleKeys.Analytics, nav: { group: 'analytics', key: 'charts', order: 40 } },

  // ===== Sistema (Roles y usuarios: F8a P4; Aparatos móviles: P5; Catálogos de valores: P6; el resto F8b o sin backend) =====
  pending({ path: '/system/printers', perm: 'admin.tenant', module: ModuleKeys.System, nav: { group: 'system', key: 'printers', order: 10 } }),
  { path: '/system/users', element: lazy(() => import('../features/system/UsersPage')), perm: 'admin.users|admin.roles', module: ModuleKeys.System, nav: { group: 'system', key: 'users', order: 20 } },
  { path: '/system/devices', element: lazy(() => import('../features/system/DevicesPage')), perm: 'devices.manage', module: ModuleKeys.WmsLotSerial, nav: { group: 'system', key: 'devices', order: 30 } },
  { path: '/system/catalogs', element: lazy(() => import('../features/system/CatalogsPage')), perm: 'admin.catalogs', module: ModuleKeys.System, nav: { group: 'system', key: 'catalogs', order: 40 } },
  pending({ path: '/system/integrations', perm: 'admin.tenant', module: ModuleKeys.System, nav: { group: 'system', key: 'integrations', order: 50 } }),
  pending({ path: '/system/audit', perm: 'admin.audit', module: ModuleKeys.System, nav: { group: 'system', key: 'audit', order: 60 } }),
  pending({ path: '/system/settings', perm: 'admin.tenant', module: ModuleKeys.System, nav: { group: 'system', key: 'settings', order: 70 } }),

  // ===== Portal de clientes (sin backend todavía) =====
  pending({ path: '/portal/home', perm: 'portalusers.manage', module: ModuleKeys.ClientPortal, nav: { group: 'portal', key: 'portalHome', order: 10 } }),
  pending({ path: '/portal/orders', perm: 'portalusers.manage', module: ModuleKeys.ClientPortal, nav: { group: 'portal', key: 'portalOrders', order: 20 } }),
  pending({ path: '/portal/consignees', perm: 'portalusers.manage', module: ModuleKeys.ClientPortal, nav: { group: 'portal', key: 'portalConsignees', order: 30 } }),
  pending({ path: '/portal/settings', perm: 'portalusers.manage', module: ModuleKeys.ClientPortal, nav: { group: 'portal', key: 'portalSettings', order: 40 } }),
  pending({ path: '/portal/profile', perm: 'portalusers.manage', module: ModuleKeys.ClientPortal, nav: { group: 'portal', key: 'portalProfile', order: 50 } }),

  { path: '/forbidden', element: ForbiddenScreen },
  { path: '/module-off', element: ModuleOffScreen },
]
