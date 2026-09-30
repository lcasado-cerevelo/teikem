// Rutas de la aplicación. Una ruta por pantalla, con carga diferida: `lazy(() => import('../features/<modulo>/<Pantalla>'))`
// (la pantalla exporta `default`). `perm` y `module` usan los códigos exactos del API; `nav` la pone en el menú lateral.
// Para agregar una pantalla: añade una entrada a `appRoutes` (internas, dentro del shell) o `publicRoutes` (sin sesión).
// El menú es el de la maqueta completo (7 grupos, 41 ítems, `nav.order` = posición en la maqueta × 10). Un ítem cuya
// pantalla aún no existe se declara con `pending({...})` (pantalla `Placeholder` con su título y subtítulo): al llegar
// la pantalla real se cambia `pending({ ... })` por `{ ..., element: lazy(...) }` sin tocar ruta, permiso, módulo ni orden.
import { lazy, type ComponentType } from 'react'
import { Navigate, useLocation, useParams, type Params } from 'react-router-dom'
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

/** Ruta que ya no es pantalla propia: lleva (sin dejar historial) a donde vive ahora; la guarda es la del destino. */
export function redirectTo(to: string): ComponentType {
  function Redirect() {
    return <Navigate to={to} replace />
  }
  return Redirect
}

/**
 * Como `redirectTo`, pero conserva la consulta (`?…`) de la dirección anterior; `mapSearch` la ajusta (p. ej. renombrar la
 * pestaña). Para direcciones viejas que aún traen filtros en la URL (enlaces guardados o de otra pantalla).
 */
export function redirectKeepingQuery(pathname: string, mapSearch?: (params: URLSearchParams) => URLSearchParams): ComponentType {
  function Redirect() {
    const { search } = useLocation()
    const params = new URLSearchParams(search)
    const query = (mapSearch ? mapSearch(params) : params).toString()
    return <Navigate to={query ? `${pathname}?${query}` : pathname} replace />
  }
  return Redirect
}

/**
 * Como `redirectKeepingQuery`, pero la consulta nueva se arma también con los parámetros de la RUTA anterior
 * (`:publicId`…): `mapSearch(consulta, parámetros)` es lógica pura. Para fichas que dejaron de ser pantalla propia y
 * ahora se eligen en una lista con un parámetro (p. ej. `/warehouse/receipts/:publicId` → `/warehouse/receipts?receipt=`).
 * Conserva el `#hash`.
 */
export function redirectWithParams(
  pathname: string,
  mapSearch: (search: URLSearchParams, params: Readonly<Params<string>>) => URLSearchParams,
): ComponentType {
  function Redirect() {
    const params = useParams()
    const { search, hash } = useLocation()
    const query = mapSearch(new URLSearchParams(search), params).toString()
    return <Navigate to={`${pathname}${query ? `?${query}` : ''}${hash}`} replace />
  }
  return Redirect
}

/**
 * Lote 13: `/warehouse/receipts/:publicId` (ficha propia del recibo) → `/warehouse/receipts?receipt=<publicId>` (el recibo
 * elegido en el maestro-detalle de la lista). `receipt` va primero; los demás parámetros se conservan (un `receipt`
 * anterior lo reemplaza el de la ruta). Sin `publicId`, la consulta queda igual.
 */
export function legacyReceiptSearch(search: URLSearchParams, params: Readonly<Params<string>>): URLSearchParams {
  const publicId = params.publicId
  if (!publicId) return new URLSearchParams(search)
  const next = new URLSearchParams({ receipt: publicId })
  search.forEach((value, key) => {
    if (key !== 'receipt') next.append(key, value)
  })
  return next
}

/**
 * Lote 14 (P8): `/warehouse/cycle-counts/:id` (ficha propia del conteo, borrada) → `/warehouse/cycle-counts?count=<id>` (el
 * conteo elegido en el panel derecho de la lista). `count` va primero; los demás parámetros se conservan (un `count` anterior
 * lo reemplaza el de la ruta). Sin `id`, la consulta queda igual.
 */
export function legacyCountSearch(search: URLSearchParams, params: Readonly<Params<string>>): URLSearchParams {
  const id = params.id
  if (!id) return new URLSearchParams(search)
  const next = new URLSearchParams({ count: id })
  search.forEach((value, key) => {
    if (key !== 'count') next.append(key, value)
  })
  return next
}

/**
 * `/warehouse/inventory` (antes 'Inventario', con Saldos como primera pestaña) → `/warehouse/kardex` (Kárdex primero):
 * sin `tab` era Saldos (`tab=balances`); `tab=kardex` pasa a no llevar parámetro. Los filtros se conservan.
 */
export function legacyInventorySearch(params: URLSearchParams): URLSearchParams {
  const next = new URLSearchParams(params)
  const tab = next.get('tab')
  if (!tab) next.set('tab', 'balances')
  else if (tab === 'kardex') next.delete('tab')
  return next
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
  // warehouse.crossdock dentro de la pantalla). Orden de la maqueta: Almacenes, Ubicaciones, Productos e inventario,
  // Compras (y Proveedores), Recibo, Recolección y empaque, Transferencias y ajustes (Lote 14: en el lugar de la vieja
  // 'Ajustes de inventario', que salió del menú), Conteo cíclico, Cruce de muelle, Kárdex de movimientos. Las tareas de almacén viven en la pantalla de su tipo (features/warehouse/taskQueue.tsx).
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
    // Ubicaciones (maqueta ubicaciones()): ocupación por zona y posiciones de un almacén; mismo acceso que Almacenes.
    path: '/warehouse/locations',
    element: lazy(() => import('../features/warehouse/LocationsScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'locations', order: 20 },
  },
  {
    // Productos e inventario (maqueta inventario(), Fase 8): catálogo con disponible/reservado/total por producto y KPIs;
    // pestaña Categorías (?tab=categories). Alta y edición en ProductEditorModal.
    path: '/warehouse/products',
    element: lazy(() => import('../features/warehouse/ProductListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'products', order: 30 },
  },
  {
    path: '/warehouse/products/:publicId',
    element: lazy(() => import('../features/warehouse/ProductDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
  },
  // Proveedores no está en la maqueta: va antes de Compras (Lote 2).
  {
    path: '/warehouse/suppliers',
    element: lazy(() => import('../features/warehouse/SupplierListScreen')),
    perm: 'purchasing.view',
    module: ModuleKeys.Purchasing,
    nav: { group: 'warehouse', key: 'suppliers', order: 40 },
  },
  // Compras (maqueta: 'Compras').
  {
    path: '/warehouse/purchase-orders',
    element: lazy(() => import('../features/warehouse/PurchaseOrderListScreen')),
    perm: 'purchasing.view',
    module: ModuleKeys.Purchasing,
    nav: { group: 'warehouse', key: 'purchaseOrders', order: 50 },
  },
  {
    path: '/warehouse/purchase-orders/:publicId',
    element: lazy(() => import('../features/warehouse/PurchaseOrderDetailScreen')),
    perm: 'purchasing.view',
    module: ModuleKeys.Purchasing,
  },
  // Recibo: incluye la pestaña 'Acomodo pendiente' (tareas PUTAWAY; antes 'Tareas de almacén', que no está en la maqueta).
  {
    path: '/warehouse/receipts',
    element: lazy(() => import('../features/warehouse/ReceiptListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'receipts', order: 60 },
  },
  // Lote 13: la ficha propia del recibo ya no existe; su dirección lleva al recibo elegido en la lista (?receipt=).
  { path: '/warehouse/receipts/:publicId', element: redirectWithParams('/warehouse/receipts', legacyReceiptSearch) },
  // Recolección y empaque: incluye la pestaña 'Reabasto' (tareas REPLENISH y 'Correr reabasto'). Lote 13: va justo
  // después de Recibo (antes de Ajustes de inventario).
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
  // Lote 14 (D1): 'Transferencias y ajustes' ocupa el lugar de la vieja 'Ajustes de inventario' (faltantes de compra), justo
  // después de Recolección y empaque. Pestañas Ajustes (sin parámetro) y Transferencias (?tab=transfers); ajustar y
  // transferir exigen inventory.adjust dentro de la pantalla.
  {
    path: '/warehouse/transfers-adjustments',
    element: lazy(() => import('../features/warehouse/TransfersAdjustmentsScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'transfersAdjustments', order: 80 },
  },
  // Lote 14 (D1-C): la pantalla de faltantes de compra salió del menú; los faltantes se resuelven desde la ficha de cada orden
  // de compra (pestaña Faltantes). Su dirección lleva a Compras.
  { path: '/warehouse/inventory-adjustments', element: redirectTo('/warehouse/purchase-orders') },
  // Conteo cíclico (Lote 14, D9/D10): dos paneles, la lista de conteos y el conteo elegido (?count=<id>); la asignación va
  // con un ícono en la lista (ya no hay pestaña 'Tareas de conteo').
  {
    path: '/warehouse/cycle-counts',
    element: lazy(() => import('../features/warehouse/CycleCountListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'cycleCounts', order: 90 },
  },
  // Lote 14: la ficha propia del conteo ya no existe; su dirección lleva al conteo elegido en la lista (?count=).
  { path: '/warehouse/cycle-counts/:id', element: redirectWithParams('/warehouse/cycle-counts', legacyCountSearch) },
  // Cruce de muelle: incluye las pestañas 'Citas de muelle' y 'Tareas de cruce' (tareas CROSSDOCK).
  {
    path: '/warehouse/cross-dock-plans',
    element: lazy(() => import('../features/warehouse/CrossDockPlanListScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.CrossDock,
    nav: { group: 'warehouse', key: 'crossDockPlans', order: 100 },
  },
  {
    path: '/warehouse/cross-dock-plans/:id',
    element: lazy(() => import('../features/warehouse/CrossDockPlanDetailScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.CrossDock,
  },
  // Kárdex de movimientos (maqueta ledger()): pestañas Kárdex, Saldos (?tab=balances) y Conciliación (?tab=reconciliation).
  {
    path: '/warehouse/kardex',
    element: lazy(() => import('../features/warehouse/InventoryScreen')),
    perm: 'inventory.view',
    module: ModuleKeys.WmsLotSerial,
    nav: { group: 'warehouse', key: 'kardex', order: 110 },
  },
  // Direcciones anteriores (sin ítem de menú): 'Tareas de almacén', 'Citas de muelle' e 'Inventario' ya no son pantallas propias.
  { path: '/warehouse/tasks', element: redirectTo('/warehouse/receipts?tab=putaway') },
  { path: '/warehouse/dock-appointments', element: redirectTo('/warehouse/cross-dock-plans?tab=appointments') },
  { path: '/warehouse/inventory', element: redirectKeepingQuery('/warehouse/kardex', legacyInventorySearch) },

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
