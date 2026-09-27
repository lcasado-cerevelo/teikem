// Rutas de la aplicación. Una ruta por pantalla, con carga diferida: `lazy(() => import('../features/<modulo>/<Pantalla>'))`
// (la pantalla exporta `default`). `perm` y `module` usan los códigos exactos del API; `nav` la pone en el menú lateral.
// Para agregar una pantalla: añade una entrada a `appRoutes` (internas, dentro del shell) o `publicRoutes` (sin sesión).
import { lazy, type ComponentType } from 'react'
import { ForbiddenScreen, ModuleOffScreen } from '../kernel/access/AccessScreens'
import { ModuleKeys } from '../kernel/access/modules'
import type { NavEntry } from './navigation'

export interface AppRoute {
  path: string
  element: ComponentType
  /** Permiso exigido (sin él: pantalla 'Sin permiso'). */
  perm?: string
  /** Módulo exigido (apagado: pantalla 'Módulo apagado'). */
  module?: string
  /** Entrada del menú lateral; sin `nav` la ruta existe pero no aparece en el menú. */
  nav?: NavEntry
}

/** Pantallas sin sesión (fuera del shell). */
export const publicRoutes: readonly AppRoute[] = [
  { path: '/login', element: lazy(() => import('../features/auth/LoginPage')) },
  { path: '/mfa', element: lazy(() => import('../features/auth/MfaPage')) },
  { path: '/select-tenant', element: lazy(() => import('../features/auth/SelectTenantPage')) },
]

/** Pantallas internas (dentro del shell, con sesión). */
export const appRoutes: readonly AppRoute[] = [
  { path: '/', element: lazy(() => import('../features/analytics/Pulse')), nav: { group: 'ops', labelKey: 'nav.pulse', order: 0 } },
  { path: '/account', element: lazy(() => import('../features/account/AccountPage')) },

  // Lote F6 — consulta de órdenes (solo lectura), grupo Operación.
  {
    path: '/orders',
    element: lazy(() => import('../features/orders/OrderListScreen')),
    perm: 'orders.view',
    module: ModuleKeys.LtlGround,
    nav: { group: 'ops', labelKey: 'nav.orders', order: 10 },
  },
  {
    path: '/orders/:publicId',
    element: lazy(() => import('../features/orders/OrderDetailScreen')),
    perm: 'orders.view',
    module: ModuleKeys.LtlGround,
  },

  // Lote F6 — Almacén e inventario (manual 06). Lecturas con inventory.view + WMS_LOTSERIAL; compras con
  // purchasing.view + PURCHASING; citas y planes de cruce de muelle con inventory.view + CROSSDOCK (las acciones exigen
  // warehouse.crossdock dentro de la pantalla).
  {
    path: '/warehouse/warehouses',
    element: lazy(() => import('../features/warehouse/WarehouseListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', labelKey: 'nav.warehouses', order: 10 },
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
    nav: { group: 'warehouse', labelKey: 'nav.products', order: 20 },
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
    nav: { group: 'warehouse', labelKey: 'nav.inventory', order: 30 },
  },
  {
    path: '/warehouse/receipts',
    element: lazy(() => import('../features/warehouse/ReceiptListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', labelKey: 'nav.receipts', order: 40 },
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
    nav: { group: 'warehouse', labelKey: 'nav.warehouseTasks', order: 50 },
  },
  {
    path: '/warehouse/cycle-counts',
    element: lazy(() => import('../features/warehouse/CycleCountListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', labelKey: 'nav.cycleCounts', order: 60 },
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
    nav: { group: 'warehouse', labelKey: 'nav.pickBatches', order: 70 },
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
    nav: { group: 'warehouse', labelKey: 'nav.suppliers', order: 80 },
  },
  {
    path: '/warehouse/purchase-orders',
    element: lazy(() => import('../features/warehouse/PurchaseOrderListScreen')),
    perm: 'purchasing.view',
    module: ModuleKeys.Purchasing,
    nav: { group: 'warehouse', labelKey: 'nav.purchaseOrders', order: 90 },
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
    nav: { group: 'warehouse', labelKey: 'nav.dockAppointments', order: 100 },
  },
  {
    path: '/warehouse/cross-dock-plans',
    element: lazy(() => import('../features/warehouse/CrossDockPlanListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.CrossDock,
    nav: { group: 'warehouse', labelKey: 'nav.crossDockPlans', order: 110 },
  },
  {
    path: '/warehouse/cross-dock-plans/:id',
    element: lazy(() => import('../features/warehouse/CrossDockPlanDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.CrossDock,
  },

  { path: '/forbidden', element: ForbiddenScreen },
  { path: '/module-off', element: ModuleOffScreen },
]
