// Lote F7A (P1) — datos del panel 'Actividad reciente' de Pulso: GET /api/v1/analytics/activity.
// El servidor decide qué módulos (pestañas) ve el usuario (`visibleModules`) y calcula los eventos al leer; aquí solo se
// pide la página, se acumulan las páginas de 'Ver más' y se traducen tipo de entidad → ficha y código → tono del chip.
import { keepPreviousData, useInfiniteQuery } from '@tanstack/react-query'
import { ModuleKeys, type ModuleKey } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { parseApiDate } from '../../kernel/api/dates'
import { formatDate, formatDayMonth, formatTime } from '../../kernel/format'
import type { components, paths } from '../../kernel/api/schema'
import type { ChipTone } from '../../kernel/ui'

export type ActivityPageDto = components['schemas']['ActivityPageDto']
export type ActivityEventDto = components['schemas']['ActivityEventDto']
/** Parámetros del endpoint (module, window, onlyMandatory, skip, take). */
export type ActivityQuery = NonNullable<paths['/api/v1/analytics/activity']['get']['parameters']['query']>
/** Filtros del panel: todo menos `skip`, que lo pone la paginación de 'Ver más'. */
export type ActivityFilters = Omit<ActivityQuery, 'skip'>

export const ACTIVITY_PATH = '/api/v1/analytics/activity'
/** Máximo por página que acepta el API (más → 400 'El máximo por página es 50.'). */
export const ACTIVITY_PAGE_SIZE = 50

/** Módulos de negocio (catálogo BusinessModule) en el orden de las pestañas: Almacén, Operación, Contabilidad. */
export const ACTIVITY_MODULES = ['WAREHOUSE', 'OPERATIONS', 'ACCOUNTING'] as const
export type ActivityModule = (typeof ACTIVITY_MODULES)[number]

/** Ventanas que acepta el API (24h por defecto). */
export const ACTIVITY_WINDOWS = ['24h', '48h', 'today'] as const
export type ActivityWindow = (typeof ACTIVITY_WINDOWS)[number]

/** Pestañas a pintar: solo los módulos de `visibleModules` que el panel conoce, en el orden fijo de arriba. */
export function activityTabs(visibleModules: readonly string[] | null | undefined): ActivityModule[] {
  const visible = new Set((visibleModules ?? []).map((m) => m.toUpperCase()))
  return ACTIVITY_MODULES.filter((m) => visible.has(m))
}

/**
 * `GET /api/v1/analytics/activity` con 'Ver más': consulta infinita con clave `[ruta, filtros]` cuyo parámetro de página
 * es `skip` (0, 50, 100…; `take` ≤ 50). `pages` trae las páginas en orden; `fetchNextPage()` pide la siguiente mientras
 * lo cargado sea menor que `total`. Cambiar un filtro cambia la clave y vuelve a la primera página (`keepPreviousData`
 * conserva la cabecera mientras llega). Pulso es la pantalla de inicio: un 403 no redirige (`handleAccessDenied: false`).
 */
export function useActivity(query: ActivityFilters, enabled = true) {
  const take = Math.min(query.take ?? ACTIVITY_PAGE_SIZE, ACTIVITY_PAGE_SIZE)
  const filters: ActivityFilters = { ...query, take }
  return useInfiniteQuery({
    queryKey: [ACTIVITY_PATH, filters],
    queryFn: ({ pageParam }) => {
      const params: ActivityQuery = { ...filters, skip: pageParam }
      return unwrap(api.GET(ACTIVITY_PATH, { params: { query: params } }))
    },
    initialPageParam: 0,
    getNextPageParam: (last: ActivityPageDto, all: ActivityPageDto[]) => nextActivitySkip(last, all),
    enabled,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
}

/** Siguiente `skip` o `undefined` si ya se cargó todo (lo cargado ≥ total, o la última página vino vacía). */
export function nextActivitySkip(last: ActivityPageDto, all: readonly ActivityPageDto[]): number | undefined {
  if ((last.items?.length ?? 0) === 0) return undefined
  const loaded = all.reduce((n, p) => n + (p.items?.length ?? 0), 0)
  return loaded < (last.total ?? 0) ? loaded : undefined
}

/** Enlace de la referencia de un evento: ruta de la ficha y la guarda de esa ruta (permiso y módulo, como en routes.tsx). */
export interface ActivityLink {
  to: string
  perm: string
  module: ModuleKey
}

/**
 * Ficha a la que lleva la referencia según `entityType` (código EntityType). `null` si el tipo no tiene pantalla (p. ej. ASN)
 * o si falta el identificador que usa la ruta (publicId o id).
 * INVENTORY_TRANSACTION no lleva enlace: el API reporta ajustes y transferencias como PRODUCT, y la búsqueda del Kárdex no
 * compara el documento de origen (una referencia no localizaría el movimiento).
 */
export function activityLink(e: ActivityEventDto): ActivityLink | null {
  const wms = ModuleKeys.WmsLotSerial
  const byPublicId = (base: string, perm = 'inventory.view', module: ModuleKey = wms): ActivityLink | null =>
    e.publicId ? { to: `${base}/${encodeURIComponent(e.publicId)}`, perm, module } : null
  const byId = (base: string, perm: string, module: ModuleKey): ActivityLink | null =>
    e.entityId != null && e.entityId > 0 ? { to: `${base}/${e.entityId}`, perm, module } : null

  switch ((e.entityType ?? '').toUpperCase()) {
    case 'RECEIPT':
      // Lote 13: el recibo se abre en la lista (maestro-detalle), elegido con `?receipt=`; la ficha propia ya no existe
      return e.publicId
        ? { to: `/warehouse/receipts?receipt=${encodeURIComponent(e.publicId)}`, perm: 'inventory.view', module: wms }
        : null
    case 'CYCLE_COUNT':
      // Lote 14: el conteo se abre en la lista de dos paneles, elegido con `?count=` (la ficha propia ya no existe); la lista
      // se lee con inventory.view (sin warehouse.count, el conteo se ve a ciegas y sin captura)
      return e.entityId != null && e.entityId > 0 ? { to: `/warehouse/cycle-counts?count=${e.entityId}`, perm: 'inventory.view', module: wms } : null
    case 'PICK_BATCH':
      return byPublicId('/warehouse/pick-batches')
    case 'PURCHASE_ORDER':
      return byPublicId('/warehouse/purchase-orders', 'purchasing.view', ModuleKeys.Purchasing)
    case 'WAREHOUSE_TASK': {
      // Sin pantalla 'Tareas de almacén': cada tipo vive en la cola de su pantalla. TASK_CANCELLED no dice el tipo: sin enlace.
      const code = (e.code ?? '').toUpperCase()
      if (code === 'PUTAWAY_DONE') return { to: '/warehouse/receipts?tab=putaway', perm: 'inventory.view', module: wms }
      if (code === 'REPLENISH_DONE') return { to: '/warehouse/pick-batches?tab=replenish', perm: 'inventory.view', module: wms }
      return null
    }
    case 'PRODUCT': {
      const code = (e.code ?? '').toUpperCase()
      if ((code === 'INVENTORY_ADJUSTED' || code === 'INVENTORY_TRANSFERRED' || code === 'BIN_MOVED') && e.publicId)
        return { to: `/warehouse/kardex?product=${encodeURIComponent(e.publicId)}`, perm: 'inventory.view', module: wms }
      return byPublicId('/warehouse/products')
    }
    case 'WAREHOUSE':
      return byPublicId('/warehouse/warehouses')
    case 'CROSSDOCK_PLAN':
      return byId('/warehouse/cross-dock-plans', 'inventory.view', ModuleKeys.CrossDock)
    default:
      return null
  }
}

/**
 * Tono del chip por familia de evento (código del catálogo ActivityEventType), como en el mock:
 * diferencias → fail; ajuste → warn; recibo → deliv (ok); conteo → route; recolección → cod; compra → disp;
 * bajas (producto, almacén) → cap; movimientos, acomodo, reabasto y tareas → wh; el resto neutro.
 */
export function activityTone(code: string | null | undefined): ChipTone {
  const c = (code ?? '').toUpperCase()
  if (c.endsWith('_VARIANCE')) return 'fail'
  if (c === 'INVENTORY_ADJUSTED') return 'warn'
  if (c.endsWith('_DEACTIVATED')) return 'cap'
  if (c.startsWith('RECEIPT_')) return 'deliv'
  if (c.startsWith('COUNT_')) return 'route'
  if (c.startsWith('PICK_')) return 'cod'
  if (c.startsWith('PO_')) return 'disp'
  if (c === 'INVENTORY_TRANSFERRED' || c === 'BIN_MOVED' || c.startsWith('PUTAWAY_') || c.startsWith('REPLENISH_') || c.startsWith('TASK_'))
    return 'wh'
  return 'neutral'
}

/**
 * Hora del evento con los formatos de la compañía y en su zona (`parseApiDate`: el API manda UTC sin zona). Del día de hoy
 * (día local de la compañía) solo la hora; de otro día (ventana de 48 h) también día y mes, para no confundir las 9:00 de
 * ayer con las de hoy.
 */
export function formatEventTime(iso: string | null | undefined, lang: string, now: Date = new Date()): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  const sameDay = formatDate(date) === formatDate(now)
  const time = formatTime(date, lang)
  return sameDay ? time : `${formatDayMonth(date)} ${time}`
}
