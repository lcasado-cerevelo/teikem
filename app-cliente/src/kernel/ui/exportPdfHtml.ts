// El PDF de una tabla del portal en la app instalada (2026-10-24): se arma como una página HTML (logo, compañía, título, «Generado el …», filtros y la tabla con los colores de la compañía) y el aparato la
// convierte a PDF (`expo-print`). La web arma el suyo con jsPDF (exportFiles.web.ts); los dos llevan lo mismo. Lo puro (el HTML) se prueba sin aparato.
import { rgbCss, type Rgb } from './exportColors'
import { generatedText, type ExportData, type ExportHeading } from './tableExport'

export const escapeHtml = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

export interface PdfHtmlOptions {
  colors: { navy: Rgb; onNavy: Rgb; flow: Rgb; zebra: Rgb }
  /** Logo como data URI (PNG o JPG); null = sin logo. */
  logo: string | null
  /** El logo es de letras claras y va sobre una placa del color de la compañía. */
  plate: boolean
}

/** Horizontal con más de 5 columnas, como el PDF de la web. */
export const isLandscape = (data: ExportData) => data.headers.length > 5

/** Tamaño de la hoja A4 en puntos (el aparato lo pide en px de 72 por pulgada): ancho y alto. */
export const pageSize = (data: ExportData) => (isLandscape(data) ? { width: 842, height: 595 } : { width: 595, height: 842 })

export function buildPdfHtml(data: ExportData, heading: ExportHeading, o: PdfHtmlOptions): string {
  const { navy, onNavy, flow, zebra } = o.colors
  const cell = (v: string | number | null, numeric: boolean, tag: 'td' | 'th') => `<${tag}${numeric ? ' class="n"' : ''}>${v === null ? '' : escapeHtml(String(v))}</${tag}>`
  const head = `<tr>${data.headers.map((h, i) => cell(h, data.numeric[i], 'th')).join('')}</tr>`
  const body = data.rows.map((r) => `<tr>${r.map((v, i) => cell(v, data.numeric[i], 'td')).join('')}</tr>`).join('\n')
  const lines: string[] = []
  if (heading.company?.trim()) lines.push(`<div class="company">${escapeHtml(heading.company.trim())}</div>`)
  lines.push(`<h1>${escapeHtml(heading.title)}</h1>`)
  lines.push(`<div class="muted">${escapeHtml(generatedText(heading.generatedAt, heading.lang))}</div>`)
  if (heading.filters?.trim()) lines.push(`<div class="muted">${escapeHtml(heading.filters.trim())}</div>`)
  const logo = o.logo ? `<div class="logo${o.plate ? ' plate' : ''}"><img src="${o.logo}" /></div>` : ''
  return `<!doctype html>
<html lang="${heading.lang}"><head><meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<style>
  @page { size: A4 ${isLandscape(data) ? 'landscape' : 'portrait'}; margin: 0; }
  body { font-family: Helvetica, Arial, sans-serif; color: #182136; margin: 0; padding: 32px; font-size: 9px; }
  .logo { margin-bottom: 8px; }
  .logo img { max-height: 34px; max-width: 150px; }
  .logo.plate { display: inline-block; background: ${rgbCss(navy)}; padding: 3px 4px; }
  .company { font-size: 11px; font-weight: bold; margin-top: 4px; }
  h1 { font-size: 14px; margin: 2px 0 3px; color: ${rgbCss(navy)}; }
  .muted { color: #5b6880; font-size: 8px; margin-top: 2px; }
  .rule { border: 0; border-top: 1.4px solid ${rgbCss(flow)}; margin: 8px 0 12px; }
  table { width: 100%; border-collapse: collapse; }
  thead { display: table-header-group; }
  tr { page-break-inside: avoid; }
  th { background: ${rgbCss(navy)}; color: ${rgbCss(onNavy)}; text-align: left; padding: 3px; font-size: 8px; }
  td { padding: 3px; border-bottom: 0.5px solid #dfe3ec; vertical-align: top; }
  tbody tr:nth-child(even) td { background: ${rgbCss(zebra)}; }
  .n { text-align: right; }
</style></head>
<body>
${logo}
${lines.join('\n')}
<hr class="rule" />
<table><thead>${head}</thead><tbody>
${body}
</tbody></table>
</body></html>`
}
