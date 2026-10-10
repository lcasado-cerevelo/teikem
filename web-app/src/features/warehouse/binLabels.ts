// Lote F16 — etiquetas de posición en Ubicaciones (solo web; usa el listado de posiciones existente, el servidor no cambió).
// Lógica pura de la pantalla y del flujo de impresión, sin React (se prueba con dependencias falsas):
// - Qué imprimir (`BinLabelsScope`): las posiciones del filtro actual de la tabla (TODOS sus filtros: zona, tipo, producto,
//   estatus, hoja y el buscador) o las marcadas con sus casillas (en tandas de 200 `binIds`).
// - Flujo (`printBinLabels`): lee `GET /warehouses/{id}/bins` por tandas de ≤ 200 con `skip` (el máximo del listado), con
//   sin tope (si pasa de `BIN_LABELS_NOTICE` la pantalla solo AVISA que el PDF es grande); ordena por código (orden
//   natural, como los reportes de códigos y las hojas) y arma y descarga el PDF (`kernel/ui/binLabelPdf`): una etiqueta =
//   una página del tamaño elegido. Cancelar durante la lectura no genera nada.
// - Reimprimir: las etiquetas NO tienen estado. Imprimirlas no marca nada ni llama a "marcar impresas" (eso es solo de las
//   hojas de posición del Lote F15): la etiqueta identifica la posición y no cambia con lo que hay en ella; se puede
//   volver a imprimir cuando se quiera con el mismo filtro o las mismas marcas.
import { chunk, type BinSheetDetail } from '../../kernel/ui/binSheetPdf'
import {
  BIN_LABEL_ORIENTATIONS,
  BIN_LABEL_SIZE_KEYS,
  binLabelNotices,
  type BinLabelBin,
  type BinLabelOrientation,
  type BinLabelPlan,
  type BinLabelSize,
  type BinLabelSpec,
} from '../../kernel/ui/binLabelPdf'
import type { WarehouseBinDto } from './api'
import { naturalCompare } from './barcodeReports'
import { binDetails } from './binProducts'
import type { BinListQuery } from './locations'

type Translate = (key: string, params?: Record<string, string | number>) => string

const S = 'warehouse.binLabels'

/** Permiso de las etiquetas: el de la pantalla y el del reporte de códigos de barras de posiciones (`inventory.view`). */
export const BIN_LABELS_PERMISSION = 'inventory.view'
/** Posiciones por lectura (el máximo de `GET .../bins`) y desde cuántas etiquetas se avisa que el PDF es grande. */
export const BIN_LABELS_PAGE_SIZE = 200
/** Desde cuántas etiquetas se AVISA que el PDF es grande (no se limita nada: se imprime todo). */
export const BIN_LABELS_NOTICE = 500

/** Qué imprimir. */
export type BinLabelsScope = 'filter' | 'selected'

/** Página del listado de posiciones (lo que lee el flujo). */
export interface BinPage {
  total?: number
  items?: WarehouseBinDto[] | null
}

/**
 * Consultas de `GET .../bins` para un alcance (sin `skip`/`take`): el filtro de la tabla tal cual (con el buscador), o las
 * marcadas en tandas de 200 ids (`binIds`, sin repetir y en orden). `query` null = filtros imposibles (nada que leer).
 */
export function binLabelsSources(scope: BinLabelsScope, query: BinListQuery | null, selectedIds: readonly number[]): BinListQuery[] {
  if (scope === 'selected') {
    const ids = [...new Set(selectedIds)].sort((a, b) => a - b)
    return chunk(ids, BIN_LABELS_PAGE_SIZE).map((binIds) => ({ binIds }))
  }
  return query ? [{ ...query }] : []
}

/** Datos de la etiqueta: almacén (código) y luego zona, pasillo, rack, nivel y posición (solo los que tienen valor). */
export function labelDetails(bin: Pick<WarehouseBinDto, 'zoneCode' | 'aisle' | 'rack' | 'level' | 'position' | 'isActive'>, warehouseCode: string | null | undefined, t: Translate): BinSheetDetail[] {
  const wh = (warehouseCode ?? '').trim()
  return [...(wh ? [{ label: t(`${S}.details.warehouse`), value: wh }] : []), ...binDetails(bin, t)]
}

/** Una posición del listado → la etiqueta. */
export function toLabelBin(bin: WarehouseBinDto, warehouseCode: string | null | undefined, t: Translate): BinLabelBin {
  return { code: bin.code ?? '', key: bin.id, details: labelDetails(bin, warehouseCode, t) }
}

/** Tamaño guardado (o el de por defecto, 4×2). */
export function parseLabelSize(value: string | null | undefined): BinLabelSize {
  return (BIN_LABEL_SIZE_KEYS as readonly string[]).includes(value ?? '') ? (value as BinLabelSize) : '4x2'
}

/** Orientación guardada (o 'auto'). */
export function parseLabelOrientation(value: string | null | undefined): BinLabelOrientation {
  return (BIN_LABEL_ORIENTATIONS as readonly string[]).includes(value ?? '') ? (value as BinLabelOrientation) : 'auto'
}

export type BinLabelsProgress = { phase: 'read'; done: number; total: number } | { phase: 'render'; labels: number }

export interface BinLabelsFlowDeps {
  /** Una tanda (`skip`/`take` ya puestos). */
  fetchPage: (query: BinListQuery) => Promise<BinPage>
  /** Arma y descarga el PDF; devuelve el plan (avisos). */
  download: (spec: BinLabelSpec) => Promise<BinLabelPlan>
  /** Cancelar (solo se atiende mientras se lee). */
  signal?: AbortSignal
  onProgress?: (p: BinLabelsProgress) => void
}

export interface BinLabelsFlowArgs {
  sources: readonly BinListQuery[]
  /** Título, compañía, idioma, tamaño y orientación del PDF. */
  spec: Pick<BinLabelSpec, 'title' | 'company' | 'locale' | 'size' | 'orientation'>
  /** Código del almacén (va en los datos de cada etiqueta). */
  warehouseCode?: string | null
  t: Translate
  lang: string
}

export type BinLabelsResult =
  | { status: 'nothing' }
  | { status: 'cancelled' }
  | { status: 'printed'; labels: number; withoutCode: number; notices: string[] }

/** Lee, ordena y descarga (ver el encabezado del archivo). Un error al leer o al generar se propaga. Nunca marca nada. */
export async function printBinLabels(deps: BinLabelsFlowDeps, args: BinLabelsFlowArgs): Promise<BinLabelsResult> {
  const aborted = () => deps.signal?.aborted === true
  const read = new Map<number, WarehouseBinDto>()
  let total = 0
  for (const source of args.sources) {
    let skip = 0
    let sourceTotal: number | null = null
    while (sourceTotal === null || skip < sourceTotal) {
      if (aborted()) return { status: 'cancelled' }
      let page: BinPage
      try {
        page = await deps.fetchPage({ ...source, skip, take: BIN_LABELS_PAGE_SIZE })
      } catch (err) {
        if (aborted()) return { status: 'cancelled' }
        throw err
      }
      if (sourceTotal === null) {
        sourceTotal = page.total ?? 0
        total += sourceTotal
      }
      const items = page.items ?? []
      for (const bin of items) if (bin.id != null && !read.has(bin.id)) read.set(bin.id, bin)
      deps.onProgress?.({ phase: 'read', done: read.size, total })
      if (items.length === 0) break
      skip += items.length
    }
  }
  if (aborted()) return { status: 'cancelled' }
  if (read.size === 0) return { status: 'nothing' }

  const cmp = naturalCompare(args.lang)
  const bins = [...read.values()].sort((a, b) => cmp(a.code ?? '', b.code ?? '')).map((b) => toLabelBin(b, args.warehouseCode, args.t))
  deps.onProgress?.({ phase: 'render', labels: bins.length })
  const plan = await deps.download({ ...args.spec, bins })
  return { status: 'printed', labels: plan.labels.length, withoutCode: plan.withoutCode, notices: binLabelNotices(plan, args.t) }
}

/** Texto del aviso final: "Se generaron N etiquetas de 4 × 2 pulgadas." (+ las que salieron sin código de barras). */
export function printedLabelsSummary(r: Extract<BinLabelsResult, { status: 'printed' }>, size: BinLabelSize, t: Translate, fmt: (n: number) => string = String): string {
  const parts = [t(r.labels === 1 ? `${S}.result.printedOne` : `${S}.result.printed`, { count: fmt(r.labels), size: t(`${S}.sizes.${size}.short`) })]
  if (r.withoutCode > 0) parts.push(t(r.withoutCode === 1 ? `${S}.result.withoutCodeOne` : `${S}.result.withoutCode`, { count: fmt(r.withoutCode) }))
  return parts.join(' ')
}
