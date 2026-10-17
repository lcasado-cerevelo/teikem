// Lee los códigos de barras de las hojas escaneadas (Lote 85, P8): PDF (cada página) e imágenes (PNG, JPG…), TODO en el navegador. Las hojas no se suben al servidor: solo los códigos leídos van a
// POST /cod/reconcile. Las dos librerías (pdf.js y zxing-wasm) se cargan SOLO al leer el primer archivo, para no pesar en el resto de la aplicación, y sus archivos (el worker de pdf.js y el .wasm de
// zxing) salen del propio sitio, no de una red de distribución.
import type { ReadResult } from 'zxing-wasm/reader'

/** Un código leído de una hoja: dónde (archivo y página) y de qué tipo de código de barras. */
export interface ScannedCode {
  code: string
  source: string
  format: string
}

export interface FileReadResult {
  fileName: string
  /** Páginas leídas (1 en una imagen). */
  pages: number
  codes: ScannedCode[]
  /** Páginas donde no se encontró ningún código. */
  emptyPages: number[]
  /** El archivo no se pudo abrir (tipo no soportado, PDF dañado…); el resto de los archivos sigue. */
  error: string | null
}

export type ReadProgress = (done: number, total: number) => void

/** Lo que se acepta como hoja escaneada. */
export const ACCEPTED_TYPES = 'application/pdf,image/png,image/jpeg,image/webp,image/bmp,image/gif,image/tiff'

export function fileKind(file: { name: string; type: string }): 'pdf' | 'image' | null {
  const name = file.name.toLowerCase()
  if (file.type === 'application/pdf' || name.endsWith('.pdf')) return 'pdf'
  if (file.type.startsWith('image/') || /\.(png|jpe?g|webp|bmp|gif|tiff?)$/.test(name)) return 'image'
  return null
}

/** Lo que dice un código leído tal cual lo devuelve el lector: sin espacios ni caracteres de control; vacío = no sirve. */
export function cleanCode(text: string): string {
  // eslint-disable-next-line no-control-regex
  return text.replace(/[\u0000-\u001f\u007f]/g, '').trim()
}

type Zxing = typeof import('zxing-wasm/reader')
let zxingReady: Promise<Zxing> | null = null

function loadZxing(): Promise<Zxing> {
  zxingReady ??= (async () => {
    const mod = await import('zxing-wasm/reader')
    const wasm = (await import('zxing-wasm/reader/zxing_reader.wasm?url')).default
    mod.prepareZXingModule({
      overrides: { locateFile: (path: string, prefix: string) => (path.endsWith('.wasm') ? wasm : prefix + path) },
      fireImmediately: true,
    })
    return mod
  })()
  return zxingReady
}

const READER_OPTIONS = { tryHarder: true, tryRotate: true, tryInvert: true, tryDownscale: true, maxNumberOfSymbols: 20 } as const

async function decode(zxing: Zxing, image: ImageData): Promise<ReadResult[]> {
  return (await zxing.readBarcodes(image, READER_OPTIONS)).filter((r) => r.isValid && cleanCode(r.text) !== '')
}

/** Dibuja la imagen en un lienzo (con tope de tamaño) y devuelve sus píxeles. */
async function imageData(file: File, maxSide = 4200): Promise<ImageData> {
  const bitmap = await createImageBitmap(file)
  try {
    const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.max(1, Math.round(bitmap.width * scale))
    canvas.height = Math.max(1, Math.round(bitmap.height * scale))
    const ctx = canvas.getContext('2d', { willReadFrequently: true })
    if (!ctx) throw new Error('canvas')
    ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    return ctx.getImageData(0, 0, canvas.width, canvas.height)
  } finally {
    bitmap.close()
  }
}

function toCodes(results: ReadResult[], source: string): ScannedCode[] {
  const seen = new Set<string>()
  const out: ScannedCode[] = []
  for (const r of results) {
    const code = cleanCode(r.text)
    if (seen.has(code)) continue   // el mismo código leído dos veces en la misma página
    seen.add(code)
    out.push({ code, source, format: r.format })
  }
  return out
}

async function readImage(file: File, zxing: Zxing): Promise<FileReadResult> {
  const results = await decode(zxing, await imageData(file))
  const codes = toCodes(results, file.name)
  return { fileName: file.name, pages: 1, codes, emptyPages: codes.length === 0 ? [1] : [], error: null }
}

/** Escalas con que se dibuja cada página del PDF: la normal y, si no se encontró nada, una más grande (códigos pequeños). */
const PDF_SCALES = [2, 3.5]

async function readPdf(file: File, zxing: Zxing, onProgress?: ReadProgress): Promise<FileReadResult> {
  const pdfjs = await import('pdfjs-dist')
  pdfjs.GlobalWorkerOptions.workerSrc = (await import('pdfjs-dist/build/pdf.worker.min.mjs?url')).default
  const task = pdfjs.getDocument({ data: new Uint8Array(await file.arrayBuffer()) })
  const doc = await task.promise
  const codes: ScannedCode[] = []
  const emptyPages: number[] = []
  try {
    for (let n = 1; n <= doc.numPages; n++) {
      const page = await doc.getPage(n)
      let found: ScannedCode[] = []
      for (const scale of PDF_SCALES) {
        const viewport = page.getViewport({ scale })
        const canvas = document.createElement('canvas')
        canvas.width = Math.ceil(viewport.width)
        canvas.height = Math.ceil(viewport.height)
        const ctx = canvas.getContext('2d', { willReadFrequently: true })
        if (!ctx) throw new Error('canvas')
        await page.render({ canvas, canvasContext: ctx, viewport }).promise
        found = toCodes(await decode(zxing, ctx.getImageData(0, 0, canvas.width, canvas.height)), `${file.name} p${n}`)
        canvas.width = 0
        if (found.length > 0) break
      }
      if (found.length === 0) emptyPages.push(n)
      codes.push(...found)
      page.cleanup()
      onProgress?.(n, doc.numPages)
    }
  } finally {
    void task.destroy()
  }
  return { fileName: file.name, pages: doc.numPages, codes, emptyPages, error: null }
}

/** Lee los códigos de un archivo. Un archivo que no se puede abrir devuelve `error` (no lanza): el resto de los archivos sigue. */
export async function readCodesFromFile(file: File, onProgress?: ReadProgress): Promise<FileReadResult> {
  const kind = fileKind(file)
  if (kind === null) return { fileName: file.name, pages: 0, codes: [], emptyPages: [], error: 'type' }
  try {
    const zxing = await loadZxing()
    return kind === 'pdf' ? await readPdf(file, zxing, onProgress) : await readImage(file, zxing)
  } catch {
    return { fileName: file.name, pages: 0, codes: [], emptyPages: [], error: 'open' }
  }
}
