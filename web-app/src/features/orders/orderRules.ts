// Reglas puras de Órdenes (Lote F-C1): filtros de la lista → consulta del API, filas por tipo de paquete, esquema del alta
// detallada (espejo de las validaciones del servidor, manual 03 §1.1 con el mensaje exacto) y request de `POST /orders`.
// Sin React: se prueba en `orderRules.test.ts`.
import { z } from 'zod'
import { ApiError } from '../../kernel/api/problem'
import type { OrderCreateRequest, OrderDetailDto, OrderListItemDto, OrderListQuery, OrderPackageLineDto, OrderPatchRequest } from './api'

// ---------------------------------------------------------------------------------------------------------------------
// Lista: filtros → consulta
// ---------------------------------------------------------------------------------------------------------------------

export interface OrderFilters {
  /** 'YYYY-MM-DD' o ''; `to` incluye el día elegido. */
  from: string
  to: string
  orderNumber: string
  consignee: string
  clientIds: string[]
  serviceTypes: string[]
  packageTypes: string[]
  cities: string[]
  postalCodePrefix: string
  invoice: string
  packBatch: string
  statuses: string[]
  /** Buscador libre (QBox): número, factura, empaque y consignatario. */
  search: string
  /** Lote 128: factura del cliente procesada ('' = todas, 'yes', 'no') y la fecha de procesamiento ('YYYY-MM-DD' o ''). */
  processed: '' | 'yes' | 'no'
  processedFrom: string
  processedTo: string
  /** Lote 129: ids (como texto) de las zonas de entrega y de los niveles de zona. */
  zoneIds: string[]
  zoneGroupIds: string[]
}

/**
 * Lote 89 (P12): los filtros que trae el enlace del Radar del Pulso — `?city=` (pueblo), `?client=` (publicId del cliente) y `?statuses=` (códigos separados por coma). Sin parámetros, sin filtros.
 */
export function filtersFromParams(params: URLSearchParams): OrderFilters {
  const city = params.get('city')?.trim()
  const client = params.get('client')?.trim()
  const statuses = (params.get('statuses') ?? '').split(',').map((x) => x.trim()).filter(Boolean)
  return { ...EMPTY_ORDER_FILTERS, cities: city ? [city] : [], clientIds: client ? [client] : [], statuses }
}

export const EMPTY_ORDER_FILTERS: OrderFilters = {
  from: '',
  to: '',
  orderNumber: '',
  consignee: '',
  clientIds: [],
  serviceTypes: [],
  packageTypes: [],
  cities: [],
  postalCodePrefix: '',
  invoice: '',
  packBatch: '',
  statuses: [],
  search: '',
  processed: '',
  processedFrom: '',
  processedTo: '',
  zoneIds: [],
  zoneGroupIds: [],
}

/** `to` del API es exclusivo sobre la fecha de creación: se manda el día siguiente para incluir el día elegido. */
export function exclusiveTo(day: string): string | undefined {
  if (!day) return undefined
  const d = new Date(`${day}T00:00:00Z`)
  if (Number.isNaN(d.getTime())) return undefined
  d.setUTCDate(d.getUTCDate() + 1)
  return d.toISOString().slice(0, 10)
}

const text = (v: string) => v.trim() || undefined
const list = (v: readonly string[]) => (v.length > 0 ? [...v] : undefined)

/** Consulta de `GET /orders` con los filtros de la pantalla; lo vacío no se manda. Los arreglos se repiten en la URL. */
export function buildOrderQuery(f: OrderFilters, page: number, pageSize: number): OrderListQuery {
  return {
    from: f.from || undefined,
    to: exclusiveTo(f.to),
    orderNumber: text(f.orderNumber),
    consignee: text(f.consignee),
    clientIds: list(f.clientIds),
    serviceTypes: list(f.serviceTypes),
    packageTypes: list(f.packageTypes),
    cities: list(f.cities),
    postalCodePrefix: text(f.postalCodePrefix),
    invoice: text(f.invoice),
    packBatch: text(f.packBatch),
    statuses: list(f.statuses),
    search: text(f.search),
    invoiceProcessed: f.processed === 'yes' ? true : f.processed === 'no' ? false : undefined,
    processedFrom: f.processedFrom || undefined,
    processedTo: exclusiveTo(f.processedTo),
    zoneIds: f.zoneIds.length > 0 ? f.zoneIds.map(Number) : undefined,
    zoneGroupIds: f.zoneGroupIds.length > 0 ? f.zoneGroupIds.map(Number) : undefined,
    skip: (page - 1) * pageSize,
    take: pageSize,
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Lista: una fila por tipo de paquete
// ---------------------------------------------------------------------------------------------------------------------

export interface PackageGroup {
  /** Etiqueta del tipo de paquete (o, en una entrega especial, el nombre del servicio). */
  label: string
  /** Descripciones distintas de las líneas de ese tipo. */
  descriptions: string[]
  pieces: number
}

/** Agrupa las líneas de paquete de una orden por tipo (en el orden en que aparecen): una fila de la lista por grupo. */
export function packageGroups(o: Pick<OrderListItemDto, 'packages' | 'packagesSummary' | 'totalPieces'>): PackageGroup[] {
  const lines: readonly OrderPackageLineDto[] = o.packages ?? []
  if (lines.length === 0) return [{ label: o.packagesSummary || '—', descriptions: [], pieces: o.totalPieces ?? 0 }]
  const groups = new Map<string, PackageGroup>()
  for (const l of lines) {
    const typeLabel = l.packageTypeLabel || l.packageType || ''
    const label = typeLabel || l.description || '—'
    const key = (l.packageType || label).toLowerCase()
    const g = groups.get(key) ?? { label, descriptions: [], pieces: 0 }
    g.pieces += l.pieces ?? 0
    const desc = typeLabel ? (l.description ?? '').trim() : ''
    if (desc && !g.descriptions.includes(desc)) g.descriptions.push(desc)
    groups.set(key, g)
  }
  return [...groups.values()]
}

/** «Caja ×2 + Sobre ×1», como el ticket: tipos de paquete con sus piezas. */
export function packageSummaryText(groups: readonly PackageGroup[]): string {
  return groups.map((g) => `${g.label} ×${g.pieces}`).join(' + ')
}

/** La papelera solo en el estatus inicial (el servidor lo vuelve a exigir con 422). */
export function canDeleteOrder(o: Pick<OrderListItemDto, 'isInitialStatus' | 'isActive'>): boolean {
  return o.isInitialStatus === true && o.isActive !== false
}

// ---------------------------------------------------------------------------------------------------------------------
// Alta detallada: esquema, valores y request
// ---------------------------------------------------------------------------------------------------------------------

export const NUMBER_MAX = 40
export const DESCRIPTION_MAX = 200

export interface PackageValues {
  packageType: string
  description: string
  pieces: number | null
}

export interface OrderFormValues {
  clientPublicId: string | null
  serviceType: string
  /** «Lista» → requestedDate. */
  readyDate: string
  /** «Límite» → promisedDate. */
  dueDate: string
  clientInvoiceNumber: string
  orderNumber: string
  consigneeLocationPublicId: string
  isSpecial: boolean
  /** id del servicio especial del cliente (texto del `<select>`). */
  specialServiceId: string
  packages: PackageValues[]
  codAmount: number | null
}

export function emptyPackage(packageType = ''): PackageValues {
  return { packageType, description: '', pieces: 1 }
}

export function emptyOrderValues(today: string): OrderFormValues {
  return {
    clientPublicId: null,
    serviceType: '',
    readyDate: today,
    dueDate: today,
    clientInvoiceNumber: '',
    orderNumber: '',
    consigneeLocationPublicId: '',
    isSpecial: false,
    specialServiceId: '',
    packages: [emptyPackage()],
    codAmount: null,
  }
}

/** Mensajes de validación (ya traducidos; los del servidor, tal cual el manual 03 §1.1). */
export interface OrderMessages {
  clientRequired: string
  serviceRequired: string
  consigneeRequired: string
  packagesRequired: string
  packageTypeRequired: string
  pieces: string
  codNegative: string
  specialRequired: string
  numberMax: string
  descriptionMax: string
  dueBeforeReady: string
}

export function orderSchema(m: OrderMessages) {
  return z
    .object({
      clientPublicId: z.string().nullable(),
      serviceType: z.string(),
      readyDate: z.string(),
      dueDate: z.string(),
      clientInvoiceNumber: z.string().max(NUMBER_MAX, m.numberMax),
      orderNumber: z.string().max(NUMBER_MAX, m.numberMax),
      consigneeLocationPublicId: z.string(),
      isSpecial: z.boolean(),
      specialServiceId: z.string(),
      packages: z.array(
        z.object({ packageType: z.string(), description: z.string().max(DESCRIPTION_MAX, m.descriptionMax), pieces: z.number().nullable() }),
      ),
      codAmount: z.number().nullable(),
    })
    .superRefine((v, ctx) => {
      const issue = (path: (string | number)[], message: string) => ctx.addIssue({ code: 'custom', path, message })
      if (!v.clientPublicId) issue(['clientPublicId'], m.clientRequired)
      if (!v.serviceType) issue(['serviceType'], m.serviceRequired)
      if (v.readyDate && v.dueDate && v.dueDate < v.readyDate) issue(['dueDate'], m.dueBeforeReady)
      if (!v.consigneeLocationPublicId) issue(['consigneeLocationPublicId'], m.consigneeRequired)
      if (v.isSpecial) {
        if (!v.specialServiceId) issue(['specialServiceId'], m.specialRequired)
        return
      }
      if (v.packages.length === 0) issue(['packages'], m.packagesRequired)
      v.packages.forEach((p, i) => {
        if (!p.packageType) issue(['packages', i, 'packageType'], m.packageTypeRequired)
        if (p.pieces == null || !Number.isInteger(p.pieces) || p.pieces < 1) issue(['packages', i, 'pieces'], m.pieces)
      })
      if (v.codAmount != null && v.codAmount < 0) issue(['codAmount'], m.codNegative)
    })
}

export interface OrderRequestOptions {
  /** Reintento autorizado por quien tiene `orders.credit_override` (al crear se cotiza y se mide el crédito). */
  overrideCredit?: boolean
  /** Reintento tras «Factura repetida… Crear de todos modos». */
  confirmDuplicateInvoice?: boolean
  /** El cliente asigna el número de factura / de orden: solo entonces se manda lo tecleado (si no, el servidor responde 400). */
  invoiceEditable: boolean
  orderNumberEditable: boolean
}

/** `'YYYY-MM-DD'` → fecha-hora sin zona (`'YYYY-MM-DDT00:00:00'`, como el ejemplo del manual); vacío → null. */
export function dayStart(day: string): string | null {
  return day ? `${day}T00:00:00` : null
}

/** Request de `POST /orders`. Una entrega especial va sin paquetes ni COD y sin chofer (se asigna después). */
export function buildOrderRequest(v: OrderFormValues, o: OrderRequestOptions): OrderCreateRequest {
  const req: OrderCreateRequest = {
    clientPublicId: v.clientPublicId,
    consigneeLocationPublicId: v.consigneeLocationPublicId || null,
    serviceType: v.serviceType || null,
    requestedDate: dayStart(v.readyDate),
    promisedDate: dayStart(v.dueDate),
    clientInvoiceNumber: o.invoiceEditable ? v.clientInvoiceNumber.trim() || null : null,
  }
  if (o.confirmDuplicateInvoice) req.confirmDuplicateInvoice = true
  if (o.overrideCredit) req.overrideCredit = true
  if (v.isSpecial) {
    req.isSpecialDelivery = true
    req.specialServiceId = Number(v.specialServiceId)
    if (o.orderNumberEditable) req.orderNumber = v.orderNumber.trim() || null
    return req
  }
  req.packages = v.packages.map((p) => ({ packageType: p.packageType || null, description: p.description.trim() || null, pieces: p.pieces ?? 1 }))
  req.codAmount = v.codAmount != null && v.codAmount > 0 ? v.codAmount : null
  return req
}

// ---------------------------------------------------------------------------------------------------------------------
// Errores del servidor
// ---------------------------------------------------------------------------------------------------------------------

/** Campos del ProblemDetails que son datos para armar el modal de factura repetida, no errores que pintar. */
const DUPLICATE_DATA_KEYS: ReadonlySet<string> = new Set(['existingOrderNumber', 'existingOrderPublicId'])

/**
 * Nombres de campo del servidor → los del formulario: `consignee`/`newConsignee.*` → el consignatario elegido,
 * `packages[0].packageType` → `packages.0.packageType`; los datos de factura repetida no se pintan como error.
 */
export function remapOrderProblem(err: unknown): unknown {
  if (!(err instanceof ApiError)) return err
  const errors: Record<string, string[]> = {}
  for (const [k, v] of Object.entries(err.errors)) {
    if (DUPLICATE_DATA_KEYS.has(k)) continue
    const bare = k.startsWith('$.') ? k.slice(2) : k
    let name = bare
    if (bare === 'consignee' || bare.startsWith('newConsignee')) name = 'consigneeLocationPublicId'
    else {
      const m = /^packages\[(\d+)\]\.(\w+)$/.exec(bare)
      if (m) name = `packages.${m[1]}.${m[2]}`
    }
    errors[name] = [...(errors[name] ?? []), ...v]
  }
  return new ApiError(err.status, { title: err.title, code: err.code, errors, correlationId: err.correlationId })
}

/** 422 `credit_exceeded` al confirmar: el cliente excede su límite de crédito. */
export function isCreditExceeded(err: unknown): err is ApiError {
  return err instanceof ApiError && err.status === 422 && err.code === 'credit_exceeded'
}

export interface DuplicateInvoice {
  /** true = el consignatario admite repetidas y se puede crear de todos modos; false = bloqueado. */
  confirmable: boolean
  message: string
  existingOrderNumber: string
  existingOrderPublicId: string
}

/** 409 `duplicate_invoice` / `duplicate_invoice_confirmable` (R36) con los datos de la orden que ya usa el número. */
export function duplicateInvoiceOf(err: unknown): DuplicateInvoice | null {
  if (!(err instanceof ApiError) || err.status !== 409) return null
  if (err.code !== 'duplicate_invoice' && err.code !== 'duplicate_invoice_confirmable') return null
  return {
    confirmable: err.code === 'duplicate_invoice_confirmable',
    message: err.title,
    existingOrderNumber: err.errors.existingOrderNumber?.[0] ?? '',
    existingOrderPublicId: err.errors.existingOrderPublicId?.[0] ?? '',
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Expediente (Lote F-C2): qué acciones se ven, derivación de «asignable», edición dentro del Detalle y su PATCH
// ---------------------------------------------------------------------------------------------------------------------

/** Máximo del comentario de cancelar / cambiar de estatus / asignar chofer (EntityStatusHistory.Comment, NVARCHAR(500)). */
export const COMMENT_MAX = 500

/** Etapa hasta la que el servidor deja asignar o reasignar el chofer de una entrega especial (pasada esta, responde 422). */
export const IN_TRANSIT_CODE = 'IN_TRANSIT'

/** Lo que el usuario puede hacer (permisos y módulos del API) para decidir qué acciones pintar. */
export interface OrderAbilities {
  edit: boolean
  cancel: boolean
  creditOverride: boolean
  /** `orders.reopen`. */
  reopen?: boolean
  dispatch: boolean
  viewPrice: boolean
  /** Módulo CATALOG encendido (choferes y tarifas); sin él el servidor rechaza la asignación. */
  catalogOn: boolean
}

/** `normal` = «Marcar como recogida» quien tiene `orders.edit`; `overrideOnly` = solo `orders.credit_override` (Facturación): únicamente autoriza el exceso. */
export type ConfirmMode = 'normal' | 'overrideOnly'

export interface OrderActionVisibility {
  /** null = no se ofrece «Marcar como recogida». */
  confirm: ConfirmMode | null
  reprice: boolean
  cancel: boolean
  /** «Reabrir»: `capabilities.canReopen` Y `orders.reopen`. */
  reopen: boolean
  delete: boolean
  assignDriver: boolean
  /** Edición en el Detalle (`capabilities.canEditCargo` con `orders.edit`). */
  edit: boolean
  /** Cotizar (panel «Cotización»): solo con `orders.view_price`. */
  quote: boolean
  /** Estatus laterales y regreso (`orders.edit`). */
  lateral: boolean
}

type ActionOrder = Pick<OrderDetailDto, 'isActive' | 'capabilities'>

/** Cada acción necesita el permiso Y la capacidad que trae el DTO (el servidor las vuelve a exigir). */
export function orderActionVisibility(order: ActionOrder, can: OrderAbilities, assignable: boolean): OrderActionVisibility {
  const cap = order.capabilities ?? {}
  const active = order.isActive !== false
  return {
    confirm: cap.canConfirm && active ? (can.edit ? 'normal' : can.creditOverride ? 'overrideOnly' : null) : null,
    reprice: !!cap.canReprice && can.edit && active,
    cancel: !!cap.canCancel && can.cancel && active,
    reopen: !!cap.canReopen && !!can.reopen && active,
    delete: !!cap.canDelete && can.cancel && active,
    assignDriver: assignable && can.dispatch && can.catalogOn && active,
    edit: !!cap.canEditCargo && can.edit && active,
    quote: can.viewPrice && active,
    lateral: can.edit && active,
  }
}

interface StatusLike {
  code: string
  stageKind: string
  sortOrder: number
}

/**
 * ¿Se puede asignar o reasignar chofer? Solo una entrega especial cuyo estatus sigue en el pipeline hasta `IN_TRANSIT` INCLUSIVE (en
 * tránsito todavía se reasigna; no en un lateral, un terminal ni después de IN_TRANSIT: `SpecialDeliveryDispatchService`). Es una
 * derivación para ofrecer el botón: el servidor decide con 422 si algo cambió.
 */
export function isAssignableOrder(order: Pick<OrderDetailDto, 'isSpecialDelivery' | 'isActive' | 'status'>, statuses: readonly StatusLike[]): boolean {
  if (!order.isSpecialDelivery || order.isActive === false) return false
  const current = statuses.find((s) => s.code.toLowerCase() === (order.status ?? '').toLowerCase())
  if (!current || current.stageKind.toUpperCase() !== 'PIPELINE') return false
  const inTransit = statuses.find((s) => s.code.toLowerCase() === IN_TRANSIT_CODE.toLowerCase())
  return !inTransit || current.sortOrder <= inTransit.sortOrder
}

/** «YYYY-MM-DD» de una fecha de la orden (se guardan como «YYYY-MM-DDT00:00:00» sin zona: el día no se corre de zona). */
export function orderDay(iso: string | null | undefined): string {
  return iso ? iso.slice(0, 10) : ''
}

export interface OrderEditValues {
  serviceType: string
  /** Código de `OrderPriority` ('' = sin prioridad). */
  priority: string
  readyDate: string
  dueDate: string
  consigneeLocationPublicId: string
  packages: PackageValues[]
  codAmount: number | null
  notes: string
}

/** Valores del formulario de edición a partir de la ficha. */
export function orderEditValues(o: OrderDetailDto): OrderEditValues {
  return {
    serviceType: o.serviceType ?? '',
    priority: (o.priority ?? '').toUpperCase(),
    readyDate: orderDay(o.requestedDate),
    dueDate: orderDay(o.promisedDate),
    consigneeLocationPublicId: o.delivery?.locationPublicId ?? '',
    packages: (o.packages ?? []).map((p) => ({ packageType: p.packageType ?? '', description: p.description ?? '', pieces: p.pieces ?? 1 })),
    codAmount: o.codAmount != null && o.codAmount > 0 ? o.codAmount : null,
    notes: o.notes ?? '',
  }
}

/** Mensajes de validación de la edición (ya traducidos). */
export interface OrderEditMessages extends OrderMessages {
  dateRequired: string
}

/** Esquema de la edición en el Detalle: las mismas reglas del alta, salvo cliente, números y servicio especial (no se editan). */
export function orderEditSchema(m: OrderEditMessages, original: { hadReady: boolean; hadDue: boolean; isSpecial: boolean }) {
  return z
    .object({
      serviceType: z.string(),
      priority: z.string(),
      readyDate: z.string(),
      dueDate: z.string(),
      consigneeLocationPublicId: z.string(),
      packages: z.array(z.object({ packageType: z.string(), description: z.string().max(DESCRIPTION_MAX, m.descriptionMax), pieces: z.number().nullable() })),
      codAmount: z.number().nullable(),
      notes: z.string(),
    })
    .superRefine((v, ctx) => {
      const issue = (path: (string | number)[], message: string) => ctx.addIssue({ code: 'custom', path, message })
      if (!v.serviceType) issue(['serviceType'], m.serviceRequired)
      if (!v.consigneeLocationPublicId) issue(['consigneeLocationPublicId'], m.consigneeRequired)
      // el servidor lee una fecha vacía como «no cambiar»: quitarla no es posible, y callarlo confundiría
      if (!v.readyDate && original.hadReady) issue(['readyDate'], m.dateRequired)
      if (!v.dueDate && original.hadDue) issue(['dueDate'], m.dateRequired)
      if (v.readyDate && v.dueDate && v.dueDate < v.readyDate) issue(['dueDate'], m.dueBeforeReady)
      if (original.isSpecial) return
      if (v.packages.length === 0) issue(['packages'], m.packagesRequired)
      v.packages.forEach((p, i) => {
        if (!p.packageType) issue(['packages', i, 'packageType'], m.packageTypeRequired)
        if (p.pieces == null || !Number.isInteger(p.pieces) || p.pieces < 1) issue(['packages', i, 'pieces'], m.pieces)
      })
      if (v.codAmount != null && v.codAmount < 0) issue(['codAmount'], m.codNegative)
    })
}

const sameText = (a: string | null | undefined, b: string | null | undefined) => (a ?? '').trim().toUpperCase() === (b ?? '').trim().toUpperCase()

/** Una línea de paquete en la forma en que se compara la guardada con la editada. */
function lineKey(p: { packageType: string | null | undefined; description: string | null | undefined; pieces: number | null | undefined }): string {
  return `${(p.packageType ?? '').toUpperCase()}|${(p.description ?? '').trim()}|${p.pieces ?? 0}`
}

/**
 * Solo lo que cambió respecto de la ficha (el servidor lee `null` como «no cambiar»): texto tal cual (`''` borra), paquetes como
 * reemplazo completo, COD con `codAmount` o `clearCod` (excluyentes). Una entrega especial no edita paquetes ni COD.
 */
export function orderPatchChanges(o: OrderDetailDto, v: OrderEditValues): OrderPatchRequest {
  const patch: OrderPatchRequest = {}
  if (v.serviceType && !sameText(v.serviceType, o.serviceType)) patch.serviceType = v.serviceType
  if (!sameText(v.priority, o.priority)) patch.priority = v.priority
  if (v.readyDate && v.readyDate !== orderDay(o.requestedDate)) patch.requestedDate = dayStart(v.readyDate)
  if (v.dueDate && v.dueDate !== orderDay(o.promisedDate)) patch.promisedDate = dayStart(v.dueDate)
  if (v.consigneeLocationPublicId && v.consigneeLocationPublicId !== (o.delivery?.locationPublicId ?? '')) patch.consigneeLocationPublicId = v.consigneeLocationPublicId
  if (v.notes !== (o.notes ?? '')) patch.notes = v.notes
  if (!o.isSpecialDelivery) {
    const before = (o.packages ?? []).map(lineKey)
    const after = v.packages.map((p) => lineKey({ packageType: p.packageType, description: p.description, pieces: p.pieces }))
    if (before.length !== after.length || before.some((k, i) => k !== after[i]))
      patch.packages = v.packages.map((p) => ({ packageType: p.packageType || null, description: p.description.trim() || null, pieces: p.pieces ?? 1 }))
    const oldCod = o.codAmount != null && o.codAmount > 0 ? o.codAmount : 0
    const newCod = v.codAmount != null && v.codAmount > 0 ? v.codAmount : 0
    if (newCod !== oldCod) {
      if (newCod > 0) patch.codAmount = newCod
      else patch.clearCod = true
    }
  }
  return patch
}

export function hasOrderChanges(patch: OrderPatchRequest): boolean {
  return Object.keys(patch).some((k) => k !== 'rowVersion' && k !== 'confirmDuplicateInvoice')
}

/** Cuerpo del `PATCH /orders/{id}`: lo que cambió más el `rowVersion` de la ficha (409 si otro usuario la modificó). */
export function buildOrderPatch(o: OrderDetailDto, v: OrderEditValues, opts: { confirmDuplicateInvoice?: boolean } = {}): OrderPatchRequest {
  const patch = orderPatchChanges(o, v)
  patch.rowVersion = o.rowVersion ?? null
  if (opts.confirmDuplicateInvoice) patch.confirmDuplicateInvoice = true
  return patch
}

/** 409 de edición concurrente (`El registro fue modificado por otro usuario…`): distinto de la factura repetida, que también es 409. */
export function isConcurrencyConflict(err: unknown): err is ApiError {
  return err instanceof ApiError && err.status === 409 && err.code === 'conflict'
}
