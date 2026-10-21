// Importación de PRODUCTOS con plantilla (2026-10-21; primera entidad del motor genérico): reglas puras de la definición de la plantilla en el editor y de lo que se muestra
// del resultado. Mismas reglas que el servidor (`ProductImportMapping.ValidateColumns`): sku en una columna; nombre en una columna o como valor por defecto; sin campos repetidos;
// sku y código de barras no admiten valor por defecto.
import type { ImportField, TemplateIssue, TemplateValues } from '../consignees/consigneeRules'

export const PRODUCT_KIND = 'PRODUCT'

export interface ProductTemplateMessages {
  columnsRequired: string
  sku: string
  name: string
  repeated: (field: string) => string
  noDefault: (field: string) => string
  defaultValueRequired: (field: string) => string
}

export function productTemplateIssues(v: Pick<TemplateValues, 'columns' | 'defaults'>, fields: readonly ImportField[], m: ProductTemplateMessages): TemplateIssue[] {
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
  if (seen.size === 0) issues.push({ path: 'columns', message: m.columnsRequired })
  else if (!seen.has('sku')) issues.push({ path: 'columns', message: m.sku })
  else if (!seen.has('name') && !defaultFields.has('name')) issues.push({ path: 'columns', message: m.name })
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
