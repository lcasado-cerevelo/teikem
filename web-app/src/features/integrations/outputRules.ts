// Reglas puras de las integraciones de SALIDA (Lote 137b): valores del formulario, validación en cliente (los mensajes más comunes; el servidor da el resto con su texto exacto) y el cuerpo del API.
// Una salida manda las órdenes del cliente que llegan a los estatus elegidos, al instante (un envío por orden) o por horario (un envío con todas las pendientes), por SFTP/FTP (archivo) o por la API saliente (HTTP).
import type { components } from '../../kernel/api/schema'

type OutputConfig = components['schemas']['OutputConfig']
type HttpOutSecret = components['schemas']['HttpOutSecret']

export const OUTPUT_FORMATS = ['CSV', 'JSON', 'XML'] as const
export const OUTPUT_TRIGGERS = ['EVENT', 'SCHEDULE'] as const
export const HTTP_METHODS = ['POST', 'PUT'] as const
export const HTTP_AUTH_KINDS = ['NONE', 'BEARER', 'BASIC', 'HEADER'] as const
export const OUTBOUND_CONNECTORS = ['SFTP', 'FTP', 'HTTP'] as const
export const MAX_COLUMNS = 40
export const MAX_HEADERS = 10
export const NAME_PATTERN = /^[A-Za-z_][A-Za-z0-9_-]*$/
export const DEFAULT_COLUMNS = ['orderNumber', 'status', 'deliveredAt', 'recipientName'] as const
/** Alcance de la salida de un catálogo (productos): ver inventario. */
export const CATALOG_EXPORT_SCOPE = 'files.catalog_export'
export const EXPORT_SCOPE = 'files.orders_export'

export interface OutputColumnValues {
  header: string
  field: string
}

export interface HttpHeaderValues {
  name: string
  value: string
}

export interface HttpOutValues {
  url: string
  method: string
  authKind: string
  authName: string
  /** Vacío al editar = se conserva el guardado. */
  secret: string
  headers: HttpHeaderValues[]
}

export interface OutputValues {
  statuses: string[]
  trigger: string
  format: string
  delimiter: string
  fileName: string
  columns: OutputColumnValues[]
  http: HttpOutValues
}

export interface OutputMessages {
  statuses: string
  columnsRequired: string
  columnsMax: string
  headerRequired: string
  headerMax: string
  headerRepeated: (h: string) => string
  headerName: (h: string) => string
  fieldRequired: string
  url: string
  authName: string
  secret: string
  headersMax: string
  headerPair: string
}

export const emptyHttp = (): HttpOutValues => ({ url: '', method: 'POST', authKind: 'NONE', authName: '', secret: '', headers: [] })

export const emptyOutput = (): OutputValues => ({
  statuses: ['DELIVERED'],
  trigger: 'EVENT',
  format: 'CSV',
  delimiter: ',',
  fileName: '',
  columns: DEFAULT_COLUMNS.map((f) => ({ header: f, field: f })),
  http: emptyHttp(),
})

export function outputValuesOf(c: OutputConfig | null | undefined): OutputValues {
  if (!c) return emptyOutput()
  const h = c.http
  return {
    statuses: [...(c.statuses ?? [])],
    trigger: c.trigger ?? 'EVENT',
    format: c.format ?? 'CSV',
    delimiter: c.delimiter ?? ',',
    fileName: c.fileName ?? '',
    columns: (c.columns ?? []).map((x) => ({ header: x.header ?? '', field: x.field ?? '' })),
    http: h
      ? { url: h.url ?? '', method: h.method ?? 'POST', authKind: h.authKind ?? 'NONE', authName: h.authName ?? '', secret: '', headers: (h.headers ?? []).map((x) => ({ name: x.name ?? '', value: x.value ?? '' })) }
      : emptyHttp(),
  }
}

/** El cuerpo del API. La API saliente (HTTP) va dentro de la salida; los demás conectores no la llevan. */
export function buildOutputConfig(v: OutputValues, connector: string): OutputConfig {
  return {
    statuses: v.statuses,
    trigger: v.trigger,
    format: v.format,
    delimiter: v.format === 'CSV' ? v.delimiter || ',' : null,
    fileName: connector === 'HTTP' || !v.fileName.trim() ? null : v.fileName.trim(),
    columns: v.columns.map((c) => ({ header: c.header.trim(), field: c.field })),
    http:
      connector === 'HTTP'
        ? {
            url: v.http.url.trim(),
            method: v.http.method,
            authKind: v.http.authKind,
            authName: v.http.authKind === 'BASIC' || v.http.authKind === 'HEADER' ? v.http.authName.trim() : null,
            headers: v.http.headers.filter((h) => h.name.trim() || h.value).map((h) => ({ name: h.name.trim(), value: h.value })),
          }
        : null,
  }
}

/** El secreto de la API saliente: solo si se escribió uno (si no, el servidor conserva el guardado). */
export const buildHttpSecret = (v: OutputValues, connector: string): HttpOutSecret | undefined => (connector === 'HTTP' && v.http.authKind !== 'NONE' && v.http.secret ? { value: v.http.secret } : undefined)

/** Problemas de la salida (ruta del formulario → mensaje): los obligatorios y los más comunes; el servidor agrega el resto con su texto exacto. */
export function outputIssues(v: OutputValues, connector: string, hasSavedSecret: boolean, m: OutputMessages, entity = false): { path: string; message: string }[] {
  const issues: { path: string; message: string }[] = []
  // la salida de un catálogo (entity) no tiene estatus ni columnas propias: las columnas son las de la plantilla de la entidad
  if (!entity && v.statuses.length === 0) issues.push({ path: 'statuses', message: m.statuses })
  if (entity) { /* sin columnas propias */ }
  else if (v.columns.length === 0) issues.push({ path: 'columns', message: m.columnsRequired })
  else if (v.columns.length > MAX_COLUMNS) issues.push({ path: 'columns', message: m.columnsMax })
  else {
    const seen = new Set<string>()
    for (const [i, c] of v.columns.entries()) {
      const header = c.header.trim()
      if (!header) { issues.push({ path: `columns.${i}.header`, message: m.headerRequired }); break }
      if (header.length > 60) { issues.push({ path: `columns.${i}.header`, message: m.headerMax }); break }
      if (seen.has(header.toLowerCase())) { issues.push({ path: `columns.${i}.header`, message: m.headerRepeated(header) }); break }
      seen.add(header.toLowerCase())
      if (v.format !== 'CSV' && !NAME_PATTERN.test(header)) { issues.push({ path: `columns.${i}.header`, message: m.headerName(header) }); break }
      if (!c.field) { issues.push({ path: `columns.${i}.field`, message: m.fieldRequired }); break }
    }
  }
  if (connector === 'HTTP') {
    const h = v.http
    if (!h.url.trim()) issues.push({ path: 'http.url', message: m.url })
    if ((h.authKind === 'BASIC' || h.authKind === 'HEADER') && !h.authName.trim()) issues.push({ path: 'http.authName', message: m.authName })
    if (h.authKind !== 'NONE' && !h.secret && !hasSavedSecret) issues.push({ path: 'http.secret', message: m.secret })
    if (h.headers.length > MAX_HEADERS) issues.push({ path: 'http.headers', message: m.headersMax })
    for (const [i, x] of h.headers.entries()) if (!x.name.trim() && x.value) { issues.push({ path: `http.headers.${i}.name`, message: m.headerPair }); break }
  }
  return issues
}

/** ¿Hay que pedir el intervalo? (La salida por horario manda con el horario de la integración.) */
export const outputNeedsInterval = (v: OutputValues) => v.trigger === 'SCHEDULE'
