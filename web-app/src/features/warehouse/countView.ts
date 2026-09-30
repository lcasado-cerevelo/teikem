// Lote 14 (P8) — lógica pura de "Conteo cíclico" en dos paneles (maqueta `conteo()`, Cambios.pdf pp. 14-15, decisiones D2-D4 y
// D7-D10): filtros de la lista y su consulta a `GET /cycle-counts/page`, el conteo elegido en la URL (`?count=<id>`, también
// la redirección de la ficha vieja `/warehouse/cycle-counts/:id`), cómo se describe un conteo en la lista, qué líneas coinciden
// con lo escaneado (SKU, código de barras, lote o serie), la cantidad tecleada en la fila, lo que impide "Confirmar conteo y
// ajustar" y la ventana de "lo cambiado" en hora de la compañía (Puerto Rico).
import { normalizeQ } from '../../kernel/ui/matchesQ'
import type { DateRange } from '../../kernel/ui/dateRange'
import type { ComboOption } from '../../kernel/ui/comboMatch'
import type { CycleCountDto, CycleCountLineDto, GetQuery } from './api'
import { listParam, type BinFilterItem } from './kardexView'
import type { ProductFilterItem } from './pickers'
import { parseQtyText } from './receiptLineEdit'

export const COUNT_STATUS_DOMAIN = 'CycleCountStatus'
export const COUNT_ORIGIN_DOMAIN = 'CycleCountOrigin'
export const COUNT_ENTITY_TYPE = 'CYCLE_COUNT'

// La zona horaria de la compañía ("hoy" y la ventana de "lo cambiado") vive en `src/kernel/api/tenantZone.ts` (Lote 15:
// un solo punto para toda la web): `TENANT_TIME_ZONE`, `zonedInputFromUtc`, `utcFromZonedInput`.

/** Estatus finales del conteo (D7): Concordancia (sin ajustes) y Diferencia (asentó algún ajuste). */
export const CLOSED_COUNT_STATUSES: readonly string[] = ['RECONCILED', 'RECONCILED_VARIANCE']

export function isCountClosed(statusCode: string | null | undefined): boolean {
  return CLOSED_COUNT_STATUSES.includes(statusCode ?? '')
}

/** Pendiente o Contado (este solo llega de la app, a ciegas): se captura y se confirma. */
export function isCountEditable(statusCode: string | null | undefined): boolean {
  return Boolean(statusCode) && !isCountClosed(statusCode)
}

// ---------------------------------------------------------------------------------------------------------------------
// Filtros de la lista (todos van al API)
// ---------------------------------------------------------------------------------------------------------------------

export interface CountFilterState {
  warehousePublicIds: string[]
  /** ids de zona como texto (de los almacenes elegidos). */
  zoneIds: string[]
  bins: BinFilterItem[]
  products: ProductFilterItem[]
  /** OPEN, COUNTED, RECONCILED, RECONCILED_VARIANCE. */
  status: string[]
  /** MANUAL (Selección) o CHANGES (Lo cambiado). */
  origins: string[]
  /** Fecha de alta en días locales de la compañía ('YYYY-MM-DD'). */
  created: DateRange
  /** Número del conteo, SKU o nombre de producto (con pausa). */
  search: string
}

export const EMPTY_COUNT_FILTERS: CountFilterState = {
  warehousePublicIds: [],
  zoneIds: [],
  bins: [],
  products: [],
  status: [],
  origins: [],
  created: { from: '', to: '' },
  search: '',
}

const some = <T>(xs: readonly T[]): T[] | undefined => (xs.length > 0 ? [...xs] : undefined)

/** Consulta de la lista SIN página (la usa también Exportar). */
export function countFilterQuery(f: CountFilterState): GetQuery<'/api/v1/cycle-counts/page'> {
  return {
    warehousePublicIds: some(f.warehousePublicIds),
    zoneIds: some(f.zoneIds.map(Number).filter((n) => Number.isInteger(n) && n > 0)),
    binIds: some(f.bins.map((b) => b.id)),
    productPublicIds: some(f.products.map((p) => p.publicId)),
    status: some(f.status),
    origins: some(f.origins),
    from: f.created.from || undefined,
    to: f.created.to || undefined,
    search: f.search.trim() || undefined,
  }
}

const DAY = /^\d{4}-\d{2}-\d{2}$/
/** Códigos de catálogo de la URL en mayúsculas y sin duplicados. */
const upperList = (params: URLSearchParams, name: string) => [...new Set(listParam(params, name).map((v) => v.toUpperCase()))]

/**
 * Lote 15 — filtros iniciales de la URL (se leen UNA vez al montar; contrato de enlaces: la franja "Almacén hoy" del Pulso
 * manda `?status=RECONCILED_VARIANCE&warehousePublicIds=`): `warehousePublicIds`, `status`, `origins` (repetibles o separados
 * por comas) y `from`/`to` (alta, 'YYYY-MM-DD'). Lo demás, vacío. `?count=` sigue siendo el conteo elegido.
 */
export function countFiltersFromUrl(params: URLSearchParams): CountFilterState {
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  return {
    ...EMPTY_COUNT_FILTERS,
    warehousePublicIds: listParam(params, 'warehousePublicIds'),
    status: upperList(params, 'status'),
    origins: upperList(params, 'origins'),
    created: { from: DAY.test(from) ? from : '', to: DAY.test(to) ? to : '' },
  }
}

export function countListQuery(f: CountFilterState, page: number, pageSize: number): GetQuery<'/api/v1/cycle-counts/page'> {
  return { ...countFilterQuery(f), skip: (Math.max(1, page) - 1) * pageSize, take: pageSize }
}

/** Una zona que ya no pertenece a los almacenes elegidos se descarta del filtro. */
export function keepZones(zoneIds: readonly string[], available: readonly string[]): string[] {
  const set = new Set(available)
  return zoneIds.filter((z) => set.has(z))
}

// ---------------------------------------------------------------------------------------------------------------------
// Conteo elegido en la URL (la ficha vieja `/warehouse/cycle-counts/:id` redirige aquí: `legacyCountSearch` de routes.tsx)
// ---------------------------------------------------------------------------------------------------------------------

/** `?count=<id>` → id entero positivo o null. */
export function countParam(params: URLSearchParams): number | null {
  const raw = params.get('count')
  if (!raw || !/^\d+$/.test(raw.trim())) return null
  const n = Number(raw)
  return Number.isSafeInteger(n) && n > 0 ? n : null
}

/** El elegido: el de la URL (aunque la página no lo traiga, p. ej. desde el Kárdex) o, sin él, el primero de la lista. */
export function selectedCountId(items: readonly Pick<CycleCountDto, 'id'>[], param: number | null): number | null {
  if (param != null) return param
  return items[0]?.id ?? null
}

// ---------------------------------------------------------------------------------------------------------------------
// Cómo se describe un conteo en la lista (maqueta: posición grande + chip; "CC-… · Zona A · 3 productos")
// ---------------------------------------------------------------------------------------------------------------------

export type CountWhere = { kind: 'bin'; code: string; zone: string | null } | { kind: 'many'; bins: number } | { kind: 'none' }

export function countWhere(c: Pick<CycleCountDto, 'binCode' | 'zoneCode' | 'binCount'>): CountWhere {
  if (c.binCode) return { kind: 'bin', code: c.binCode, zone: c.zoneCode ?? null }
  const n = c.binCount ?? 0
  return n > 0 ? { kind: 'many', bins: n } : { kind: 'none' }
}

/** Tono del origen: "Lo cambiado" se marca (etiqueta de flujo); "Selección" va neutra. */
export function isChangesOrigin(c: Pick<CycleCountDto, 'originCode'>): boolean {
  return (c.originCode ?? '').toUpperCase() === 'CHANGES'
}

// ---------------------------------------------------------------------------------------------------------------------
// Escáner: qué líneas coinciden con un código (D9: lleva a la línea y pide la cantidad)
// ---------------------------------------------------------------------------------------------------------------------

export type CountMatchBy = 'sku' | 'barcode' | 'lot' | 'serial'

export interface CountLineMatch {
  line: CycleCountLineDto
  by: CountMatchBy
  /** Serie escaneada (solo `by: 'serial'`), para agregarla a lo contado. */
  serial?: string
}

const fold = (s: string | null | undefined) => normalizeQ((s ?? '').trim())

/**
 * Líneas cuyo SKU, código de barras, lote o serie (esperada o ya contada) es EXACTAMENTE el código (sin mayúsculas ni
 * acentos). Una línea aparece una sola vez (primer criterio que coincide, en ese orden). Vacío = ninguna.
 */
export function matchCountLine(lines: readonly CycleCountLineDto[], code: string): CountLineMatch[] {
  const q = fold(code)
  if (!q) return []
  const out: CountLineMatch[] = []
  for (const line of lines) {
    if (fold(line.sku) === q) out.push({ line, by: 'sku' })
    else if (line.barcode && fold(line.barcode) === q) out.push({ line, by: 'barcode' })
    else if (line.lotNumber && fold(line.lotNumber) === q) out.push({ line, by: 'lot' })
    else {
      const serial = [...(line.expectedSerials ?? []), ...(line.countedSerials ?? [])].find((s) => fold(s) === q)
      if (serial) out.push({ line, by: 'serial', serial })
    }
  }
  return out
}

/** Opciones del buscador de líneas: "SKU · Producto", con posición, lote y código de barras en la pista (también se buscan). */
export function scanOptions(lines: readonly CycleCountLineDto[]): ComboOption[] {
  return lines.map((l) => ({
    value: String(l.id ?? 0),
    label: [l.sku, l.productName].filter(Boolean).join(' · '),
    hint: [l.binCode, l.lotNumber, l.barcode].filter(Boolean).join(' · ') || undefined,
  }))
}

// ---------------------------------------------------------------------------------------------------------------------
// Cantidad contada en la fila
// ---------------------------------------------------------------------------------------------------------------------

/** Texto de la cantidad guardada ('' = sin capturar). */
export function countedText(n: number | null | undefined): string {
  return n == null ? '' : String(n)
}

/** Lo tecleado: número (coma o punto), null si está vacío, NaN si no es un número. */
export function parseCounted(text: string): number | null {
  return parseQtyText(text)
}

/** Diferencia de una línea contra lo esperado (la foto): con lo tecleado si es un número; si no, la del servidor. */
export function lineVariance(line: Pick<CycleCountLineDto, 'systemQty' | 'countedQty' | 'varianceQty'>, draft?: string): number | null {
  if (draft !== undefined) {
    const n = parseCounted(draft)
    if (n === null || Number.isNaN(n) || line.systemQty == null) return null
    return Math.round((n - line.systemQty) * 1000) / 1000
  }
  if (line.varianceQty != null) return line.varianceQty
  if (line.countedQty == null || line.systemQty == null) return null
  return Math.round((line.countedQty - line.systemQty) * 1000) / 1000
}

/** Líneas sin contar, tomando lo tecleado (un número válido cuenta como contado). */
export function pendingLines(lines: readonly Pick<CycleCountLineDto, 'id' | 'countedQty'>[], drafts: ReadonlyMap<number, string>): number {
  let n = 0
  for (const l of lines) {
    const draft = drafts.get(l.id ?? 0)
    if (draft !== undefined) {
      const v = parseCounted(draft)
      if (v === null || Number.isNaN(v)) n++
    } else if (l.countedQty == null) n++
  }
  return n
}

export type ConfirmBlocker = { key: 'noLines' } | { key: 'pending'; params: { n: number } } | { key: 'closed' } | { key: 'blind' }

/**
 * Qué impide "Confirmar conteo y ajustar" (D8, un paso desde Pendiente o Contado): conteo cerrado, a ciegas (la web no
 * confirma sin ver lo esperado), sin líneas o con líneas sin contar ('Faltan {n} línea(s) por contar.', el mismo 422 del API).
 */
export function confirmBlocker(args: {
  statusCode: string | null | undefined
  isBlind: boolean
  lines: readonly Pick<CycleCountLineDto, 'id' | 'countedQty'>[]
  drafts: ReadonlyMap<number, string>
}): ConfirmBlocker | null {
  if (!isCountEditable(args.statusCode)) return { key: 'closed' }
  if (args.isBlind) return { key: 'blind' }
  if (args.lines.length === 0) return { key: 'noLines' }
  const n = pendingLines(args.lines, args.drafts)
  return n > 0 ? { key: 'pending', params: { n } } : null
}
