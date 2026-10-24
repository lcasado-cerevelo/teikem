// Excel y PDF de las tablas del portal en la APP INSTALADA (celulares y tabletas; 2026-10-24, pedido de Luis: «el estándar es CSV, PDF y Excel», también ahí). El archivo se guarda en la carpeta temporal y se
// abre el menú de compartir. Excel: SheetJS arma el libro (igual que en la web, `exportWorkbook.ts`). PDF: se arma una página HTML con el logo y los colores de la compañía (`exportPdfHtml.ts`) y `expo-print` la
// convierte a PDF. La web tiene los suyos en exportFiles.web.ts (jsPDF y XLSX se bajan directo).
import { Asset } from 'expo-asset'
import * as Print from 'expo-print'

import { loadPdfLogoSource } from '../brand/portalBrand'
import { pdfColors } from './exportColors'
import { buildPdfHtml, pageSize } from './exportPdfHtml'
import { buildWorkbook } from './exportWorkbook'
import { readBase64, shareFile, sharePdfAt } from './shareFile'
import type { ExportData, ExportHeading } from './tableExport'

export async function saveXlsx(data: ExportData, fileName: string, heading: ExportHeading): Promise<void> {
  const XLSX = require('xlsx') as typeof import('xlsx') // se carga cuando se pide un Excel, no al abrir la app
  const wb = buildWorkbook(XLSX, data, heading)
  const bytes = new Uint8Array(XLSX.write(wb, { type: 'array', bookType: 'xlsx', compression: true }) as ArrayBuffer)
  await shareFile(fileName, bytes, 'xlsx', heading.title)
}

/** El logo del encabezado como data URI: el de la compañía (ya viene así) o, si no subió ninguno, el de Teikem (un archivo del paquete). null si ninguno se pudo leer: el PDF sale igual, sin logo. */
async function headingLogo(): Promise<{ uri: string; plate: boolean } | null> {
  try {
    const source = await loadPdfLogoSource()
    if (!source) return null
    if (source.src.startsWith('data:')) return { uri: source.src, plate: source.plate === 'navy' }
    const asset = Asset.fromModule(require('../../../assets/teikem-pdf-logo.png'))
    await asset.downloadAsync()
    const b64 = asset.localUri ? await readBase64(asset.localUri) : null
    return b64 ? { uri: `data:image/png;base64,${b64}`, plate: false } : null
  } catch {
    return null
  }
}

export async function savePdf(data: ExportData, fileName: string, heading: ExportHeading): Promise<void> {
  const logo = await headingLogo()
  const html = buildPdfHtml(data, heading, { colors: pdfColors(), logo: logo?.uri ?? null, plate: logo?.plate ?? false })
  const { width, height } = pageSize(data)
  const printed = await Print.printToFileAsync({ html, width, height })
  await sharePdfAt(printed.uri, fileName, heading.title)
}
