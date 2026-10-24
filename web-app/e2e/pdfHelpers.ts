// Ayudantes para revisar un PDF de jsPDF en los recorridos (Lote 213): el texto que dibuja, los colores de relleno (`r g b rg`) y el ancho de las imágenes (el logo).
import { inflateSync } from 'node:zlib'

/** Los flujos de contenido del PDF ya descomprimidos (texto de los operadores). */
export function pdfStreams(buf: Buffer): string[] {
  const raw = buf.toString('latin1')
  const out: string[] = []
  const re = /stream\r?\n/g
  let m: RegExpExecArray | null
  while ((m = re.exec(raw))) {
    const end = raw.indexOf('endstream', m.index + m[0].length)
    if (end < 0) break
    try {
      out.push(inflateSync(buf.subarray(m.index + m[0].length, end)).toString('latin1'))
    } catch {
      // un flujo que no es de contenido (imagen, fuente): no interesa
    }
  }
  return out
}

/** Texto de un PDF de jsPDF: las cadenas de los operadores `Tj` y `TJ`. */
export function pdfText(buf: Buffer): string {
  const parts: string[] = []
  for (const text of pdfStreams(buf)) {
    for (const s of text.matchAll(/\(((?:\\.|[^\\)])*)\)\s*Tj/g)) parts.push(s[1].replace(/\\(\d{3})/g, (_, o) => String.fromCharCode(parseInt(o, 8))).replace(/\\(.)/g, '$1'))
    for (const arr of text.matchAll(/\[((?:\((?:\\.|[^\\)])*\)|[^\]])*)\]\s*TJ/g)) parts.push([...arr[1].matchAll(/\(((?:\\.|[^\\)])*)\)/g)].map((x) => x[1]).join(''))
  }
  return parts.join('\n')
}

/** Colores de relleno (`rg`) que usa el PDF, como [r, g, b] de 0 a 255. */
export function pdfFillColors(buf: Buffer): [number, number, number][] {
  const out: [number, number, number][] = []
  for (const text of pdfStreams(buf)) {
    for (const m of text.matchAll(/(\d*\.?\d+)\s+(\d*\.?\d+)\s+(\d*\.?\d+)\s+rg\b/g)) out.push([Number(m[1]) * 255, Number(m[2]) * 255, Number(m[3]) * 255])
  }
  return out
}

/** ¿El PDF rellena algo con este color (±2 de 255, por el redondeo a 3 decimales del PDF)? */
export function pdfUsesFill(buf: Buffer, rgb: [number, number, number]): boolean {
  return pdfFillColors(buf).some((c) => c.every((v, i) => Math.abs(v - rgb[i]) <= 2))
}

/** Anchos de las imágenes que lleva el PDF (el logo): `/Subtype /Image … /Width N`. */
export function pdfImageWidths(buf: Buffer): number[] {
  const raw = buf.toString('latin1')
  return [...raw.matchAll(/\/Subtype\s*\/Image[^>]*?\/Width\s+(\d+)|\/Width\s+(\d+)[^>]*?\/Subtype\s*\/Image/g)].map((x) => Number(x[1] ?? x[2]))
}

/** `rgb(3, 31, 73)` o `#031F49` → [3, 31, 73]. */
export function cssToRgb(value: string): [number, number, number] {
  const v = value.trim()
  const hex = /^#([0-9a-f]{6})$/i.exec(v)
  if (hex) return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)) as [number, number, number]
  const rgb = /rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)/i.exec(v)
  if (rgb) return [Number(rgb[1]), Number(rgb[2]), Number(rgb[3])]
  throw new Error(`Color no entendido: ${value}`)
}
