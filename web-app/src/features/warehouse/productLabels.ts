// Etiquetas de producto (2026-10-09, pedido de Luis): lo mismo que las etiquetas de posición (Lote F16) pero para productos, en los mismos tamaños
// (4 × 2, 4 × 4 y 4 × 6 pulgadas): UNA etiqueta por producto y cada etiqueta es UNA página del tamaño exacto de la etiqueta, para imprimir SOLO la
// etiqueta en una impresora térmica (no la hoja de "Códigos de barras", que lleva muchos por hoja). Reutiliza el generador de etiquetas de
// `kernel/ui/binLabelPdf` (código de barras Code 128 a todo lo ancho, código en letra grande y hasta dos renglones de datos).
// - Qué se imprime: EXACTAMENTE los productos que filtra la tabla de Productos e inventario (`productListQuery`, de a 200 sin tope, como Exportar),
//   en orden natural de SKU, sin tope de etiquetas (si pasa de `PRODUCT_LABELS_NOTICE` la pantalla solo AVISA que el PDF es grande).
// - Cada etiqueta: el código de barras lleva el SKU exacto (la app del lector busca el producto por código de barras o por SKU: el código de
//   barras propio del producto NO se usa, igual que en el reporte de códigos de barras); debajo el SKU en grande y, en letra pequeña, el nombre del
//   producto y su categoría.
// - Las etiquetas no tienen estado: imprimirlas no marca nada y se pueden reimprimir cuando se quiera.
import type { BinSheetDetail } from '../../kernel/ui/binSheetPdf'
import { binLabelNotices, type BinLabelBin, type BinLabelPlan, type BinLabelSpec } from '../../kernel/ui/binLabelPdf'
import type { ProductListItemDto } from './api'
import { naturalCompare } from './barcodeReports'
import type { BinLabelsResult } from './binLabels'

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Permiso: el de la pantalla de Productos y de su reporte de códigos de barras (`inventory.view`). */
export const PRODUCT_LABELS_PERMISSION = 'inventory.view'
/** Desde cuántas etiquetas se AVISA que el PDF es grande (no se limita nada: se imprime todo). */
export const PRODUCT_LABELS_NOTICE = 500

/** Datos de la etiqueta: nombre del producto y, si tiene, su categoría. */
export function productLabelDetails(p: Pick<ProductListItemDto, 'name' | 'categoryName'>): BinSheetDetail[] {
  return [
    { label: '', value: (p.name ?? '').trim() },
    { label: '', value: (p.categoryName ?? '').trim() },
  ].filter((d) => d.value)
}

/** Un producto del listado → la etiqueta (el código es el SKU exacto). */
export function toProductLabel(p: ProductListItemDto): BinLabelBin {
  return { code: p.sku ?? '', key: p.id, details: productLabelDetails(p) }
}

export interface ProductLabelsFlowDeps {
  /** Lee todos los productos del filtro (`truncated` = se cortó en el tope de lectura). */
  fetchProducts: () => Promise<{ items: ProductListItemDto[]; truncated: boolean }>
  /** Arma y descarga el PDF; devuelve el plan (avisos). */
  download: (spec: BinLabelSpec) => Promise<BinLabelPlan>
  onProgress?: (p: { phase: 'read' } | { phase: 'render'; labels: number }) => void
}

export interface ProductLabelsFlowArgs {
  spec: Pick<BinLabelSpec, 'title' | 'company' | 'locale' | 'size' | 'orientation'>
  t: Translate
  lang: string
}

/** Lee, ordena por SKU y descarga. Un error al leer o al generar se propaga. Nunca marca nada. */
export async function printProductLabels(deps: ProductLabelsFlowDeps, args: ProductLabelsFlowArgs): Promise<BinLabelsResult> {
  deps.onProgress?.({ phase: 'read' })
  const { items } = await deps.fetchProducts()
  if (items.length === 0) return { status: 'nothing' }
  const cmp = naturalCompare(args.lang)
  const labels = [...items].sort((a, b) => cmp(a.sku ?? '', b.sku ?? '')).map(toProductLabel)
  deps.onProgress?.({ phase: 'render', labels: labels.length })
  const plan = await deps.download({ ...args.spec, bins: labels })
  return { status: 'printed', labels: plan.labels.length, withoutCode: plan.withoutCode, notices: binLabelNotices(plan, args.t) }
}
