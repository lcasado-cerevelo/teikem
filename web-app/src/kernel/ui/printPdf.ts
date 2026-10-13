// Lote F-G1 — mandar un PDF a imprimir desde el navegador.
// - Impresora con conexión QZ_TRAY y QZ Tray conectado: DIRECTO a la impresora del sistema (por su nombre exacto), sin diálogo.
// - Si no (BROWSER, o QZ Tray no instalado / no abierto / falla): respaldo con el PDF y el diálogo de impresión del navegador en un
//   iframe oculto (con Chrome `--kiosk-printing` sale sin diálogo a la impresora predeterminada de Windows). Nunca se rompe: el
//   resultado dice por dónde salió y por qué cayó al respaldo, para avisar.
// - En teléfonos y tabletas (donde el iframe no imprime) se descarga el PDF.
// El servidor nunca se conecta a una impresora.
import type { jsPDF } from 'jspdf'
import { getQzClient, type QzClient } from './qzTray'

export interface PrintTarget {
  /** 'BROWSER' | 'QZ_TRAY' (dbo.Printer.ConnectionType). */
  connection: string
  /** Nombre exacto de la impresora en esta computadora (QZ Tray); null = solo navegador. */
  systemName: string | null
  widthIn: number
  heightIn: number
  rotate: boolean
}

export type PrintOutcome =
  | { via: 'qz'; printer: string }
  | { via: 'browser'; fallback?: 'qzNoLibrary' | 'qzNotRunning' | 'qzFailed'; detail?: string }
  | { via: 'download'; fallback?: 'qzNoLibrary' | 'qzNotRunning' | 'qzFailed'; detail?: string }

/** El PDF ya armado; `rotateInPdf` = poner el giro en la página (navegador) o dejarlo a QZ Tray. */
export type PdfFactory = (rotateInPdf: boolean) => Promise<jsPDF>

export interface PrintDeps {
  qz?: QzClient
  /** Imprime en el navegador (por defecto, iframe oculto). Devuelve false si no pudo (se descarga). */
  browserPrint?: (doc: jsPDF, fileName: string) => Promise<boolean>
  isMobile?: () => boolean
}

/** ¿Teléfono o tableta? (el diálogo de impresión desde un iframe no funciona ahí). */
export function isMobileBrowser(ua: string = typeof navigator === 'undefined' ? '' : navigator.userAgent): boolean {
  return /Android|iPhone|iPad|iPod|Mobile/i.test(ua)
}

/** PDF a base64 sin el prefijo `data:` (lo que pide QZ Tray). */
export function pdfBase64(doc: jsPDF): string {
  const uri = doc.output('datauristring')
  const i = uri.indexOf('base64,')
  return i >= 0 ? uri.slice(i + 'base64,'.length) : uri
}

/** Diálogo de impresión del navegador con el PDF en un iframe oculto; false si el navegador no lo permite. */
export async function iframePrint(doc: jsPDF, _fileName: string): Promise<boolean> {
  const blob = doc.output('blob')
  const url = URL.createObjectURL(blob)
  const frame = document.createElement('iframe')
  frame.setAttribute('aria-hidden', 'true')
  frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden'
  frame.src = url
  document.body.appendChild(frame)
  const cleanup = () => {
    frame.remove()
    URL.revokeObjectURL(url)
  }
  return new Promise<boolean>((resolve) => {
    const timer = setTimeout(() => {
      cleanup()
      resolve(false)
    }, 15_000)
    frame.onload = () => {
      clearTimeout(timer)
      try {
        frame.contentWindow?.focus()
        frame.contentWindow?.print()
        // se deja un rato para que el diálogo termine de leer el documento
        setTimeout(cleanup, 60_000)
        resolve(true)
      } catch {
        cleanup()
        resolve(false)
      }
    }
  })
}

/**
 * Imprime por la mejor vía disponible (ver arriba). `make(rotateInPdf)` arma el PDF: para QZ Tray sin giro en la página (QZ lo gira),
 * para el navegador con el giro en la página.
 */
export async function printPdf(make: PdfFactory, fileName: string, target: PrintTarget, deps: PrintDeps = {}): Promise<PrintOutcome> {
  let fallback: 'qzNoLibrary' | 'qzNotRunning' | 'qzFailed' | undefined
  let detail: string | undefined
  if (target.connection === 'QZ_TRAY' && target.systemName) {
    const qz = deps.qz ?? getQzClient()
    const state = await qz.connect()
    if (state === 'connected') {
      try {
        const doc = await make(false)
        await qz.printPdf(target.systemName, pdfBase64(doc), { widthIn: target.widthIn, heightIn: target.heightIn, rotate: target.rotate })
        return { via: 'qz', printer: target.systemName }
      } catch (e) {
        fallback = 'qzFailed'
        detail = e instanceof Error ? e.message : String(e)
      }
    } else fallback = state === 'noLibrary' ? 'qzNoLibrary' : 'qzNotRunning'
  }
  const doc = await make(true)
  const mobile = (deps.isMobile ?? isMobileBrowser)()
  if (!mobile && (await (deps.browserPrint ?? iframePrint)(doc, fileName))) return { via: 'browser', fallback, detail }
  doc.save(fileName)
  return { via: 'download', fallback, detail }
}
