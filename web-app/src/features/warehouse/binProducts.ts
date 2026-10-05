// Informe "Productos por posición" en Ubicaciones (servidor: `GET .../bin-products`, manual 06). Lógica pura de la
// pantalla y del flujo de impresión, sin React (se prueba con dependencias falsas):
// - Qué imprimir (`BinProductsScope`): las posiciones del filtro actual de la tabla o las marcadas con sus casillas.
// - Flujo (`printBinProducts`): lee del servidor por tandas de ≤ 200 posiciones (cada tanda con su `generatedAtUtc`), con
//   tope de `BIN_PRODUCTS_MAX_BINS` posiciones (si se pasa, avisa y pide acotar sin generar nada); ordena por código (orden
//   natural), arma el PDF (`kernel/ui/binSheetPdf`: UNA posición por página, con el código de barras de cada producto) y lo
//   descarga. No guarda ningún estado: es un listado de lo que hay en cada posición al momento de generarlo. Cancelar
//   durante la lectura, o un error al leer o al generar, no descarga nada.
import { parseApiDate } from '../../kernel/api/dates'
import { chunk, planBinSheets, type BinSheetBin, type BinSheetDetail, type BinSheetSpec } from '../../kernel/ui/binSheetPdf'
import type { BinProductsDto, BinProductsPageDto, BinProductsQuery } from './api'
import { naturalCompare } from './barcodeReports'
import type { BinListQuery } from './locations'

type Translate = (key: string, params?: Record<string, string | number>) => string

const S = 'warehouse.binProducts'

/** Permiso del informe (leer: el de la pantalla y el del servidor). */
export const BIN_PRODUCTS_PERMISSION = 'inventory.view'

/** Posiciones por lectura (el máximo del servidor) y tope por impresión. */
export const BIN_PRODUCTS_PAGE_SIZE = 200
export const BIN_PRODUCTS_MAX_BINS = 500

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

/** Detalle del encabezado: zona, pasillo, rack, nivel y posición (los que existen) y "Inactiva". */
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

/** Una posición del servidor → la posición del PDF (productos en el orden del servidor: por SKU). */
export function toSheetBin(s: BinProductsDto, t: Translate, printedAt?: Date): BinSheetBin {
  return {
    code: s.code ?? '',
    key: s.binId,
    details: binDetails(s, t),
    printedAt,
    products: (s.products ?? []).map((p) => ({ sku: p.sku ?? '', name: p.name ?? '', barcode: p.barcode ?? null })),
  }
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

export type BinProductsProgress = { phase: 'read'; done: number; total: number } | { phase: 'render'; sheets: number }

export interface BinProductsFlowDeps {
  /** Una tanda (`skip`/`take` ya puestos). */
  fetchPage: (query: BinProductsQuery) => Promise<BinProductsPageDto>
  /** Arma y descarga el PDF; si lanza, el error se propaga. */
  download: (spec: BinSheetSpec) => Promise<unknown>
  /** Cancelar (solo se atiende mientras se lee). */
  signal?: AbortSignal
  onProgress?: (p: BinProductsProgress) => void
  /** Hora de respaldo si el servidor no mandó `generatedAtUtc` (pruebas). */
  now?: () => Date
}

export interface BinProductsFlowArgs {
  sources: readonly BinProductsQuery[]
  includeEmpty: boolean
  /** Título, compañía, almacén e idioma del PDF. */
  spec: Pick<BinSheetSpec, 'title' | 'company' | 'warehouse' | 'locale'>
  t: Translate
  lang: string
  /** Tope de posiciones (por defecto `BIN_PRODUCTS_MAX_BINS`). */
  max?: number
}

export type BinProductsResult =
  | { status: 'tooMany'; total: number; max: number }
  | { status: 'nothing'; bins: number; omittedEmpty: number }
  | { status: 'cancelled' }
  | { status: 'printed'; sheets: number; bins: number; omittedEmpty: number; withoutCode: number }

/** Lee, arma el PDF y lo descarga (ver el encabezado del archivo). Un error al leer o al generar se propaga. */
export async function printBinProducts(deps: BinProductsFlowDeps, args: BinProductsFlowArgs): Promise<BinProductsResult> {
  const max = args.max ?? BIN_PRODUCTS_MAX_BINS
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
        // más del tope: no se genera nada (ni se sigue leyendo); la pantalla pide acotar
        if (total > max) return { status: 'tooMany', total, max }
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
  const bins = entries.map((e) => toSheetBin(e.bin, args.t, e.generatedAtUtc ? parseApiDate(e.generatedAtUtc) : undefined))
  const plan = planBinSheets(bins, { includeEmpty: args.includeEmpty })
  if (plan.pages.length === 0) return { status: 'nothing', bins: bins.length, omittedEmpty: plan.omittedEmpty.length }

  const generatedAtUtc = earliestUtc(plan.printedBins.map((i) => entries[i].generatedAtUtc))
  const printedAt = generatedAtUtc ? parseApiDate(generatedAtUtc) : (deps.now?.() ?? new Date())
  deps.onProgress?.({ phase: 'render', sheets: plan.pages.length })
  await deps.download({ ...args.spec, printedAt, bins, includeEmpty: args.includeEmpty })
  return { status: 'printed', sheets: plan.pages.length, bins: plan.printedBins.length, omittedEmpty: plan.omittedEmpty.length, withoutCode: plan.withoutCode }
}

/** Texto del aviso final de una impresión correcta (páginas, posiciones, vacías omitidas, productos sin código). */
export function printedSummary(r: Extract<BinProductsResult, { status: 'printed' }>, t: Translate, fmt: (n: number) => string = String): string {
  const parts = [t(`${S}.result.printed`, { sheets: fmt(r.sheets), bins: fmt(r.bins) })]
  if (r.omittedEmpty > 0) parts.push(t(`${S}.result.omittedEmpty`, { count: fmt(r.omittedEmpty) }))
  if (r.withoutCode > 0) parts.push(t(`${S}.result.withoutCode`, { count: fmt(r.withoutCode) }))
  return parts.join(' ')
}
