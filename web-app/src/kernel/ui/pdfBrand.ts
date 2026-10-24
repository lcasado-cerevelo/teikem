// Colores de la compañía en los PDF (2026-10-23, pedido de Luis: «los colores que usan los PDF deben ser los establecidos por la compañía, no otros»). Todo PDF (tablas, tablas agrupadas y
// reportes) toma sus colores de la marca que la compañía definió en Ajustes → Marca, que el tema ya aplica como variables CSS en <html>: `--navbar` (el color de la barra: el azul marino de la
// compañía; encabezados de tabla, bandas y títulos), `--on-navbar` (el texto sobre él), `--flow` (su primer acento: filetes y detalles) y `--money` (el tercer color, el gris o el que haya elegido).
// El papel es blanco: el encabezado de tabla y las bandas usan el color de la barra (oscuro en los dos temas) y las zonas suaves (cebra, banda de grupo) son una mezcla clara de él.
// Sin marca propia (o sin DOM, como en las pruebas) valen los colores de siempre del tema por omisión. Lógica pura salvo `readPdfBrand`, que lee las variables.

export type Rgb = [number, number, number]

export interface PdfBrand {
  /** Color de la barra de la compañía: encabezado de tabla, banda, títulos. */
  navy: Rgb
  /** Texto sobre `navy` (blanco o casi negro según el contraste). */
  onNavy: Rgb
  /** Primer acento (filetes, detalles). */
  flow: Rgb
  /** Tercer color de la marca. */
  money: Rgb
  /** `money` oscurecido para texto sobre blanco. */
  moneyInk: Rgb
  /** Banda de grupo: una mezcla clara de `navy`. */
  tint: Rgb
  /** Fila alterna (cebra): una mezcla muy clara de `navy`. */
  zebra: Rgb
}

const WHITE: Rgb = [255, 255, 255]
const BLACK: Rgb = [0, 0, 0]

/** Mezcla en sRGB: `share` (0-1) de `a` y el resto de `b`. */
export function mixRgb(a: Rgb, b: Rgb, share: number): Rgb {
  return [0, 1, 2].map((i) => Math.round(a[i] * share + b[i] * (1 - share))) as Rgb
}

function luminance([r, g, b]: Rgb): number {
  const c = (v: number) => {
    const s = v / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * c(r) + 0.7152 * c(g) + 0.0722 * c(b)
}

/** Blanco sobre los colores oscuros y casi negro sobre los claros. */
export const onColor = (bg: Rgb): Rgb => (luminance(bg) > 0.4 ? [24, 33, 54] : WHITE)

/** `#RGB`, `#RRGGBB`, `rgb(r, g, b)` o `rgba(r, g, b, a)` → [r, g, b]; null si no se entiende. */
export function parseCssColor(value: string | null | undefined): Rgb | null {
  const v = (value ?? '').trim()
  let m = /^#([0-9a-f]{3})$/i.exec(v)
  if (m) return m[1].split('').map((h) => parseInt(h + h, 16)) as Rgb
  m = /^#([0-9a-f]{6})$/i.exec(v)
  if (m) return [0, 2, 4].map((i) => parseInt(m![1].slice(i, i + 2), 16)) as Rgb
  m = /^rgba?\(\s*(\d{1,3})[\s,]+(\d{1,3})[\s,]+(\d{1,3})/i.exec(v)
  if (m) return [Number(m[1]), Number(m[2]), Number(m[3])].map((n) => Math.min(255, n)) as Rgb
  return null
}

/** Arma los colores del PDF a partir de los de la marca. */
export function pdfBrandFrom(navy: Rgb, flow: Rgb, money: Rgb, onNavy?: Rgb): PdfBrand {
  return { navy, onNavy: onNavy ?? onColor(navy), flow, money, moneyInk: mixRgb(money, BLACK, 0.65), tint: mixRgb(navy, WHITE, 0.1), zebra: mixRgb(navy, WHITE, 0.04) }
}

/** Los colores de siempre (tema por omisión: azul marino, naranja y gris). */
export const DEFAULT_PDF_BRAND: PdfBrand = pdfBrandFrom([3, 31, 73], [243, 113, 35], [108, 117, 125], WHITE)

/** Los colores vigentes de la compañía (variables CSS de <html>); lo que falte, el de siempre. */
export function readPdfBrand(root: Element | null = typeof document === 'undefined' ? null : document.documentElement): PdfBrand {
  if (!root || typeof getComputedStyle === 'undefined') return DEFAULT_PDF_BRAND
  const style = getComputedStyle(root)
  const get = (name: string) => parseCssColor(style.getPropertyValue(name))
  const navy = get('--navbar')
  const flow = get('--flow')
  const money = get('--money')
  if (!navy && !flow && !money) return DEFAULT_PDF_BRAND
  return pdfBrandFrom(navy ?? DEFAULT_PDF_BRAND.navy, flow ?? DEFAULT_PDF_BRAND.flow, money ?? DEFAULT_PDF_BRAND.money, get('--on-navbar') ?? undefined)
}
