import { spacing } from './theme'

/** Letra del campo con texto escrito/escaneado y tope de la del placeholder. */
export const INPUT_FONT = 20
const PLACEHOLDER_MIN_FONT = 12
/** Ancho medio de un carácter respecto de su tamaño de letra (fuente del sistema, con margen). */
const CHAR_WIDTH_RATIO = 0.55
/** Ancho que NO es del texto del campo, además de los márgenes de la pantalla: Aceptar (mín. 96), el teclado (44), los dos
 *  huecos de la fila, el relleno del campo (2 × 12) y su borde (2 × 2). */
const FIELD_CHROME = 96 + 44 + 2 * spacing.sm + 2 * spacing.md + 4

/**
 * Tamaño de letra con que el placeholder cabe en UNA línea: el de siempre (20) si cabe y, si no, el mayor que cabe en el
 * ancho de la pantalla (mínimo 12). El texto escrito sigue en 20. Pantallas de otros tamaños se ajustan solas.
 */
export function placeholderFontSize(windowWidth: number, text: string | undefined): number {
  if (!text) return INPUT_FONT
  const available = windowWidth - 2 * spacing.lg - FIELD_CHROME
  const fit = Math.floor(available / (text.length * CHAR_WIDTH_RATIO))
  return Math.max(PLACEHOLDER_MIN_FONT, Math.min(INPUT_FONT, fit))
}
