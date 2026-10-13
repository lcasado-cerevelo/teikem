// Lote F-G1 — imprimir desde una estación: arma el PDF (labels o documento), lo manda por la mejor vía (`printPdf`: QZ Tray directo
// o el navegador de respaldo) y traduce el resultado a un aviso. La lee «Prueba» de esta pantalla; F-G2 la reusa para los labels de
// las órdenes.
import type { jsPDF } from 'jspdf'
import { renderOrderLabelsPdf, type LabelMedia } from '../../kernel/ui/orderLabelPdf'
import type { LabelLayout, LabelTexts, OrderLabelData } from '../../kernel/ui/orderLabelLayout'
import { printPdf, type PrintDeps, type PrintOutcome } from '../../kernel/ui/printPdf'
import { pdfSafeText } from '../../kernel/ui/exportTable'
import { mediaInches, type ResolvedPrinter } from './stationConfig'

type Translate = (key: string, params?: Record<string, string | number>) => string

export const asLabelMedia = (m: string): LabelMedia => (m === '4X4' || m === '4X2' ? m : '4X6')

/** PDF de labels (uno por paquete) con la impresora resuelta. */
export function labelPdfFactory(title: string, printer: ResolvedPrinter, layout: LabelLayout, texts: LabelTexts, labels: readonly OrderLabelData[]) {
  let notFitting = 0
  const make = async (rotateInPdf: boolean): Promise<jsPDF> => {
    const r = await renderOrderLabelsPdf({ title, media: asLabelMedia(printer.mediaSize), rotate: rotateInPdf && printer.rotate, layout, texts, labels })
    notFitting = r.notFitting
    return r.doc
  }
  return { make, notFitting: () => notFitting }
}

/** Documento de prueba en carta (la impresora de documentos). */
export async function testDocumentPdf(t: Translate, company: string, printedAt: string): Promise<jsPDF> {
  const { jsPDF: JsPdf } = await import('jspdf')
  const doc = new JsPdf({ unit: 'pt', format: 'letter', orientation: 'portrait' })
  doc.setProperties({ title: pdfSafeText(t('printing.test.docTitle')), creator: 'Teikem' })
  doc.setTextColor(0, 0, 0)
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(20)
  doc.text(pdfSafeText(t('printing.test.docTitle')), 54, 72)
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(12)
  const lines = [company, printedAt, '', t('printing.test.docBody')].map(pdfSafeText)
  lines.forEach((l, i) => doc.text(l, 54, 104 + i * 18))
  doc.setLineWidth(1)
  doc.rect(36, 36, 612 - 72, 792 - 72)
  return doc
}

/** Imprime con la impresora resuelta (QZ Tray directo si corresponde; si no, el navegador). */
export function printWith(printer: ResolvedPrinter, make: (rotateInPdf: boolean) => Promise<jsPDF>, fileName: string, deps?: PrintDeps): Promise<PrintOutcome> {
  const { widthIn, heightIn } = mediaInches(printer.mediaSize)
  return printPdf(make, fileName, { connection: printer.connection, systemName: printer.systemName, widthIn, heightIn, rotate: printer.rotate }, deps)
}

/** Aviso del resultado: tono y texto. */
export function outcomeMessage(o: PrintOutcome, t: Translate): { tone: 'success' | 'info'; text: string } {
  if (o.via === 'qz') return { tone: 'success', text: t('printing.outcome.qz', { printer: o.printer }) }
  const why = o.fallback ? t(`printing.outcome.${o.fallback}`, { detail: o.detail ?? '' }) + ' ' : ''
  const how = o.via === 'browser' ? t('printing.outcome.browser') : t('printing.outcome.download')
  return { tone: o.fallback ? 'info' : 'success', text: why + how }
}
