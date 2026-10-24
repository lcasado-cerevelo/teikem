// Excel (.xlsx) y PDF de las tablas del portal, en la web (2026-10-23, pedido de Luis: «como en la aplicación del personal»). Se cargan bajo demanda (import dinámico) para no inflar el paquete inicial.
// Los dos llevan arriba el LOGO (el de la compañía si subió uno; si no, el de Teikem), la compañía, el título, «Generado el …» y la oración de filtros. Fechas AAAA-MM-DD salen como fecha en Excel.
// En el PDF (A4, horizontal con más de 5 columnas) los números van a la derecha. Las fuentes estándar de jsPDF solo cubren Latin-1: `pdfSafeText` cambia lo demás por su parecido más cercano.
import { loadPdfLogoSource } from '../brand/portalBrand'
import { brandedPalette, getBrand } from './theme'
import { generatedText, type ExportData, type ExportHeading } from './tableExport'

/** Las fuentes estándar de jsPDF solo cubren Latin-1 (el español cabe): lo demás se reemplaza por su equivalente más cercano. */
export function pdfSafeText(s: string): string {
  return s
    .replace(/[‘’‚]/g, "'")
    .replace(/[“”„]/g, '"')
    .replace(/[–—]/g, '-')
    .replace(/…/g, '...')
    .replace(/[→▸]/g, '>')
    .replace(/[  ]/g, ' ')
    .replace(/[^\u0000-ÿ]/g, '?')
}

const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/

type Rgb = [number, number, number]
const hexToRgb = (hex: string): Rgb => {
  const n = Number.parseInt(hex.replace('#', '').slice(0, 6), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}
const mix = (a: Rgb, b: Rgb, share: number): Rgb => [0, 1, 2].map((i) => Math.round(a[i] * share + b[i] * (1 - share))) as Rgb

/** Los colores del PDF (hoja blanca) son los de la marca de la compañía (Ajustes → Marca): la barra para encabezados y títulos, el acento para el filete; sin marca propia, los de siempre del portal. */
function pdfColors() {
  const pal = brandedPalette('light', getBrand())
  const navy = hexToRgb(pal.brand)
  return { navy, onNavy: hexToRgb(pal.onBrand), flow: hexToRgb(pal.flow), zebra: mix(navy, [255, 255, 255], 0.05) }
}

/** Texto de fecha AAAA-MM-DD → Date a mediodía local (Excel la guarda como fecha). */
const dayToDate = (s: string) => {
  const [y, m, d] = s.split('-').map(Number)
  return new Date(y, m - 1, d, 12)
}

interface Raster {
  dataUrl: string
  width: number
  height: number
}

/** Rasteriza una imagen (SVG incluido) a PNG con su proporción, dentro de `maxW`×`maxH` px. null si no se puede (sin DOM, error o 3 s sin cargar). */
export function rasterize(src: string, maxW = 480, maxH = 160, timeoutMs = 3000): Promise<Raster | null> {
  if (typeof document === 'undefined' || typeof Image === 'undefined') return Promise.resolve(null)
  return new Promise((resolve) => {
    const img = new Image()
    const timer = setTimeout(() => resolve(null), timeoutMs)
    img.onload = () => {
      clearTimeout(timer)
      try {
        const nw = img.naturalWidth || 300 // un SVG sin tamaño propio no da medidas: se le supone 3:1
        const nh = img.naturalHeight || 100
        const scale = Math.min(maxW / nw, maxH / nh)
        const width = Math.max(1, Math.round(nw * scale))
        const height = Math.max(1, Math.round(nh * scale))
        const canvas = document.createElement('canvas')
        canvas.width = width
        canvas.height = height
        const ctx = canvas.getContext('2d')
        if (!ctx) return resolve(null)
        ctx.drawImage(img, 0, 0, width, height)
        resolve({ dataUrl: canvas.toDataURL('image/png'), width, height })
      } catch {
        resolve(null)
      }
    }
    img.onerror = () => {
      clearTimeout(timer)
      resolve(null)
    }
    img.src = src
  })
}

/** El logo del encabezado ya rasterizado: el de la compañía o el de Teikem; null si ninguno se pudo leer (el archivo sale igual, sin logo). */
async function headingLogo(): Promise<(Raster & { plate: 'navy' | null }) | null> {
  const source = await loadPdfLogoSource()
  if (!source) return null
  const r = await rasterize(source.src)
  return r ? { ...r, plate: source.plate } : null
}

/** Mide el logo dentro de una caja `maxW`×`maxH` (pt) sin deformarlo. */
const fit = (logo: Pick<Raster, 'width' | 'height'>, maxW: number, maxH: number) => {
  const scale = Math.min(maxW / logo.width, maxH / logo.height)
  return { w: logo.width * scale, h: logo.height * scale }
}

export async function saveXlsx(data: ExportData, fileName: string, heading: ExportHeading): Promise<void> {
  const XLSX = await import('xlsx')
  const aoa: (string | number | Date | null)[][] = []
  if (heading.company?.trim()) aoa.push([heading.company.trim()])
  aoa.push([heading.title])
  aoa.push([generatedText(heading.generatedAt, heading.lang)])
  if (heading.filters?.trim()) aoa.push([heading.filters.trim()])
  aoa.push([])
  const headerRow = aoa.length
  aoa.push(data.headers)
  for (const r of data.rows) aoa.push(r.map((v) => (typeof v === 'string' && ISO_DAY.test(v) ? dayToDate(v) : v)))
  const ws = XLSX.utils.aoa_to_sheet(aoa, { cellDates: true, dateNF: 'yyyy-mm-dd' })
  // anchos por el contenido (tope 50), encabezados con filtro automático
  ws['!cols'] = data.headers.map((h, i) => ({ wch: Math.min(50, Math.max(h.length, 8, ...data.rows.map((r) => (r[i] === null ? 0 : String(r[i]).length))) + 2) }))
  if (data.headers.length > 0 && data.rows.length > 0) ws['!autofilter'] = { ref: XLSX.utils.encode_range({ s: { r: headerRow, c: 0 }, e: { r: headerRow + data.rows.length, c: data.headers.length - 1 } }) }
  const wb = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(wb, ws, heading.title.replace(/[\\/?*[\]:]/g, ' ').slice(0, 31) || 'Hoja1')
  XLSX.writeFile(wb, fileName, { compression: true })
}

export async function savePdf(data: ExportData, fileName: string, heading: ExportHeading): Promise<void> {
  const [{ jsPDF }, { autoTable }, logo] = await Promise.all([import('jspdf'), import('jspdf-autotable'), headingLogo()])
  const doc = new jsPDF({ orientation: data.headers.length > 5 ? 'landscape' : 'portrait', unit: 'pt', format: 'a4', compress: true })
  const margin = 32
  const pageWidth = doc.internal.pageSize.getWidth()
  const colors = pdfColors()
  let y = margin - 6
  if (logo) {
    const { w, h } = fit(logo, Math.min(150, pageWidth - margin * 2), 34)
    if (logo.plate) {
      doc.setFillColor(...colors.navy)
      doc.rect(margin - 4, y + 2, w + 8, h + 4, 'F')
    }
    doc.addImage(logo.dataUrl, 'PNG', margin, y + 4, w, h)
    y += h + 12
  }
  const lines: { text: string; size: number; bold: boolean; muted: boolean }[] = []
  if (heading.company?.trim()) lines.push({ text: heading.company.trim(), size: 11, bold: true, muted: false })
  lines.push({ text: heading.title, size: 14, bold: true, muted: false })
  lines.push({ text: generatedText(heading.generatedAt, heading.lang), size: 8, bold: false, muted: true })
  if (heading.filters?.trim()) lines.push({ text: heading.filters.trim(), size: 8, bold: false, muted: true })
  for (const l of lines) {
    y += l.size + 3
    doc.setFont('helvetica', l.bold ? 'bold' : 'normal')
    doc.setFontSize(l.size)
    if (l.muted) doc.setTextColor(91, 104, 128)
    else if (l.size >= 14) doc.setTextColor(...colors.navy)   // el título, en el color de la compañía
    else doc.setTextColor(24, 33, 54)
    doc.text(pdfSafeText(l.text), margin, y, { maxWidth: pageWidth - margin * 2 })
  }
  // filete con el acento de la compañía bajo el encabezado
  doc.setDrawColor(...colors.flow)
  doc.setLineWidth(1.4)
  doc.line(margin, y + 5, pageWidth - margin, y + 5)
  y += 4
  const columnStyles: Record<number, { halign: 'right' }> = {}
  data.numeric.forEach((n, i) => {
    if (n) columnStyles[i] = { halign: 'right' }
  })
  autoTable(doc, {
    head: [data.headers.map(pdfSafeText)],
    body: data.rows.map((r) => r.map((v) => (v === null ? '' : pdfSafeText(String(v))))),
    startY: y + 12,
    margin: { left: margin, right: margin, top: margin, bottom: margin },
    styles: { font: 'helvetica', fontSize: 8, cellPadding: 3, overflow: 'linebreak' },
    headStyles: { fillColor: colors.navy, textColor: colors.onNavy, fontStyle: 'bold' },
    alternateRowStyles: { fillColor: colors.zebra },
    columnStyles,
    didDrawPage: () => {
      const { width, height } = doc.internal.pageSize
      doc.setFont('helvetica', 'normal')
      doc.setFontSize(8)
      doc.setTextColor(91, 104, 128)
      doc.text(String(doc.getNumberOfPages()), width - margin, height - margin / 2, { align: 'right' })
    },
  })
  doc.save(fileName)
}
