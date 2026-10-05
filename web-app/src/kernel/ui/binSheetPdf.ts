// Informe "Productos por posición" (antes "hoja de posición", Lote F15; PDF vectorial carta vertical): una página por posición con la lista de
// productos que hay en ella y el código de barras Code 128 de cada uno, para escanearlos con el lector (Zebra) cuando el
// producto no tiene código visible o está muy alto. Genérico: el código de la posición se imprime tal cual (no se asume
// ningún formato de pasillo, piso o nivel).
// Reglas del dueño (2026-10-03):
// - UNA posición por hoja, siempre: cada posición empieza página nueva. Con más de `BIN_SHEET_MAX_PRODUCTS` (10) productos
//   se parte en hojas de 10 (11 → 10 + 1) y cada hoja repite el encabezado de la posición con "Hoja 2 de 2".
// - Encabezado: código de la posición en grande con su código de barras y, debajo, zona / pasillo / rack / nivel / posición
//   (lo que exista). Pie: "Impresa el <fecha y hora>" (formatos y zona de la compañía) y el almacén.
// - Cada producto es un BLOQUE: SKU en grande, nombre (recortado con "..." a un máximo de renglones) y el código de barras
//   del CÓDIGO DE BARRAS del producto; sin código de barras (o si Code 128 no lo admite o no cabe), el del SKU; si ninguno
//   sirve, el bloque va solo con texto y un aviso al pie de esa hoja (mismos textos que el reporte de códigos de barras).
// - Tamaño adaptable: el cuerpo de la hoja se reparte entre los N productos de la hoja (`binSheetBlockHeight`, entre
//   `BIN_SHEET_MIN_BLOCK_PT` y `BIN_SHEET_MAX_BLOCK_PT`); con bloques altos (1 o 2 productos) el código va debajo del texto a
//   todo el ancho ('stacked'); con bloques más bajos (3 a 10) va a la derecha del texto ('side'); si así no cabe, debajo de un
//   renglón "SKU · nombre" ('wide'). Módulo entre `BARCODE_MIN_MODULE_MM` (0.25 mm, el mismo del reporte de códigos) y
//   `BIN_SHEET_MAX_MODULE_MM`, con la zona de silencio de 10 módulos (`CODE128_QUIET_ZONE`); un valor que ni a todo el ancho
//   cabe a 0.25 mm sale "No cabe" (el aviso de siempre).
// - Posiciones sin productos: se omiten salvo `includeEmpty` (hoja con el encabezado y "Sin productos").
// Lo que decide qué se pinta y dónde es puro y se prueba sin DOM (`planBinSheets`, `binSheetCodeChoice`,
// `binSheetBlockHeight`, `binSheetBlockLayout`, `binSheetBodyHeight`, `sheetModuleWidth`); `renderBinSheetsPdf` arma el
// documento en memoria y `downloadBinSheetsPdf` lo descarga con el nombre de los demás reportes.
import type { jsPDF } from 'jspdf'
import { formatDateTime } from '../format/format'
import { t as translate } from '../i18n'
import { BARCODE_MIN_MODULE_MM, PT_PER_MM, barcodeEndNotices, clampLines } from './barcodeReportPdf'
import { CODE128_QUIET_ZONE, code128Unsupported, encodeCode128, isCode128Encodable, type Code128Symbol } from './code128'
import { pdfSafeText } from './exportTable'
import { REPORT_COLORS, REPORT_LAYOUT, drawReportNotice, reportFileName, reportNoticeHeight, reportNoticeLines } from './reportPdf'

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Carta vertical en puntos (como el reporte de códigos de barras). */
export const BIN_SHEET_PAGE = { width: 612, height: 792 } as const
/** Productos por hoja; con más, la posición sigue en otra hoja ("Hoja 2 de 2"). */
export const BIN_SHEET_MAX_PRODUCTS = 10
/** Alto mínimo y máximo de un bloque de producto (pt). Con 10 productos y el aviso más largo al pie, el bloque no baja de ~45. */
export const BIN_SHEET_MIN_BLOCK_PT = 44
export const BIN_SHEET_MAX_BLOCK_PT = 480
/** Desde este alto (1 o 2 productos) el bloque pone el código DEBAJO del texto a todo el ancho y el SKU puede ir más
 *  grande; más bajo, a la derecha del texto (con 3 productos así las barras salen más altas que debajo). */
export const BIN_SHEET_STACKED_MIN_BLOCK_PT = 200
/** Separación vertical entre bloques (pt). */
export const BIN_SHEET_BLOCK_GAP_PT = 8
/** Alto del encabezado (código, su código de barras, detalle y filete) y del pie (pt). */
export const BIN_SHEET_HEADER_PT = 108
export const BIN_SHEET_FOOTER_PT = 24
/** Módulo máximo del código de un producto (barras más anchas = se lee desde más lejos) y del de la posición (mm). */
export const BIN_SHEET_MAX_MODULE_MM = 1
export const BIN_SHEET_HEADER_MAX_MODULE_MM = 0.5
/** Alto de las barras: el de la posición, y el mínimo y el máximo de las de un producto (mm); con el código a la derecha
 *  del texto el máximo es menor (el símbolo es más angosto: más alto no ayuda y no pasaría al de 2 productos). */
export const BIN_SHEET_HEADER_BAR_MM = 12
export const BIN_SHEET_MIN_BAR_MM = 8
export const BIN_SHEET_MAX_BAR_MM = 60
export const BIN_SHEET_MAX_BAR_SIDE_MM = 30
/** Ancho mínimo de la columna de texto cuando el código va a la derecha (pt). */
export const BIN_SHEET_SIDE_TEXT_MIN_PT = 180
/** Renglones máximos del nombre: bloques altos y bloques bajos. */
export const BIN_SHEET_NAME_MAX_LINES = 3
export const BIN_SHEET_NAME_MAX_LINES_COMPACT = 2
/** Letra mínima del SKU y del código de la posición (pt); por encima se agranda hasta llenar el ancho. */
export const BIN_SHEET_SKU_MIN_PT = 10
export const BIN_SHEET_CODE_MAX_PT = 34
export const BIN_SHEET_CODE_MIN_PT = 14
/** Renglones máximos del aviso al pie de una hoja. */
export const BIN_SHEET_NOTICE_MAX_LINES = 4

const M = REPORT_LAYOUT.margin
/** Ancho útil de la hoja (pt). */
export const BIN_SHEET_INNER_PT = BIN_SHEET_PAGE.width - M * 2
/** Arriba y abajo del cuerpo (pt). */
export const BIN_SHEET_BODY_TOP = M + BIN_SHEET_HEADER_PT
export const BIN_SHEET_BODY_BOTTOM = BIN_SHEET_PAGE.height - M - BIN_SHEET_FOOTER_PT
/** Margen interior de un bloque alto, y el ancho máximo para un código (el de un bloque alto): más ancho = "No cabe". */
const STACKED_PAD = 8
export const BIN_SHEET_CODE_MAX_WIDTH_PT = BIN_SHEET_INNER_PT - STACKED_PAD * 2

const SKU_LINE = 1.15
const LINE = 1.2
const INNER_GAP = 4
const SIDE_GAP = 12

export interface BinSheetProduct {
  sku: string
  name?: string | null
  /** Código de barras propio del producto (vacío = sin código: se usa el SKU). */
  barcode?: string | null
}

export interface BinSheetDetail {
  /** Etiqueta ya traducida ("Zona", "Pasillo"…). */
  label: string
  /** Valor; vacío = solo la etiqueta (p. ej. "Inactiva"). */
  value: string
}

export interface BinSheetBin {
  /** Código de la posición, tal cual (también es el valor de su código de barras). */
  code: string
  /** Zona, pasillo, rack, nivel, posición… (solo los que existen), en ese orden. */
  details?: readonly BinSheetDetail[]
  /** Productos de la posición en el orden en que se imprimen. */
  products: readonly BinSheetProduct[]
  /** Instante de los datos ("Impresa el …"); por defecto el del reporte. */
  printedAt?: Date
  /** Identificador de quien llama (p. ej. el id de la posición), para saber qué se imprimió. */
  key?: string | number
}

export interface BinSheetSpec {
  /** Título del documento y base del nombre del archivo ("Productos por posición"). */
  title: string
  company?: string | null
  /** Almacén (código y nombre ya armados) para el pie de cada hoja. */
  warehouse?: string | null
  locale: string
  /** Instante por defecto de "Impresa el …" (y fecha del nombre del archivo). */
  printedAt: Date
  bins: readonly BinSheetBin[]
  /** true = las posiciones sin productos también salen ("Sin productos"). */
  includeEmpty?: boolean
}

// ---------------------------------------------------------------------------------------------------------------------
// Lógica pura
// ---------------------------------------------------------------------------------------------------------------------

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v))

/**
 * Ancho de módulo (pt) para un símbolo de `symbolModules` módulos en `widthPt`, contando la zona de silencio a cada lado:
 * el que llene el ancho sin pasar de `maxModuleMm` ni bajar de 0.25 mm (`BARCODE_MIN_MODULE_MM`); null = no cabe.
 */
export function sheetModuleWidth(symbolModules: number, widthPt: number, maxModuleMm: number = BIN_SHEET_MAX_MODULE_MM): number | null {
  const max = maxModuleMm * PT_PER_MM
  const min = BARCODE_MIN_MODULE_MM * PT_PER_MM
  const w = Math.min(max, widthPt / (symbolModules + 2 * CODE128_QUIET_ZONE))
  return w + 1e-9 >= min ? w : null
}

/** Qué codifica el bloque de un producto. `status`: con código, "No cabe" o no admitido (solo texto). */
export type BinSheetCodeChoice =
  | { status: 'ok'; value: string; source: 'barcode' | 'sku'; symbol: Code128Symbol }
  | { status: 'tooWide'; value: string; source: 'barcode' | 'sku'; symbol: Code128Symbol }
  | { status: 'unsupported'; value: string; chars: string[] }

/**
 * El código de un producto: su código de barras si Code 128 lo admite y cabe; si no, el SKU (exacto) si lo admite y cabe;
 * si alguno se admite pero no cabe, "No cabe"; si ninguno se admite (o los dos están vacíos), no admitido.
 */
export function binSheetCodeChoice(p: BinSheetProduct, maxWidthPt: number = BIN_SHEET_CODE_MAX_WIDTH_PT): BinSheetCodeChoice {
  const candidates: { value: string; source: 'barcode' | 'sku' }[] = []
  if (p.barcode && p.barcode.trim() !== '') candidates.push({ value: p.barcode, source: 'barcode' })
  if (p.sku && p.sku.trim() !== '') candidates.push({ value: p.sku, source: 'sku' })
  let wide: BinSheetCodeChoice | null = null
  for (const c of candidates) {
    if (!isCode128Encodable(c.value)) continue
    const symbol = encodeCode128(c.value)
    if (sheetModuleWidth(symbol.width, maxWidthPt) !== null) return { status: 'ok', ...c, symbol }
    wide ??= { status: 'tooWide', ...c, symbol }
  }
  if (wide) return wide
  const value = candidates[0]?.value ?? ''
  return { status: 'unsupported', value, chars: code128Unsupported(value) }
}

/** Un producto ya decidido para su hoja. */
export interface PlannedSheetProduct {
  sku: string
  name: string
  code: BinSheetCodeChoice
}

/** El código de barras de la posición en el encabezado: módulo, o null si no se admite o no cabe (va solo el texto). */
export interface PlannedSheetHeader {
  symbol: Code128Symbol | null
  modulePt: number | null
  /** Caracteres no admitidos del código (vacío si se admite). */
  chars: string[]
}

/** Una hoja: UNA posición (o una parte de ella si tiene más de 10 productos). */
export interface BinSheetPage {
  /** Índice de la posición en `spec.bins`. */
  bin: number
  code: string
  details: readonly BinSheetDetail[]
  printedAt?: Date
  key?: string | number
  /** Parte de la posición (base 1) y total de partes. */
  part: number
  parts: number
  /** true = posición sin productos ("Sin productos"). */
  empty: boolean
  header: PlannedSheetHeader
  products: PlannedSheetProduct[]
  /** Lo que va al aviso del pie de esta hoja (los mismos textos que el reporte de códigos de barras). */
  tooWide: string[]
  unsupported: { value: string; chars: string[] }[]
}

export interface BinSheetPlan {
  pages: BinSheetPage[]
  /** Códigos de las posiciones omitidas por no tener productos (sin `includeEmpty`). */
  omittedEmpty: string[]
  /** Posiciones con al menos una hoja (índices de `spec.bins`, sin repetir, en orden). */
  printedBins: number[]
  /** Productos (bloques) sin código: no caben o no se admiten. */
  withoutCode: number
}

/** Parte una lista en tandas de `size` (la última, lo que sobre); vacía → []. */
export function chunk<T>(items: readonly T[], size: number): T[][] {
  const out: T[][] = []
  for (let i = 0; i < items.length; i += size) out.push(items.slice(i, i + size))
  return out
}

/** Código de barras de la posición: el de su código exacto, a lo más 0.5 mm por módulo y a todo el ancho. */
export function planSheetHeader(code: string, widthPt: number = BIN_SHEET_INNER_PT): PlannedSheetHeader {
  if (!isCode128Encodable(code)) return { symbol: null, modulePt: null, chars: code128Unsupported(code) }
  const symbol = encodeCode128(code)
  return { symbol, modulePt: sheetModuleWidth(symbol.width, widthPt, BIN_SHEET_HEADER_MAX_MODULE_MM), chars: [] }
}

/**
 * Hojas a imprimir: cada posición empieza hoja nueva; sus productos se parten en hojas de `BIN_SHEET_MAX_PRODUCTS` (cada
 * una con "Hoja i de n"); una posición sin productos sale como una hoja "Sin productos" solo con `includeEmpty` (si no, va
 * a `omittedEmpty`). Los avisos de cada hoja: sus productos sin código y, si el de la posición no se pudo codificar, ese.
 */
export function planBinSheets(bins: readonly BinSheetBin[], options: { includeEmpty?: boolean } = {}): BinSheetPlan {
  const pages: BinSheetPage[] = []
  const omittedEmpty: string[] = []
  const printedBins: number[] = []
  let withoutCode = 0
  bins.forEach((bin, index) => {
    const products: PlannedSheetProduct[] = bin.products.map((p) => ({ sku: p.sku, name: p.name ?? '', code: binSheetCodeChoice(p) }))
    if (products.length === 0 && !options.includeEmpty) {
      omittedEmpty.push(bin.code)
      return
    }
    const header = planSheetHeader(bin.code)
    const parts = products.length === 0 ? [[]] : chunk(products, BIN_SHEET_MAX_PRODUCTS)
    parts.forEach((slice, i) => {
      const tooWide: string[] = []
      const unsupported: { value: string; chars: string[] }[] = []
      if (header.symbol === null) unsupported.push({ value: bin.code, chars: header.chars })
      else if (header.modulePt === null) tooWide.push(bin.code)
      for (const p of slice) {
        if (p.code.status === 'tooWide') tooWide.push(p.code.value)
        if (p.code.status === 'unsupported') unsupported.push({ value: p.code.value, chars: p.code.chars })
      }
      withoutCode += slice.filter((p) => p.code.status !== 'ok').length
      pages.push({
        bin: index,
        code: bin.code,
        details: bin.details ?? [],
        printedAt: bin.printedAt,
        key: bin.key,
        part: i + 1,
        parts: parts.length,
        empty: slice.length === 0,
        header,
        products: slice,
        tooWide,
        unsupported,
      })
    })
    printedBins.push(index)
  })
  return { pages, omittedEmpty, printedBins, withoutCode }
}

/** Alto del cuerpo de una hoja (pt): entre el encabezado y el pie, menos el aviso del pie si lo hay (`noticeLines` renglones). */
export function binSheetBodyHeight(noticeLines = 0): number {
  const lines = Math.min(noticeLines, BIN_SHEET_NOTICE_MAX_LINES)
  const notice = lines > 0 ? reportNoticeHeight(lines) + BIN_SHEET_BLOCK_GAP_PT : 0
  return BIN_SHEET_BODY_BOTTOM - BIN_SHEET_BODY_TOP - notice
}

/**
 * Alto de cada bloque cuando la hoja tiene `n` productos y `available` pt de cuerpo: el cuerpo repartido entre los `n`
 * (menos las separaciones), acotado a [`BIN_SHEET_MIN_BLOCK_PT`, `BIN_SHEET_MAX_BLOCK_PT`]. Menos productos = bloques más altos.
 */
export function binSheetBlockHeight(n: number, available: number): number {
  if (n <= 0) return 0
  const share = (available - BIN_SHEET_BLOCK_GAP_PT * (n - 1)) / n
  return clamp(share, BIN_SHEET_MIN_BLOCK_PT, BIN_SHEET_MAX_BLOCK_PT)
}

export type BinSheetBlockMode = 'stacked' | 'side' | 'wide'

/** Medidas de un bloque de producto (pt). Las letras son el máximo: el SKU se achica si no cabe en `textWidth`. */
export interface BinSheetBlockLayout {
  mode: BinSheetBlockMode
  height: number
  pad: number
  skuSize: number
  nameSize: number
  nameMaxLines: number
  /** Letra del valor legible debajo de las barras (0 = sin ese renglón: modo 'wide'). */
  valueSize: number
  barHeight: number
  /** Ancho de módulo; null = sin símbolo (no cabe o no se admite: va el texto del valor y el aviso). */
  modulePt: number | null
  /** Ancho del código con su zona de silencio (o del recuadro del aviso cuando no hay símbolo). */
  codeWidth: number
  /** Ancho de la columna de texto. */
  textWidth: number
}

/**
 * Medidas del bloque de un producto de alto `h` con un símbolo de `symbolModules` módulos (null = sin símbolo):
 * - 'stacked' (h ≥ `BIN_SHEET_STACKED_MIN_BLOCK_PT`): SKU, nombre (hasta 3 renglones), código a todo el ancho y su valor;
 * - 'side': texto a la izquierda (al menos `BIN_SHEET_SIDE_TEXT_MIN_PT`) y código a la derecha, si así cabe a 0.25 mm;
 * - 'wide': si a la derecha no cabe, un renglón "SKU · nombre" y el código a todo el ancho debajo (sin el valor legible).
 * Barras entre `BIN_SHEET_MIN_BAR_MM` y `BIN_SHEET_MAX_BAR_MM` (`BIN_SHEET_MAX_BAR_SIDE_MM` fuera de 'stacked'); módulo
 * hasta `BIN_SHEET_MAX_MODULE_MM`.
 */
export function binSheetBlockLayout(h: number, symbolModules: number | null, inner: number = BIN_SHEET_INNER_PT): BinSheetBlockLayout {
  const minBar = BIN_SHEET_MIN_BAR_MM * PT_PER_MM
  const maxBar = BIN_SHEET_MAX_BAR_MM * PT_PER_MM
  const symbolWidth = (mw: number | null, fallback: number) => (mw !== null && symbolModules !== null ? (symbolModules + 2 * CODE128_QUIET_ZONE) * mw : fallback)
  if (h >= BIN_SHEET_STACKED_MIN_BLOCK_PT) {
    const pad = STACKED_PAD
    const skuSize = clamp(h * 0.15, 14, 64)
    const nameSize = clamp(h * 0.06, 9, 20)
    const nameMaxLines = h >= 260 ? BIN_SHEET_NAME_MAX_LINES : BIN_SHEET_NAME_MAX_LINES_COMPACT
    const valueSize = clamp(h * 0.045, 8, 14)
    const area = inner - pad * 2
    const modulePt = symbolModules === null ? null : sheetModuleWidth(symbolModules, area)
    const fixed = pad * 2 + skuSize * SKU_LINE + nameMaxLines * nameSize * LINE + valueSize * LINE + INNER_GAP * 3
    return {
      mode: 'stacked',
      height: h,
      pad,
      skuSize,
      nameSize,
      nameMaxLines,
      valueSize,
      barHeight: clamp(h - fixed, minBar, maxBar),
      modulePt,
      codeWidth: symbolWidth(modulePt, area),
      textWidth: area,
    }
  }
  const pad = clamp(h * 0.08, 4, 8)
  const valueSize = clamp(h * 0.07, 7, 12)
  const sideArea = inner - pad * 2 - SIDE_GAP - BIN_SHEET_SIDE_TEXT_MIN_PT
  const sideModule = symbolModules === null ? null : sheetModuleWidth(symbolModules, sideArea)
  if (symbolModules === null || sideModule !== null) {
    const codeWidth = symbolWidth(sideModule, Math.min(sideArea, inner * 0.4))
    return {
      mode: 'side',
      height: h,
      pad,
      skuSize: clamp(h * 0.22, 11, 30),
      nameSize: clamp(h * 0.09, 8, 14),
      nameMaxLines: BIN_SHEET_NAME_MAX_LINES_COMPACT,
      valueSize,
      barHeight: clamp(h - pad * 2 - valueSize * LINE - 2, minBar, BIN_SHEET_MAX_BAR_SIDE_MM * PT_PER_MM),
      modulePt: sideModule,
      codeWidth,
      textWidth: inner - pad * 2 - SIDE_GAP - codeWidth,
    }
  }
  const textSize = clamp(h * 0.2, 9, 14)
  const area = inner - pad * 2
  const modulePt = sheetModuleWidth(symbolModules, area)
  return {
    mode: 'wide',
    height: h,
    pad,
    skuSize: textSize,
    nameSize: textSize,
    nameMaxLines: 1,
    valueSize: 0,
    barHeight: clamp(h - pad * 2 - textSize * SKU_LINE - 2, minBar, BIN_SHEET_MAX_BAR_SIDE_MM * PT_PER_MM),
    modulePt,
    codeWidth: symbolWidth(modulePt, area),
    textWidth: area,
  }
}

/** Letra (pt) para que `text` quepa en `width`: la mayor entre `min` y `max` (`unitWidth` = ancho del texto a 1 pt). */
export function fitFontSize(unitWidth: number, width: number, max: number, min: number): number {
  if (unitWidth <= 0) return max
  return clamp(width / unitWidth, min, max)
}

/** Avisos del pie de una hoja (productos o código de la posición sin código de barras). */
export function binSheetNotices(page: Pick<BinSheetPage, 'tooWide' | 'unsupported'>, t: Translate = translate): string[] {
  return barcodeEndNotices(page, t)
}

// ---------------------------------------------------------------------------------------------------------------------
// Dibujo
// ---------------------------------------------------------------------------------------------------------------------

/** Barras del símbolo con su zona de silencio empezando en `left` (vectoriales: rectángulos). */
function drawBars(doc: jsPDF, symbol: Code128Symbol, modulePt: number, left: number, top: number, height: number) {
  const x0 = left + CODE128_QUIET_ZONE * modulePt
  doc.setFillColor(0, 0, 0)
  for (const bar of symbol.bars) doc.rect(x0 + bar.x * modulePt, top, bar.width * modulePt, height, 'F')
}

/** SKU en negrita a la mayor letra que quepa en `width` (si ni a la mínima cabe, en 2 renglones). Devuelve renglones y letra. */
function skuLines(doc: jsPDF, sku: string, width: number, max: number): { lines: string[]; size: number } {
  doc.setFont('helvetica', 'bold')
  const text = pdfSafeText(sku)
  const size = fitFontSize(doc.getStringUnitWidth(text), width, max, Math.min(max, BIN_SHEET_SKU_MIN_PT))
  doc.setFontSize(size)
  if (doc.getTextWidth(text) <= width + 0.5) return { lines: [text], size }
  return { lines: clampLines(doc, doc.splitTextToSize(text, width) as string[], 2, width), size }
}

/** Nombre a `size` en `width`, recortado a `max` renglones con "...". */
function nameLines(doc: jsPDF, name: string, width: number, size: number, max: number): string[] {
  const text = pdfSafeText(name.trim())
  if (!text) return []
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(size)
  return clampLines(doc, doc.splitTextToSize(text, width) as string[], max, width)
}

/** Sin símbolo: el valor en letra de máquina y el motivo, dentro de `width` desde `left` (centrado). */
function drawNoCode(doc: jsPDF, code: BinSheetCodeChoice, left: number, top: number, width: number, height: number) {
  const { muted, noticeInk } = REPORT_COLORS
  const cx = left + width / 2
  doc.setFont('courier', 'normal')
  doc.setFontSize(8)
  doc.setTextColor(...muted)
  const value = code.value ? (doc.splitTextToSize(pdfSafeText(code.value), width) as string[]).slice(0, 2) : []
  const reason =
    code.status === 'unsupported'
      ? translate('ui.binSheet.noCode', { chars: code.chars.map((c) => pdfSafeText(c) || '?').join(' ') || '-' })
      : translate('ui.barcodeReport.tooWide')
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(8)
  const reasonLines = (doc.splitTextToSize(pdfSafeText(reason), width) as string[]).slice(0, 2)
  const total = (value.length + reasonLines.length) * 10
  let y = top + Math.max(0, (height - total) / 2) + 8
  doc.setFont('courier', 'normal')
  doc.setTextColor(...muted)
  for (const l of value) {
    doc.text(l, cx, y, { align: 'center' })
    y += 10
  }
  doc.setFont('helvetica', 'bold')
  doc.setTextColor(...noticeInk)
  for (const l of reasonLines) {
    doc.text(l, cx, y, { align: 'center' })
    y += 10
  }
}

/** Un bloque de producto en `y` con alto `h`. */
function drawProductBlock(doc: jsPDF, p: PlannedSheetProduct, y: number, h: number) {
  const { ink, line } = REPORT_COLORS
  const inner = BIN_SHEET_INNER_PT
  const symbol = p.code.status === 'ok' ? p.code.symbol : null
  const layout = binSheetBlockLayout(h, symbol ? symbol.width : null, inner)
  const { pad } = layout
  doc.setDrawColor(...line)
  doc.setLineWidth(0.7)
  doc.roundedRect(M, y, inner, h, 4, 4, 'S')
  const valueLine = layout.valueSize * LINE

  if (layout.mode === 'wide') {
    // un renglón "SKU · nombre" y el código a todo el ancho debajo
    const textY = y + pad + layout.skuSize * 0.9
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(layout.skuSize)
    doc.setTextColor(...ink)
    const sku = clampLines(doc, doc.splitTextToSize(pdfSafeText(p.sku), layout.textWidth) as string[], 1, layout.textWidth)[0] ?? ''
    doc.text(sku, M + pad, textY)
    const used = doc.getTextWidth(`${sku}  `)
    const rest = layout.textWidth - used
    if (rest > 30 && p.name.trim()) {
      doc.setFont('helvetica', 'normal')
      const name = clampLines(doc, doc.splitTextToSize(pdfSafeText(`· ${p.name.trim()}`), rest) as string[], 1, rest)[0] ?? ''
      doc.text(name, M + pad + used, textY)
    }
    if (symbol && layout.modulePt !== null) {
      const top = y + h - pad - layout.barHeight
      drawBars(doc, symbol, layout.modulePt, M + inner / 2 - layout.codeWidth / 2, top, layout.barHeight)
    }
    return
  }

  const sku = skuLines(doc, p.sku, layout.textWidth, layout.skuSize)
  const skuH = sku.lines.length * sku.size * SKU_LINE
  const names = nameLines(doc, p.name, layout.textWidth, layout.nameSize, layout.nameMaxLines)
  const nameH = names.length * layout.nameSize * LINE
  const drawText = (x: number, top: number) => {
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(sku.size)
    doc.setTextColor(...ink)
    sku.lines.forEach((l, i) => doc.text(l, x, top + sku.size * 0.86 + i * sku.size * SKU_LINE))
    if (names.length > 0) {
      doc.setFont('helvetica', 'normal')
      doc.setFontSize(layout.nameSize)
      names.forEach((l, i) => doc.text(l, x, top + skuH + INNER_GAP + layout.nameSize * 0.86 + i * layout.nameSize * LINE))
    }
  }
  const drawValue = (cx: number, top: number) => {
    doc.setFont('courier', 'normal')
    doc.setFontSize(layout.valueSize)
    doc.setTextColor(...ink)
    const width = layout.codeWidth
    const v = clampLines(doc, doc.splitTextToSize(pdfSafeText(p.code.value), width) as string[], 1, width)[0] ?? ''
    doc.text(v, cx, top + layout.valueSize * 0.86, { align: 'center' })
  }

  if (layout.mode === 'stacked') {
    // texto arriba y código a todo el ancho debajo; el conjunto, centrado en el alto del bloque
    const codeH = symbol ? layout.barHeight + 2 + valueLine : 40
    const content = skuH + (nameH > 0 ? INNER_GAP + nameH : 0) + INNER_GAP * 2 + codeH
    const top = y + Math.max(pad, (h - content) / 2)
    drawText(M + pad, top)
    const codeTop = top + skuH + (nameH > 0 ? INNER_GAP + nameH : 0) + INNER_GAP * 2
    if (symbol && layout.modulePt !== null) {
      const left = M + inner / 2 - layout.codeWidth / 2
      drawBars(doc, symbol, layout.modulePt, left, codeTop, layout.barHeight)
      drawValue(M + inner / 2, codeTop + layout.barHeight + 2)
    } else {
      drawNoCode(doc, p.code, M + pad, codeTop, layout.textWidth, codeH)
    }
    return
  }

  // 'side': texto a la izquierda (centrado en el alto) y código a la derecha
  const textH = skuH + (nameH > 0 ? INNER_GAP + nameH : 0)
  drawText(M + pad, y + Math.max(pad, (h - textH) / 2))
  const codeLeft = M + inner - pad - layout.codeWidth
  if (symbol && layout.modulePt !== null) {
    const codeH = layout.barHeight + 2 + valueLine
    const top = y + Math.max(pad, (h - codeH) / 2)
    drawBars(doc, symbol, layout.modulePt, codeLeft, top, layout.barHeight)
    drawValue(codeLeft + layout.codeWidth / 2, top + layout.barHeight + 2)
  } else {
    drawNoCode(doc, p.code, codeLeft, y + pad, layout.codeWidth, h - pad * 2)
  }
}

/** Una hoja completa: encabezado de la posición, bloques (o "Sin productos"), aviso del pie y pie. */
function drawSheet(doc: jsPDF, spec: BinSheetSpec, page: BinSheetPage) {
  const { ink, muted, line, navy, noticeInk } = REPORT_COLORS
  const W = BIN_SHEET_PAGE.width
  const H = BIN_SHEET_PAGE.height
  const inner = BIN_SHEET_INNER_PT

  // ---- encabezado: código grande (y "Hoja i de n" a la derecha), su código de barras y el detalle ----
  const partText = page.parts > 1 ? pdfSafeText(translate('ui.binSheet.part', { part: page.part, parts: page.parts })) : ''
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(12)
  const partW = partText ? doc.getTextWidth(partText) + 16 : 0
  if (partText) {
    doc.setTextColor(...navy)
    doc.text(partText, W - M, M + 22, { align: 'right' })
  }
  const code = pdfSafeText(page.code)
  const codeSize = fitFontSize(doc.getStringUnitWidth(code), inner - partW, BIN_SHEET_CODE_MAX_PT, BIN_SHEET_CODE_MIN_PT)
  doc.setFontSize(codeSize)
  doc.setTextColor(...ink)
  doc.text(clampLines(doc, doc.splitTextToSize(code, inner - partW) as string[], 1, inner - partW)[0] ?? '', M, M + 28)
  const barTop = M + 40
  const barH = BIN_SHEET_HEADER_BAR_MM * PT_PER_MM
  if (page.header.symbol && page.header.modulePt !== null) {
    // la zona de silencio empieza en el margen (las barras quedan un poco a la derecha del código impreso)
    drawBars(doc, page.header.symbol, page.header.modulePt, M, barTop, barH)
  } else {
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(9)
    doc.setTextColor(...noticeInk)
    const reason = page.header.symbol ? translate('ui.barcodeReport.tooWide') : translate('ui.binSheet.noCode', { chars: page.header.chars.join(' ') || '-' })
    doc.text(pdfSafeText(reason), M, barTop + barH / 2)
  }
  const details = page.details.map((d) => `${d.label} ${d.value}`.trim()).join('  ·  ')
  if (details) {
    doc.setFont('helvetica', 'normal')
    doc.setFontSize(10)
    doc.setTextColor(...muted)
    doc.text(clampLines(doc, doc.splitTextToSize(pdfSafeText(details), inner) as string[], 1, inner)[0] ?? '', M, barTop + barH + 15)
  }
  doc.setDrawColor(...navy)
  doc.setLineWidth(1.2)
  doc.line(M, BIN_SHEET_BODY_TOP - 8, W - M, BIN_SHEET_BODY_TOP - 8)

  // ---- aviso del pie (si hay productos sin código) ----
  let notice: string[] = binSheetNotices(page).flatMap((n) => reportNoticeLines(doc, n))
  if (notice.length > BIN_SHEET_NOTICE_MAX_LINES) {
    notice = notice.slice(0, BIN_SHEET_NOTICE_MAX_LINES)
    notice[notice.length - 1] = `${notice[notice.length - 1].replace(/\.*\s*$/, '')}...`
  }
  const available = binSheetBodyHeight(notice.length)

  // ---- cuerpo ----
  if (page.empty) {
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(30)
    doc.setTextColor(...muted)
    const cy = BIN_SHEET_BODY_TOP + available / 2
    doc.text(pdfSafeText(translate('ui.binSheet.empty')), W / 2, cy, { align: 'center' })
    doc.setFont('helvetica', 'normal')
    doc.setFontSize(11)
    doc.text(pdfSafeText(translate('ui.binSheet.emptyHint')), W / 2, cy + 22, { align: 'center', maxWidth: inner })
  } else {
    const h = binSheetBlockHeight(page.products.length, available)
    page.products.forEach((p, i) => drawProductBlock(doc, p, BIN_SHEET_BODY_TOP + i * (h + BIN_SHEET_BLOCK_GAP_PT), h))
  }
  if (notice.length > 0) drawReportNotice(doc, notice, BIN_SHEET_BODY_BOTTOM - reportNoticeHeight(notice.length))

  // ---- pie: "Impresa el …" y el almacén ----
  const footY = H - M - BIN_SHEET_FOOTER_PT + 8
  doc.setDrawColor(...line)
  doc.setLineWidth(0.6)
  doc.line(M, footY, W - M, footY)
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(8.5)
  doc.setTextColor(...muted)
  const when = formatDateTime(page.printedAt ?? spec.printedAt, spec.locale)
  const left = pdfSafeText(translate('ui.binSheet.printedAt', { date: when }))
  doc.text(left, M, footY + 13)
  const right = [spec.warehouse ? translate('ui.binSheet.warehouse', { warehouse: spec.warehouse }) : '', spec.company?.trim() ?? ''].filter(Boolean).join(' · ')
  if (right) {
    const width = inner - doc.getTextWidth(left) - 20
    if (width > 40) doc.text(clampLines(doc, doc.splitTextToSize(pdfSafeText(right), width) as string[], 1, width)[0] ?? '', W - M, footY + 13, { align: 'right' })
  }
}

export interface RenderBinSheetsOptions {
  /** false = sin comprimir (pruebas: el texto queda legible en el PDF). Por defecto true. */
  compress?: boolean
}

/** Arma el documento en memoria (no descarga). Sin hojas: una página con el texto de vacío (quien llama no debería pedirlo). */
export async function renderBinSheetsPdf(spec: BinSheetSpec, options: RenderBinSheetsOptions = {}): Promise<{ doc: jsPDF; plan: BinSheetPlan }> {
  const { jsPDF: JsPdf } = await import('jspdf')
  const doc = new JsPdf({ orientation: 'portrait', unit: 'pt', format: 'letter', compress: options.compress ?? true })
  doc.setProperties({ title: pdfSafeText(spec.title), subject: pdfSafeText(spec.title), creator: 'Teikem' })
  const plan = planBinSheets(spec.bins, { includeEmpty: spec.includeEmpty })
  plan.pages.forEach((page, i) => {
    if (i > 0) doc.addPage('letter', 'portrait')
    drawSheet(doc, spec, page)
  })
  if (plan.pages.length === 0) {
    doc.setFont('helvetica', 'italic')
    doc.setFontSize(11)
    doc.setTextColor(...REPORT_COLORS.muted)
    doc.text(pdfSafeText(translate('ui.binSheet.nothing')), BIN_SHEET_PAGE.width / 2, BIN_SHEET_BODY_TOP, { align: 'center' })
  }
  return { doc, plan }
}

/** Arma las hojas y las descarga como `reportFileName(spec)` ("productos-por-posicion-<compañía>-<día>.pdf"); devuelve el plan. */
export async function downloadBinSheetsPdf(spec: BinSheetSpec): Promise<BinSheetPlan> {
  const { doc, plan } = await renderBinSheetsPdf(spec)
  doc.save(reportFileName(spec, spec.printedAt))
  return plan
}
