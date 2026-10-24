// Los colores del PDF de una tabla del portal (hoja blanca): los de la marca de la compañía (Ajustes → Marca): la barra para encabezados y títulos y el acento para el filete; sin marca propia, los de siempre
// del portal. Los comparten el PDF de la web (jsPDF) y el de la app instalada (HTML → PDF).
import { brandedPalette, getBrand } from './theme'

export type Rgb = [number, number, number]

const hexToRgb = (hex: string): Rgb => {
  const n = Number.parseInt(hex.replace('#', '').slice(0, 6), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}
const mix = (a: Rgb, b: Rgb, share: number): Rgb => [0, 1, 2].map((i) => Math.round(a[i] * share + b[i] * (1 - share))) as Rgb

export function pdfColors() {
  const pal = brandedPalette('light', getBrand())
  const navy = hexToRgb(pal.brand)
  return { navy, onNavy: hexToRgb(pal.onBrand), flow: hexToRgb(pal.flow), zebra: mix(navy, [255, 255, 255], 0.05) }
}

export const rgbCss = ([r, g, b]: Rgb) => `rgb(${r}, ${g}, ${b})`
