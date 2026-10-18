// Reglas puras de la importación CSV de órdenes (Lote F-C3, manual 03 §7): catálogo y plantilla estándar, definición de plantillas de
// ÓRDENES (espejo de `ImportMapping.ValidateColumns`: consignatario por nombre o código, paquete por tipo o piezas, en columna o en
// valores por defecto), opciones de la confirmación y estado de cada fila. Sin React: se prueba en `orderImportRules.test.ts`.
import type { components } from '../../kernel/api/schema'
import type { ImportField, ImportTemplate, ImportTemplateCreateRequest, TemplateIssue, TemplateValues } from '../consignees/consigneeRules'

type Schemas = components['schemas']

export type OrderImportPreview = Schemas['ImportPreviewDto']
export type OrderImportRow = Schemas['ImportRowDto']
export type OrderImportResult = Schemas['ImportResultDto']
export type OrderImportConfirmRequest = Schemas['ImportConfirmRequest']

/** Tipo de plantilla que atiende `/import-templates` (el servidor responde 400 con cualquier otro). */
export const ORDER_KIND = 'ORDER'

export const CONSIGNEE_FIELDS = ['consigneeName', 'consigneeCode'] as const
export const PACKAGE_FIELDS = ['packageType', 'pieces'] as const

/** Columnas de la plantilla «Formato estándar»: lo mínimo que exige el servidor (consignatario + paquete) más servicio y COD. */
export const ORDER_STANDARD_COLUMNS = ['consigneeName', 'line1', 'city', 'serviceType', 'packageType', 'pieces', 'codAmount'] as const

export function orderStandardTemplateRequest(name: string): ImportTemplateCreateRequest {
  return {
    name,
    delimiter: ',',
    hasHeader: true,
    columns: ORDER_STANDARD_COLUMNS.map((field, i) => ({ position: i + 1, field })),
  }
}

/** Mensajes de la definición (ya traducidos; los del servidor, tal cual el manual 03 §7.1). */
export interface OrderTemplateMessages {
  columnsRequired: string
  consignee: string
  packages: string
  repeated: (field: string) => string
  noDefault: (field: string) => string
  defaultValueRequired: (field: string) => string
}

/**
 * Errores de la definición de una plantilla de órdenes: ruta → mensaje. Campo repetido, valor por defecto de un campo que no lo admite
 * o sin valor, sin columnas, sin consignatario (consigneeName o consigneeCode) y sin paquete (packageType o pieces).
 */
export function orderTemplateIssues(
  v: Pick<TemplateValues, 'columns' | 'defaults'>,
  fields: readonly ImportField[],
  m: OrderTemplateMessages,
): TemplateIssue[] {
  const issues: TemplateIssue[] = []
  const chosen = v.columns.map((c) => c.field).filter(Boolean)
  const seen = new Set<string>()
  v.columns.forEach((c, i) => {
    if (!c.field) return
    if (seen.has(c.field)) issues.push({ path: `columns.${i}.field`, message: m.repeated(c.field) })
    seen.add(c.field)
  })
  const allowDefault = new Set(fields.filter((f) => f.defaultAllowed).map((f) => f.field ?? ''))
  const defaultFields = new Set<string>()
  v.defaults.forEach((d, i) => {
    if (!d.field) return
    if (fields.length > 0 && !allowDefault.has(d.field)) issues.push({ path: `defaults.${i}.field`, message: m.noDefault(d.field) })
    else if (!d.value.trim()) issues.push({ path: `defaults.${i}.value`, message: m.defaultValueRequired(d.field) })
    else defaultFields.add(d.field)
  })
  const has = (group: readonly string[]) => group.some((f) => seen.has(f) || defaultFields.has(f))
  if (chosen.length === 0) issues.push({ path: 'columns', message: m.columnsRequired })
  else if (!has(CONSIGNEE_FIELDS)) issues.push({ path: 'columns', message: m.consignee })
  else if (!has(PACKAGE_FIELDS)) issues.push({ path: 'columns', message: m.packages })
  return issues
}

/**
 * Plantillas que sirven para un cliente: las generales y las suyas; sin cliente elegido, solo las generales (el servidor responde 404 si
 * se usa una plantilla de otro cliente).
 */
export function templatesForClient(list: readonly ImportTemplate[], clientPublicId: string): ImportTemplate[] {
  return list.filter((t) => !t.clientPublicId || t.clientPublicId === clientPublicId)
}

/** Opciones de la confirmación tal como las ve el usuario (casillas del modal). */
export interface ImportOptions {
  overrideCredit: boolean
  confirmDuplicateInvoice: boolean
}

export const DEFAULT_IMPORT_OPTIONS: ImportOptions = { overrideCredit: false, confirmDuplicateInvoice: false }

/**
 * Cuerpo de `POST /orders/import/{lote}/confirm`: solo lo que se marcó. Lote 106: ya no hay «confirmar al importar» (las órdenes nacen Sometidas y cotizadas);
 * `overrideCredit` (crédito excedido al crear) exige el permiso `orders.credit_override` (403 si no), así que sin él nunca se manda. Sin `rows` se crean todas las válidas.
 */
export function buildConfirmRequest(o: ImportOptions, canOverride: boolean): OrderImportConfirmRequest {
  const body: OrderImportConfirmRequest = {}
  if (o.overrideCredit && canOverride) body.overrideCredit = true
  if (o.confirmDuplicateInvoice) body.confirmDuplicateInvoice = true
  return body
}

/** Firma de las opciones: una clave de idempotencia sirve para reintentar la MISMA confirmación, no una con otras opciones. */
export function confirmSignature(batch: string, body: OrderImportConfirmRequest): string {
  return `${batch}|${body.overrideCredit ? 1 : 0}${body.confirmDuplicateInvoice ? 1 : 0}`
}

export type RowStatus = 'ok' | 'warn' | 'bad' | 'created' | 'failed' | 'skipped'

/** Estado de una fila: antes de confirmar (válida / con aviso / con errores) y después (creada / fallida / omitida). */
export function rowStatus(row: OrderImportRow, confirmed: boolean): RowStatus {
  if (confirmed) return row.skipped ? 'skipped' : row.orderPublicId ? 'created' : 'failed'
  return row.isValid ? ((row.warnings?.length ?? 0) > 0 ? 'warn' : 'ok') : 'bad'
}

/** Filas del resultado que no se crearon por un error (las que el usuario debe revisar). */
export function failedRows(rows: readonly OrderImportRow[] | null | undefined): OrderImportRow[] {
  return (rows ?? []).filter((r) => !r.orderPublicId && !r.skipped)
}
