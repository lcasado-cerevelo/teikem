// Importación de entidades con plantilla (motor genérico, 2026-10-21; productos y proveedores): reglas puras de la definición de la plantilla en el editor y de lo que se muestra del resultado.
// Mismas reglas que el servidor (`ImportEntityMeta.ValidateColumns`): columnas con campos conocidos y sin repetir; valores por defecto solo de los campos que los admiten; y las reglas de grupo de cada
// entidad (productos: sku en columna y nombre en columna o por defecto; proveedores: nombre en columna).
import type { ImportField, TemplateIssue, TemplateValues } from '../consignees/consigneeRules'

export type ImportEntity = 'PRODUCT' | 'SUPPLIER'
export const IMPORT_ENTITIES: readonly ImportEntity[] = ['PRODUCT', 'SUPPLIER']

/** Dónde vive cada entidad en el API (rutas de plantillas e importación), el permiso para cambiarlas y el que las ve. */
export const ENTITY_CONFIG: Record<ImportEntity, { base: string; manage: string; view: string; module: string }> = {
  PRODUCT: { base: '/api/v1/products', manage: 'inventory.manage', view: 'inventory.view', module: 'WMS_LOT_SERIAL' },
  SUPPLIER: { base: '/api/v1/suppliers', manage: 'purchasing.manage', view: 'purchasing.view', module: 'PURCHASING' },
}

/** Reglas de grupo de cada entidad: al menos uno de `fields` en una columna (o, con `allowDefault`, en los valores por defecto); si no, el error `messageKey`. */
export const ENTITY_REQUIREMENTS: Record<ImportEntity, readonly { fields: readonly string[]; allowDefault: boolean; messageKey: string }[]> = {
  PRODUCT: [
    { fields: ['sku'], allowDefault: false, messageKey: 'entityImport.errors.PRODUCT.sku' },
    { fields: ['name'], allowDefault: true, messageKey: 'entityImport.errors.PRODUCT.name' },
  ],
  SUPPLIER: [{ fields: ['name'], allowDefault: false, messageKey: 'entityImport.errors.SUPPLIER.name' }],
}

export interface EntityTemplateMessages {
  columnsRequired: string
  /** Mensaje de cada regla de grupo (por su `messageKey`, ya traducido). */
  requirement: (messageKey: string) => string
  repeated: (field: string) => string
  noDefault: (field: string) => string
  defaultValueRequired: (field: string) => string
}

export function entityTemplateIssues(entity: ImportEntity, v: Pick<TemplateValues, 'columns' | 'defaults'>, fields: readonly ImportField[], m: EntityTemplateMessages): TemplateIssue[] {
  const issues: TemplateIssue[] = []
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
  if (seen.size === 0) {
    issues.push({ path: 'columns', message: m.columnsRequired })
    return issues
  }
  for (const r of ENTITY_REQUIREMENTS[entity]) {
    if (!r.fields.some((f) => seen.has(f) || (r.allowDefault && defaultFields.has(f)))) {
      issues.push({ path: 'columns', message: m.requirement(r.messageKey) })
      break
    }
  }
  return issues
}

/** Cuántas filas de cada estado trae el resultado (para el resumen y el color). */
export function importCounts(rows: readonly { status?: string | null }[]): { ok: number; created: number; error: number } {
  let ok = 0
  let created = 0
  let error = 0
  for (const r of rows) {
    if (r.status === 'CREATED') created++
    else if (r.status === 'OK') ok++
    else error++
  }
  return { ok, created, error }
}

/** Los errores de una fila en un solo texto: «campo: mensaje · campo: mensaje». */
export function rowErrorText(errors: Record<string, string> | null | undefined, label: (field: string) => string): string {
  return Object.entries(errors ?? {})
    .map(([field, message]) => `${label(field)}: ${message}`)
    .join(' · ')
}
