// Lote 14 — lógica pura del Kárdex en la web (P5/P7): un solo estado de filtros (`InventoryFilterState`) compartido por las
// tres pestañas de 'Kárdex de movimientos' (Kárdex, Saldos y Conciliación) y los armadores de consulta de cada endpoint
// (`kardexQuery`, `summaryQuery`, `balancesQuery`, `discrepancyQuery`), qué filtro aplica a qué pestaña (`filterApplies`),
// la lectura de la URL (`filtersFromUrl`), y cómo se pinta un movimiento: tono del tipo (MOV_TYPES de la maqueta: Recepción
// `deliv`, Despacho `route`, Transferencia `disp`, Ajuste `fail`, Cruce de muelle `cod`), cantidad con signo y color, fecha y
// hora por separado, lote/serie, de → a, origen del movimiento y la ruta para abrir su documento (`documentLink`).
import { parseApiDate } from '../../kernel/api/dates'
import { ModuleKeys } from '../../kernel/access/modules'
import type { ChipTone } from '../../kernel/ui/Chip'
import type { DateRange } from '../../kernel/ui/dateRange'
import type { SummaryItem } from '../../kernel/ui/SummaryBar'
import type { GetQuery, KardexDocumentDto, KardexRowDto, KardexSummaryDto } from './api'
import { formatNumber } from './lineRules'
import type { ProductFilterItem } from './pickers'
import { numberLocale } from '../../kernel/i18n'

// ---------------------------------------------------------------------------------------------------------------------
// Estado de filtros compartido
// ---------------------------------------------------------------------------------------------------------------------

export type InventoryTab = 'kardex' | 'balances' | 'reconciliation'

/** Posición elegida en `BinMultiFilter` (se guarda la etiqueta "Código · Zona · Almacén" para no volver a pedirla). */
export interface BinFilterItem {
  id: number
  label: string
}

/** Valor del filtro Dueño para "Propio" (inventario de la compañía); el resto son `clientPublicId`. */
export const OWN_OWNER = 'OWN'

export type KardexDirection = '' | 'IN' | 'OUT'

export interface InventoryFilterState {
  range: DateRange
  /** InternalCode de InventoryTxnType. */
  types: string[]
  warehousePublicIds: string[]
  bins: BinFilterItem[]
  products: ProductFilterItem[]
  /** ids de categoría como texto (el API incluye las subcategorías). */
  categoryIds: string[]
  /** `OWN_OWNER` y/o clientPublicId de los dueños. */
  owners: string[]
  /** InternalCode de AdjustmentReason. */
  reasons: string[]
  direction: KardexDirection
  manualOnly: boolean
  lotNumber: string
  serialNumber: string
  /** Texto libre ya aplicado (QBox con pausa). */
  search: string
  /** Solo Saldos. */
  includeZero: boolean
  onlyAvailable: boolean
  /** Documento de origen que llega en la URL (`refEntity`/`refId`, p. ej. los ajustes de un conteo). */
  refEntity: string
  refId: number | null
  /** Solo Conciliación: estatus de los descuadres (por defecto Pendiente). */
  discrepancyStatus: string[]
}

export const DEFAULT_DISCREPANCY_STATUS: readonly string[] = ['OPEN']

export const EMPTY_INVENTORY_FILTERS: InventoryFilterState = {
  range: { from: '', to: '' },
  types: [],
  warehousePublicIds: [],
  bins: [],
  products: [],
  categoryIds: [],
  owners: [],
  reasons: [],
  direction: '',
  manualOnly: false,
  lotNumber: '',
  serialNumber: '',
  search: '',
  includeZero: false,
  onlyAvailable: false,
  refEntity: '',
  refId: null,
  discrepancyStatus: [...DEFAULT_DISCREPANCY_STATUS],
}

/** Nombre de cada filtro de la barra (para saber a qué pestaña aplica). */
export type InventoryFilterKey =
  | 'range'
  | 'types'
  | 'warehouses'
  | 'bins'
  | 'products'
  | 'categories'
  | 'owners'
  | 'reasons'
  | 'direction'
  | 'manualOnly'
  | 'lot'
  | 'serial'
  | 'search'
  | 'includeZero'
  | 'onlyAvailable'
  | 'status'

const KARDEX_FILTERS: readonly InventoryFilterKey[] = [
  'range',
  'types',
  'warehouses',
  'bins',
  'products',
  'categories',
  'owners',
  'reasons',
  'direction',
  'manualOnly',
  'lot',
  'serial',
  'search',
]

/**
 * Filtros que aplica cada pestaña. Saldos: almacén, posición, producto, categoría, lote, buscador, "Incluir en cero" y
 * "Solo con disponible" (el API de saldos no filtra por fechas, tipo, dueño, motivo ni dirección: esos solo cambian su
 * resumen de movimientos, D13). Conciliación: almacén, posición, producto, categoría, fechas (de detección) y estatus.
 */
export const TAB_FILTERS: Readonly<Record<InventoryTab, readonly InventoryFilterKey[]>> = {
  kardex: KARDEX_FILTERS,
  balances: ['warehouses', 'bins', 'products', 'categories', 'lot', 'search', 'includeZero', 'onlyAvailable'],
  reconciliation: ['range', 'warehouses', 'bins', 'products', 'categories', 'status'],
}

export function filterApplies(tab: InventoryTab, key: InventoryFilterKey): boolean {
  return TAB_FILTERS[tab].includes(key)
}

/** Filtros con valor que la pestaña NO aplica (se atenúan y se nombran en la ayuda de la barra). */
export function inactiveFilters(tab: InventoryTab, f: InventoryFilterState): InventoryFilterKey[] {
  const active: [InventoryFilterKey, boolean][] = [
    ['range', Boolean(f.range.from || f.range.to)],
    ['types', f.types.length > 0],
    ['warehouses', f.warehousePublicIds.length > 0],
    ['bins', f.bins.length > 0],
    ['products', f.products.length > 0],
    ['categories', f.categoryIds.length > 0],
    ['owners', f.owners.length > 0],
    ['reasons', f.reasons.length > 0],
    ['direction', f.direction !== ''],
    ['manualOnly', f.manualOnly],
    ['lot', f.lotNumber.trim() !== ''],
    ['serial', f.serialNumber.trim() !== ''],
    ['search', f.search.trim() !== ''],
    ['includeZero', f.includeZero],
    ['onlyAvailable', f.onlyAvailable],
  ]
  return active.filter(([key, on]) => on && !filterApplies(tab, key)).map(([key]) => key)
}

const nonEmpty = <T>(list: readonly T[]): T[] | undefined => (list.length > 0 ? [...list] : undefined)

/** Dueño → `ownerClientPublicIds` + `includeOwn` del API (Propio = `includeOwn`). */
export function ownerQuery(owners: readonly string[]): { ownerClientPublicIds?: string[]; includeOwn?: boolean } {
  const clients = owners.filter((o) => o !== OWN_OWNER)
  return {
    ownerClientPublicIds: nonEmpty(clients),
    includeOwn: owners.includes(OWN_OWNER) ? true : undefined,
  }
}

/** true si las fechas están al revés (el API respondería 400; la pantalla avisa sin consultar). */
export function rangeInverted(range: DateRange): boolean {
  return Boolean(range.from && range.to && range.from > range.to)
}

/** Consulta del Kárdex (sin `skip`/`take`): la tabla, su Exportar y el resumen. */
export function kardexQuery(f: InventoryFilterState): GetQuery<'/api/v1/inventory/transactions'> {
  return {
    from: f.range.from || undefined,
    to: f.range.to || undefined,
    types: nonEmpty(f.types),
    warehousePublicIds: nonEmpty(f.warehousePublicIds),
    binIds: nonEmpty(f.bins.map((b) => b.id)),
    productPublicIds: nonEmpty(f.products.map((p) => p.publicId)),
    categoryIds: nonEmpty(f.categoryIds.map(Number)),
    ...ownerQuery(f.owners),
    reasons: nonEmpty(f.reasons),
    direction: f.direction || undefined,
    manualOnly: f.manualOnly || undefined,
    lotNumber: f.lotNumber.trim() || undefined,
    serialNumber: f.serialNumber.trim() || undefined,
    search: f.search.trim() || undefined,
    refEntity: f.refEntity || undefined,
    refId: f.refEntity && f.refId != null ? f.refId : undefined,
  }
}

/** Resumen (`/transactions/summary`): los MISMOS filtros que la lista, también en Saldos (D13). */
export function summaryQuery(f: InventoryFilterState): GetQuery<'/api/v1/inventory/transactions/summary'> {
  return kardexQuery(f)
}

/** Consulta de Saldos (sin `skip`/`take`). */
export function balancesQuery(f: InventoryFilterState): GetQuery<'/api/v1/inventory/balances'> {
  return {
    warehousePublicIds: nonEmpty(f.warehousePublicIds),
    binIds: nonEmpty(f.bins.map((b) => b.id)),
    productPublicIds: nonEmpty(f.products.map((p) => p.publicId)),
    categoryIds: nonEmpty(f.categoryIds.map(Number)),
    lotNumber: f.lotNumber.trim() || undefined,
    includeZero: f.includeZero || undefined,
    onlyAvailable: f.onlyAvailable || undefined,
    search: f.search.trim() || undefined,
  }
}

/** Consulta de descuadres (sin `skip`/`take`): estatus, almacén, posición, producto, categoría y fecha de detección. */
export function discrepancyQuery(f: InventoryFilterState): GetQuery<'/api/v1/inventory/discrepancies'> {
  return {
    status: nonEmpty(f.discrepancyStatus),
    warehousePublicIds: nonEmpty(f.warehousePublicIds),
    binIds: nonEmpty(f.bins.map((b) => b.id)),
    productPublicIds: nonEmpty(f.products.map((p) => p.publicId)),
    categoryIds: nonEmpty(f.categoryIds.map(Number)),
    from: f.range.from || undefined,
    to: f.range.to || undefined,
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// URL (contrato de enlaces: Pulso, reportes, conteo, "Necesita tu atención")
// ---------------------------------------------------------------------------------------------------------------------

/** Valores de un parámetro repetible o separado por comas, sin vacíos ni duplicados. */
export function listParam(params: URLSearchParams, name: string): string[] {
  const values = params
    .getAll(name)
    .flatMap((v) => v.split(','))
    .map((v) => v.trim())
    .filter(Boolean)
  return [...new Set(values)]
}

const DAY = /^\d{4}-\d{2}-\d{2}$/

/** Pestaña de `?tab=`: la primera (Kárdex) va sin parámetro; valor desconocido = la primera. */
export function tabFromParam(value: string | null): InventoryTab {
  return value === 'balances' || value === 'reconciliation' ? value : 'kardex'
}

/**
 * Filtros iniciales de la URL (se leen UNA vez al montar y valen para las tres pestañas): `warehousePublicIds`, `product`
 * (publicId; el SKU se resuelve después), `categoryIds`, `types`, `reasons`, `direction` (IN/OUT), `manualOnly=true`,
 * `from`/`to` (YYYY-MM-DD), `refEntity` + `refId` (documento de origen) y `status` (estatus de descuadres).
 */
export function filtersFromUrl(params: URLSearchParams): InventoryFilterState {
  const direction = (params.get('direction') ?? '').toUpperCase()
  const refId = Number(params.get('refId'))
  const refEntity = (params.get('refEntity') ?? '').trim().toUpperCase()
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const status = listParam(params, 'status')
  return {
    ...EMPTY_INVENTORY_FILTERS,
    range: { from: DAY.test(from) ? from : '', to: DAY.test(to) ? to : '' },
    warehousePublicIds: listParam(params, 'warehousePublicIds'),
    products: listParam(params, 'product').map((publicId) => ({ publicId, sku: '', label: '' })),
    categoryIds: listParam(params, 'categoryIds').filter((v) => /^\d+$/.test(v)),
    types: listParam(params, 'types'),
    reasons: listParam(params, 'reasons'),
    direction: direction === 'IN' || direction === 'OUT' ? direction : '',
    manualOnly: params.get('manualOnly') === 'true',
    refEntity: refEntity && Number.isInteger(refId) && refId > 0 ? refEntity : '',
    refId: refEntity && Number.isInteger(refId) && refId > 0 ? refId : null,
    discrepancyStatus: status.length > 0 ? status : [...DEFAULT_DISCREPANCY_STATUS],
  }
}

/** `?txn=<id>` (abre el detalle de un movimiento) o null. */
export function txnParam(params: URLSearchParams): number | null {
  const n = Number(params.get('txn'))
  return Number.isInteger(n) && n > 0 ? n : null
}

// ---------------------------------------------------------------------------------------------------------------------
// Cómo se pinta un movimiento
// ---------------------------------------------------------------------------------------------------------------------

const TYPE_TONES: Record<string, ChipTone> = {
  RECEIPT: 'deliv',
  ISSUE: 'route',
  TRANSFER: 'disp',
  ADJUSTMENT: 'fail',
  CROSSDOCK: 'cod',
}

/** Tono del chip del tipo de movimiento (MOV_TYPES de la maqueta); desconocido = neutro. */
export function txnTypeTone(code: string | null | undefined): ChipTone {
  return (code && TYPE_TONES[code.toUpperCase()]) || 'neutral'
}

/** Clase de color de una cantidad: entra (flujo), sale (peligro) o cero. */
export function qtyClass(q: number | null | undefined): 'qty-in' | 'qty-out' | 'qty-zero' {
  if (!q) return 'qty-zero'
  return q > 0 ? 'qty-in' : 'qty-out'
}

/** Cantidad con signo explícito ("+2", "−3", "0") con los separadores del idioma. */
export function formatSignedQty(q: number | null | undefined, lang: string): string {
  const n = q ?? 0
  const text = new Intl.NumberFormat(numberLocale(lang), { maximumFractionDigits: 3 }).format(Math.abs(n))
  if (n > 0) return `+${text}`
  if (n < 0) return `−${text}`
  return text
}

/** Fecha y hora por separado (columnas Fecha y Hora de la maqueta), en hora local del navegador. */
export function splitDateTime(iso: string | null | undefined, lang: string): { date: string; time: string } {
  if (!iso) return { date: '', time: '' }
  const d = parseApiDate(iso)
  if (Number.isNaN(d.getTime())) return { date: '', time: '' }
  return {
    date: new Intl.DateTimeFormat(lang, { dateStyle: 'short' }).format(d),
    time: new Intl.DateTimeFormat(lang, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(d),
  }
}

/** "Lote · Serie" de un movimiento ('' si no tiene). */
export function lotSerialText(r: Pick<KardexRowDto, 'lotNumber' | 'serialNumber'>): string {
  return [r.lotNumber, r.serialNumber].filter(Boolean).join(' · ')
}

/** Un lado de un movimiento: "ALM-01/A-01", solo el almacén o ''. */
export function sideText(warehouse: string | null | undefined, bin: string | null | undefined): string {
  return [warehouse, bin].filter(Boolean).join('/')
}

/** "De → A" de un movimiento; un movimiento de un solo lado muestra ese lado. */
export function fromToText(r: Pick<KardexRowDto, 'fromWarehouseCode' | 'fromBinCode' | 'toWarehouseCode' | 'toBinCode' | 'position'>): string {
  const from = sideText(r.fromWarehouseCode, r.fromBinCode)
  const to = sideText(r.toWarehouseCode, r.toBinCode)
  if (from && to) return `${from} → ${to}`
  return from || to || r.position || ''
}

/**
 * Origen del movimiento (columna de Transferencias y filtro "Solo manuales"): sin referencia = manual; si no, según el
 * documento (`refEntityCode`): conteo, acomodo o reabasto (tarea de almacén; el reabasto por producto), recibo,
 * recolección, compra, cruce de muelle u otro. Texto con `t('warehouse.kardexView.origin.<clave>')`.
 */
export type MovementOrigin = 'manual' | 'count' | 'task' | 'replenish' | 'receipt' | 'pick' | 'purchase' | 'crossdock' | 'order' | 'other'

export function movementOrigin(refEntityCode: string | null | undefined): MovementOrigin {
  switch ((refEntityCode ?? '').toUpperCase()) {
    case '':
      return 'manual'
    case 'CYCLE_COUNT':
      return 'count'
    case 'WAREHOUSE_TASK':
      return 'task'
    case 'PRODUCT':
      return 'replenish'
    case 'RECEIPT':
      return 'receipt'
    case 'PICK_BATCH':
      return 'pick'
    case 'PURCHASE_ORDER':
      return 'purchase'
    case 'CROSSDOCK_ALLOCATION':
    case 'CROSSDOCK_PLAN':
      return 'crossdock'
    case 'TRANSPORT_ORDER':
      return 'order'
    default:
      return 'other'
  }
}

/** Ruta para abrir el documento de origen, con el permiso y el módulo que exige esa pantalla. */
export interface DocumentLink {
  to: string
  perm: string
  module: string
}

/**
 * Ruta del documento de un movimiento (`KardexDocumentDto`): recibo `?receipt=`, recolección, conteo `?count=`, compra,
 * orden, producto y plan de cruce; la tarea de almacén abre su documento padre. null si no hay pantalla que lo abra.
 */
export function documentLink(doc: KardexDocumentDto | null | undefined): DocumentLink | null {
  if (!doc?.entityCode) return null
  const wms = { perm: 'inventory.view', module: ModuleKeys.WmsLotSerial }
  switch (doc.entityCode.toUpperCase()) {
    case 'RECEIPT':
      return doc.publicId ? { to: `/warehouse/receipts?receipt=${doc.publicId}`, ...wms } : null
    case 'PICK_BATCH':
      return doc.publicId ? { to: `/warehouse/pick-batches/${doc.publicId}`, ...wms } : null
    case 'CYCLE_COUNT':
      return doc.id ? { to: `/warehouse/cycle-counts?count=${doc.id}`, ...wms } : null
    case 'PURCHASE_ORDER':
      return doc.publicId ? { to: `/warehouse/purchase-orders/${doc.publicId}`, perm: 'purchasing.view', module: ModuleKeys.Purchasing } : null
    case 'TRANSPORT_ORDER':
      return doc.publicId ? { to: `/orders/${doc.publicId}`, perm: 'orders.view', module: ModuleKeys.LtlGround } : null
    case 'PRODUCT':
      return doc.publicId ? { to: `/warehouse/products/${doc.publicId}`, ...wms } : null
    case 'CROSSDOCK_PLAN':
      return doc.id ? { to: `/warehouse/cross-dock-plans/${doc.id}`, perm: 'inventory.view', module: ModuleKeys.CrossDock } : null
    case 'WAREHOUSE_TASK':
      return documentLink(doc.parent)
    default:
      return null
  }
}

/** "Código · Zona · Almacén" de una posición de la búsqueda entre almacenes (opción y píldora de `BinMultiFilter`). */
export function binFilterLabel(b: { code?: string | null; zoneCode?: string | null; warehouseCode?: string | null }): string {
  return [b.code, b.zoneCode, b.warehouseCode].filter(Boolean).join(' · ')
}

/** Cifras del resumen de movimientos (D13: en Saldos se agregan En mano y Disponible). */
export function summaryItems(
  s: KardexSummaryDto | undefined,
  t: (key: string, params?: Record<string, string | number>) => string,
  lang: string,
  balances?: { onHand: number | null; available: number | null },
): SummaryItem[] {
  const n = (v: number | undefined) => (s ? formatNumber(v ?? 0, lang) : '—')
  // con signo y color solo si hay unidades: en cero se muestra "0" neutro (no "+0" ni "−0" en rojo)
  const signed = (v: number | undefined, sign: '+' | '−') =>
    !s ? '—' : (v ?? 0) === 0 ? formatNumber(0, lang) : `${sign}${formatNumber(v ?? 0, lang)}`
  const items: SummaryItem[] = [
    { key: 'movements', label: t('warehouse.inventory.summary.movements'), value: n(s?.movements) },
    { key: 'inCount', label: t('warehouse.inventory.summary.inCount'), value: n(s?.inCount) },
    { key: 'inQty', label: t('warehouse.inventory.summary.inQty'), value: signed(s?.inQty, '+'), tone: s?.inQty ? 'in' : undefined },
    { key: 'outCount', label: t('warehouse.inventory.summary.outCount'), value: n(s?.outCount) },
    { key: 'outQty', label: t('warehouse.inventory.summary.outQty'), value: signed(s?.outQty, '−'), tone: s?.outQty ? 'out' : undefined },
    { key: 'internal', label: t('warehouse.inventory.summary.internal'), value: n(s?.internalCount), title: t('warehouse.inventory.summary.internalHint') },
  ]
  if (balances) {
    items.push(
      { key: 'onHand', label: t('warehouse.inventory.summary.onHand'), value: balances.onHand != null ? formatNumber(balances.onHand, lang) : '—', tone: 'money' },
      { key: 'available', label: t('warehouse.inventory.summary.available'), value: balances.available != null ? formatNumber(balances.available, lang) : '—', tone: 'money' },
    )
  }
  return items
}


/**
 * Cantidad de un movimiento para pintar: con signo y color según la perspectiva del filtro (`signedQuantity`); una
 * transferencia interna (sin filtro de ubicación el API da `signedQuantity` 0) muestra lo que se movió, sin signo.
 */
export function movementQtyView(
  r: Pick<KardexRowDto, 'signedQuantity' | 'quantity'>,
  lang: string,
): { text: string; className: 'qty-in' | 'qty-out' | 'qty-zero'; value: number } {
  const signed = r.signedQuantity ?? r.quantity ?? 0
  if (signed === 0 && (r.quantity ?? 0) !== 0) {
    const q = Math.abs(r.quantity ?? 0)
    return { text: new Intl.NumberFormat(numberLocale(lang), { maximumFractionDigits: 3 }).format(q), className: 'qty-zero', value: q }
  }
  return { text: formatSignedQty(signed, lang), className: qtyClass(signed), value: signed }
}
