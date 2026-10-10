// Informe "Productos por posición" en Ubicaciones (servidor: `GET .../bin-products`, manual 06). Lógica pura de la
// pantalla y del flujo de impresión, sin React (se prueba con dependencias falsas):
// - Qué imprimir (`BinProductsScope`): las posiciones del filtro actual de la tabla o las marcadas con sus casillas.
// - Flujo (`printBinProducts`): lee del servidor por tandas de ≤ 200 posiciones (cada tanda con su `generatedAtUtc`), SIN tope de
//   posiciones (2026-10-10, Luis: no se restringe, solo se avisa si es grande); ordena por código (orden natural), arma el PDF y lo
//   descarga. No guarda ningún estado: es un listado de lo que hay en cada posición al momento de generarlo. Cancelar durante la
//   lectura, o un error al leer o al generar, no descarga nada.
// - Formato (2026-10-10, Luis): IGUAL al de "Códigos de barras" (`kernel/ui/barcodeReportPdf`): hoja carta con encabezado y pie de marca,
//   rejilla de 3 columnas con varios códigos por página. Cada posición abre un grupo con su título (código, zona, pasillo, rack…) y
//   debajo, en celdas, el código de barras de cada producto que tiene (SKU, nombre y el código de barras del producto si lo tiene).
import { parseApiDate } from '../../kernel/api/dates'
import type { BarcodeReportGroup, BarcodeReportRow, BarcodeReportSpec } from '../../kernel/ui/barcodeReportPdf'
import { code128Unsupported } from '../../kernel/ui/code128'
import { chunk, type BinSheetDetail } from '../../kernel/ui/binSheetPdf'
import type { ReportFilter } from '../../kernel/ui/reportPdf'
import type { BinProductsDto, BinProductsPageDto, BinProductsQuery } from './api'
import { naturalCompare } from './barcodeReports'
import type { BinListQuery } from './locations'

type Translate = (key: string, params?: Record<string, string | number>) => string

const S = 'warehouse.binProducts'

/** Permiso del informe (leer: el de la pantalla y el del servidor). */
export const BIN_PRODUCTS_PERMISSION = 'inventory.view'

/** Posiciones por lectura (el máximo del servidor) y tope por impresión. */
export const BIN_PRODUCTS_PAGE_SIZE = 200
/** Desde cuántas posiciones se AVISA que el PDF es grande (no se limita nada: se imprime todo). */
export const BIN_PRODUCTS_NOTICE = 500

/** Qué imprimir. */
export type BinProductsScope = 'filter' | 'selected'

/**
 * Consultas de `GET .../bin-products` para un alcance (sin `skip`/`take`): el filtro de la tabla tal cual, o las marcadas
 * en tandas de 200 ids (`binIds`, sin repetir y en orden: la dirección no crece de más). `query` null = filtros imposibles
 * (nada que leer).
 */
export function binProductsSources(scope: BinProductsScope, query: BinListQuery | null, selectedIds: readonly number[]): BinProductsQuery[] {
  if (scope === 'selected') {
    const ids = [...new Set(selectedIds)].sort((a, b) => a - b)
    return chunk(ids, BIN_PRODUCTS_PAGE_SIZE).map((binIds) => ({ binIds }))
  }
  if (!query) return []
  return [{ ...query }]
}

/** Detalle de una posición: zona, pasillo, rack, nivel y posición (los que existen) e "Inactiva" (lo usan también las etiquetas). */
export function binDetails(s: Pick<BinProductsDto, 'zoneCode' | 'aisle' | 'rack' | 'level' | 'position' | 'isActive'>, t: Translate): BinSheetDetail[] {
  const parts: [string, string | null | undefined][] = [
    ['zone', s.zoneCode],
    ['aisle', s.aisle],
    ['rack', s.rack],
    ['level', s.level],
    ['position', s.position],
  ]
  const out = parts.filter(([, v]) => v != null && v.trim() !== '').map(([k, v]) => ({ label: t(`${S}.details.${k}`), value: (v ?? '').trim() }))
  if (s.isActive === false) out.push({ label: t(`${S}.details.inactive`), value: '' })
  return out
}

/** El mismo detalle en una línea de texto ("Zona A · Pasillo 01 · Inactiva"), para el título del grupo del reporte. */
export function binDetailsText(s: Parameters<typeof binDetails>[0], t: Translate): string {
  return binDetails(s, t).map((d) => (d.value ? `${d.label} ${d.value}` : d.label)).join(' · ')
}

/** Una fila del reporte: el código de barras del producto (el suyo si Code 128 lo admite; si no, el SKU), SKU en negrita y nombre debajo. */
export function productReportRow(p: NonNullable<BinProductsDto['products']>[number]): BarcodeReportRow {
  const sku = (p.sku ?? '').trim()
  const own = (p.barcode ?? '').trim()
  const useOwn = own !== '' && code128Unsupported(own).length === 0
  const value = useOwn ? own : sku
  return { value, title: sku || value, description: p.name ?? '', meta: useOwn && own !== sku ? own : null }
}

/** El instante más antiguo (texto del API tal cual); null si no hay ninguno válido. */
export function earliestUtc(values: readonly (string | null | undefined)[]): string | null {
  let best: { raw: string; ms: number } | null = null
  for (const raw of values) {
    if (!raw) continue
    const ms = parseApiDate(raw).getTime()
    if (Number.isNaN(ms)) continue
    if (!best || ms < best.ms) best = { raw, ms }
  }
  return best?.raw ?? null
}

export type BinProductsProgress = { phase: 'read'; done: number; total: number } | { phase: 'render'; products: number }

export interface BinProductsFlowDeps {
  /** Una tanda (`skip`/`take` ya puestos). */
  fetchPage: (query: BinProductsQuery) => Promise<BinProductsPageDto>
  /** Arma y descarga el PDF; si lanza, el error se propaga. */
  download: (spec: BarcodeReportSpec) => Promise<unknown>
  /** Cancelar (solo se atiende mientras se lee). */
  signal?: AbortSignal
  onProgress?: (p: BinProductsProgress) => void
  /** Hora de respaldo si el servidor no mandó `generatedAtUtc` (pruebas). */
  now?: () => Date
}

export interface BinProductsFlowArgs {
  sources: readonly BinProductsQuery[]
  includeEmpty: boolean
  /** Encabezado del PDF (título, compañía, usuario, filtros aplicados e idioma). */
  spec: Pick<BarcodeReportSpec, 'title' | 'subtitle' | 'company' | 'user' | 'locale'> & { filters?: readonly ReportFilter[] }
  t: Translate
  lang: string
}

export type BinProductsResult =
  | { status: 'nothing'; bins: number; omittedEmpty: number }
  | { status: 'cancelled' }
  | { status: 'printed'; bins: number; products: number; omittedEmpty: number }

/** Cuántos códigos de posición vacía se nombran en el aviso; el resto va como "y N más". */
const EMPTY_BINS_NAMED = 60

/** Especificación del reporte (puro): un grupo por posición con productos, en el orden dado; las vacías, solo como aviso. */
export function buildBinProductsReport(
  bins: readonly BinProductsDto[],
  args: Pick<BinProductsFlowArgs, 'spec' | 't' | 'includeEmpty'>,
  generatedAt?: Date,
): { spec: BarcodeReportSpec; products: number; withProducts: number; empty: string[] } {
  const { t } = args
  const groups: BarcodeReportGroup[] = []
  const empty: string[] = []
  let products = 0
  for (const b of bins) {
    const list = b.products ?? []
    if (list.length === 0) {
      empty.push(b.code ?? '')
      continue
    }
    products += list.length
    const details = binDetailsText(b, t)
    groups.push({
      title: t(`${S}.group`, { bin: b.code ?? '', details: details ? ` · ${details}` : '', count: list.length }),
      rows: list.map(productReportRow),
    })
  }
  const notices: string[] = []
  if (empty.length > 0) {
    const named = empty.slice(0, EMPTY_BINS_NAMED).join(', ')
    const more = empty.length > EMPTY_BINS_NAMED ? ` ${t(`${S}.emptyMore`, { count: empty.length - EMPTY_BINS_NAMED })}` : ''
    notices.push(t(args.includeEmpty ? `${S}.emptyListed` : `${S}.emptyOmitted`, { count: empty.length, bins: `${named}${more}` }))
  }
  return {
    spec: {
      ...args.spec,
      filters: args.spec.filters ?? [],
      generatedAt,
      groups,
      columns: 'auto',
      notices,
      emptyText: t(`${S}.nothingInReport`),
    },
    products,
    withProducts: groups.length,
    empty,
  }
}

/** Lee, arma el PDF y lo descarga (ver el encabezado del archivo). Un error al leer o al generar se propaga. */
export async function printBinProducts(deps: BinProductsFlowDeps, args: BinProductsFlowArgs): Promise<BinProductsResult> {
  const aborted = () => deps.signal?.aborted === true
  const read = new Map<number, { bin: BinProductsDto; generatedAtUtc: string | null }>()
  let total = 0
  for (const source of args.sources) {
    let skip = 0
    let sourceTotal: number | null = null
    while (sourceTotal === null || skip < sourceTotal) {
      if (aborted()) return { status: 'cancelled' }
      let page: BinProductsPageDto
      try {
        page = await deps.fetchPage({ ...source, skip, take: BIN_PRODUCTS_PAGE_SIZE })
      } catch (err) {
        if (aborted()) return { status: 'cancelled' }
        throw err
      }
      if (sourceTotal === null) {
        sourceTotal = page.total ?? 0
        total += sourceTotal
      }
      const items = page.items ?? []
      for (const bin of items) {
        if (bin.binId != null && !read.has(bin.binId)) read.set(bin.binId, { bin, generatedAtUtc: page.generatedAtUtc ?? null })
      }
      deps.onProgress?.({ phase: 'read', done: read.size, total })
      if (items.length === 0) break
      skip += items.length
    }
  }
  if (aborted()) return { status: 'cancelled' }

  const cmp = naturalCompare(args.lang)
  const entries = [...read.values()].sort((a, b) => cmp(a.bin.code ?? '', b.bin.code ?? ''))
  const withProducts = entries.filter((e) => (e.bin.products ?? []).length > 0)
  // sin productos en nada de lo elegido: no hay nada que imprimir (las vacías solas no hacen un reporte)
  if (withProducts.length === 0) return { status: 'nothing', bins: entries.length, omittedEmpty: entries.length }

  const generatedAtUtc = earliestUtc(withProducts.map((e) => e.generatedAtUtc))
  const generatedAt = generatedAtUtc ? parseApiDate(generatedAtUtc) : (deps.now?.() ?? new Date())
  const built = buildBinProductsReport(entries.map((e) => e.bin), args, generatedAt)
  deps.onProgress?.({ phase: 'render', products: built.products })
  await deps.download(built.spec)
  return { status: 'printed', bins: built.withProducts, products: built.products, omittedEmpty: built.empty.length }
}

/** Texto del aviso final de una impresión correcta (posiciones, productos y vacías aparte). */
export function printedSummary(r: Extract<BinProductsResult, { status: 'printed' }>, t: Translate, fmt: (n: number) => string = String): string {
  const parts = [t(`${S}.result.printed`, { products: fmt(r.products), bins: fmt(r.bins) })]
  if (r.omittedEmpty > 0) parts.push(t(`${S}.result.omittedEmpty`, { count: fmt(r.omittedEmpty) }))
  return parts.join(' ')
}
