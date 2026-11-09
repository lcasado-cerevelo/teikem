// Filtros de la pestaña «Órdenes» de Órdenes de venta (2026-11-09, pedido de Luis): estado de la pantalla y traducción a la consulta del API (puro).
import type { ClientFilterItem } from '../../kernel/ui/ClientSearchSelect'
import { EMPTY_RANGE, type DateRange } from '../../kernel/ui/dateRange'
import type { ProductFilterItem } from '../warehouse/pickers'
import type { SalesDateField, SalesOrderFilters } from './salesApi'

/** Estado de los filtros de la pestaña Órdenes (lo elegido guarda su etiqueta para mostrarla sin volver a pedirla). */
export interface OrdersFilterState {
  q: string
  dateField: SalesDateField
  range: DateRange
  clients: ClientFilterItem[]
  products: ProductFilterItem[]
  statuses: string[]
  mine: boolean
}

export const EMPTY_ORDERS_FILTERS: OrdersFilterState = { q: '', dateField: 'REQUESTED', range: EMPTY_RANGE, clients: [], products: [], statuses: [], mine: false }

/** ¿Hay algún filtro puesto? (para el texto de «sin resultados»). */
export function hasOrdersFilters(f: OrdersFilterState): boolean {
  return Boolean(f.q.trim() || f.range.from || f.range.to || f.clients.length || f.products.length || f.statuses.length || f.mine)
}

/** Lo que va al API (el texto libre ya con su pausa entre teclas). */
export function ordersApiFilters(f: OrdersFilterState, search: string): SalesOrderFilters {
  return {
    q: search,
    dateField: f.dateField,
    from: f.range.from,
    to: f.range.to,
    clientPublicIds: f.clients.map((c) => c.publicId),
    productPublicIds: f.products.map((p) => p.publicId),
    statuses: f.statuses,
    mine: f.mine,
  }
}
