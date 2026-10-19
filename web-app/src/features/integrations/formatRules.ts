// Reglas puras del formato de archivo de Integraciones (Lote 137): Excel (hoja), JSON y XML con mapeo por ruta. Valores del formulario, validación en cliente (mismos mensajes que el servidor) y el cuerpo del API.
// Una ruta es «$.a.b[0].c»: $ = documento, @ = el registro actual (la línea si hay ruta de líneas, si no la orden), ^ = la orden (solo con líneas); [*] = todos, [n] = uno; en XML, @atributo y #text.
import type { components } from '../../kernel/api/schema'

type FileFormatConfig = components['schemas']['FileFormatConfig']

export const FORMAT_KINDS = ['AUTO', 'CSV', 'EXCEL', 'JSON', 'XML'] as const
export const SHEET_MAX = 31
export const MAX_COLUMNS = 60
export const HEADER_MAX = 80
export const PATH_MAX = 200

export interface FormatColumnValues {
  header: string
  path: string
}

export interface FormatValues {
  kind: string
  sheet: string
  recordsPath: string
  linesPath: string
  columns: FormatColumnValues[]
}

export interface FormatMessages {
  sheet: string
  recordsRequired: string
  recordsRoot: string
  linesRoot: string
  columnsRequired: string
  columnsMax: string
  headerRequired: string
  headerMax: string
  headerRepeated: (h: string) => string
  pathInvalid: (p: string) => string
  orderRoot: (p: string) => string
}

export const emptyFormat = (): FormatValues => ({ kind: 'AUTO', sheet: '', recordsPath: '', linesPath: '', columns: [] })

/** ¿Es una ruta bien formada? Igual que el servidor: raíz $ @ ^, luego «.nombre», «[n]» o «[*]». */
export function isValidPath(path: string): boolean {
  const p = path.trim()
  if (!p || p.length > PATH_MAX || !'$@^'.includes(p[0])) return false
  return /^(?:\.[A-Za-z0-9_\-:@#]+|\[(?:\*|\d{1,6})\])*$/.test(p.slice(1))
}

export const isStructured = (kind: string) => kind === 'JSON' || kind === 'XML'

export function formatValuesOf(c: FileFormatConfig | null | undefined): FormatValues {
  if (!c) return emptyFormat()
  return {
    kind: c.kind ?? 'AUTO',
    sheet: c.sheet ?? '',
    recordsPath: c.recordsPath ?? '',
    linesPath: c.linesPath ?? '',
    columns: (c.columns ?? []).map((x) => ({ header: x.header ?? '', path: x.path ?? '' })),
  }
}

/** El cuerpo del API. En AUTO sin nada que decir va vacío (el servidor lo guarda como «sin formato»). */
export function buildFormatConfig(v: FormatValues): FileFormatConfig {
  const structured = isStructured(v.kind)
  return {
    kind: v.kind,
    sheet: v.kind === 'EXCEL' && v.sheet.trim() ? v.sheet.trim() : null,
    recordsPath: structured ? v.recordsPath.trim() : null,
    linesPath: structured && v.linesPath.trim() ? v.linesPath.trim() : null,
    columns: structured ? v.columns.map((c) => ({ header: c.header.trim(), path: c.path.trim() })) : [],
  }
}

/** Problemas del formato (campo → mensaje), con el mismo orden y texto que el servidor; el campo es la ruta del formulario («format.recordsPath», «format.columns.2.path»…). */
export function formatIssues(v: FormatValues, m: FormatMessages): { path: string; message: string }[] {
  const issues: { path: string; message: string }[] = []
  if (v.kind === 'EXCEL' && v.sheet.trim().length > SHEET_MAX) issues.push({ path: 'sheet', message: m.sheet })
  if (!isStructured(v.kind)) return issues
  const records = v.recordsPath.trim()
  if (!records) issues.push({ path: 'recordsPath', message: m.recordsRequired })
  else if (!isValidPath(records)) issues.push({ path: 'recordsPath', message: m.pathInvalid(records) })
  else if (records[0] !== '$') issues.push({ path: 'recordsPath', message: m.recordsRoot })
  const lines = v.linesPath.trim()
  if (lines) {
    if (!isValidPath(lines)) issues.push({ path: 'linesPath', message: m.pathInvalid(lines) })
    else if (lines[0] !== '@') issues.push({ path: 'linesPath', message: m.linesRoot })
  }
  if (v.columns.length === 0) issues.push({ path: 'columns', message: m.columnsRequired })
  else if (v.columns.length > MAX_COLUMNS) issues.push({ path: 'columns', message: m.columnsMax })
  else {
    const seen = new Set<string>()
    for (const [i, c] of v.columns.entries()) {
      const header = c.header.trim()
      const path = c.path.trim()
      if (!header) { issues.push({ path: `columns.${i}.header`, message: m.headerRequired }); break }
      if (header.length > HEADER_MAX) { issues.push({ path: `columns.${i}.header`, message: m.headerMax }); break }
      if (seen.has(header.toLowerCase())) { issues.push({ path: `columns.${i}.header`, message: m.headerRepeated(header) }); break }
      seen.add(header.toLowerCase())
      if (!isValidPath(path)) { issues.push({ path: `columns.${i}.path`, message: m.pathInvalid(path) }); break }
      if (path[0] === '^' && !lines) { issues.push({ path: `columns.${i}.path`, message: m.orderRoot(path) }); break }
    }
  }
  return issues
}

/** ¿El formato dice algo? (Para no mandar un «AUTO» vacío al crear.) */
export const formatIsPlain = (v: FormatValues) => v.kind === 'AUTO' && !v.sheet.trim() && !v.recordsPath.trim() && !v.linesPath.trim() && v.columns.length === 0
