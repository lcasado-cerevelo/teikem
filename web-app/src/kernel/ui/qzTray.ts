// Lote F-G1 — impresión DIRECTA a las impresoras de la computadora con QZ Tray (gratis, código abierto; se instala en cada PC y la
// página le habla por websocket a localhost). El servidor nunca se conecta a QZ Tray ni a una impresora: solo firma (certificado
// propio de la plataforma, GET /printing/qz/certificate y POST /printing/qz/sign) para que QZ Tray no pregunte «¿Permitir?».
// La librería oficial (qz-tray.js, LGPL) NO se descarga de internet: se sirve desde `/vendor/qz-tray.js` (web-app/public/vendor) si
// soporte la copió ahí (manual 12, «QZ Tray»). Sin la librería o sin QZ Tray abierto, el estado es 'noLibrary' / 'notRunning' y la
// impresión cae al PDF del navegador con aviso (`printPdf.ts`).
// Todo pasa por la interfaz `QzClient`: las pruebas ponen un cliente falso con `setQzClient`.
import { api, unwrap } from '../api/client'

export type QzState = 'connected' | 'noLibrary' | 'notRunning'

export interface QzPrintOptions {
  /** Medidas de la etiqueta o del papel (pulgadas). */
  widthIn: number
  heightIn: number
  /** «Girar 90°» de la impresora. */
  rotate: boolean
  copies?: number
}

export interface QzClient {
  /** Carga la librería y abre la conexión con QZ Tray (si ya estaba abierta, no hace nada). */
  connect(): Promise<QzState>
  /** Nombres exactos de las impresoras instaladas en esta computadora. */
  findPrinters(): Promise<string[]>
  /** Imprime un PDF (base64, sin el prefijo data:) en la impresora del sistema con ese nombre. */
  printPdf(printer: string, base64: string, options: QzPrintOptions): Promise<void>
}

/** Dónde se sirve la librería oficial (no se descarga de internet). */
export const QZ_SCRIPT_URL = '/vendor/qz-tray.js'
/** Página oficial de descarga de QZ Tray (enlace «Instalar QZ Tray» de la pantalla). */
export const QZ_DOWNLOAD_URL = 'https://qz.io/download/'
/** Algoritmo de firma que usa el servidor (QzSigningService). */
export const QZ_SIGNATURE_ALGORITHM = 'SHA512'

type Resolver<T> = (value?: T) => void

/** Lo que usa Teikem de la API pública de qz-tray.js 2.x. */
export interface QzApi {
  websocket: { isActive(): boolean; connect(options?: { retries?: number; delay?: number }): Promise<void> }
  printers: { find(query?: string): Promise<string | string[]> }
  configs: { create(printer: string, options?: Record<string, unknown>): unknown }
  print(config: unknown, data: unknown[]): Promise<void>
  security: {
    setCertificatePromise(fn: (resolve: Resolver<string>, reject: (e: unknown) => void) => void): void
    setSignatureAlgorithm?(algorithm: string): void
    setSignaturePromise(fn: (toSign: string) => (resolve: Resolver<string>, reject: (e: unknown) => void) => void): void
  }
}

/** Certificado y firma (del servidor); null = sin configurar (QZ Tray preguntará «Permitir»). */
export interface QzSecurity {
  certificate(): Promise<string | null>
  sign(text: string): Promise<string | null>
}

let libraryPromise: Promise<QzApi | null> | null = null

/** Carga `/vendor/qz-tray.js` una sola vez; null si no está (404) o no expone `window.qz`. */
export function loadQzLibrary(url = QZ_SCRIPT_URL, timeoutMs = 8000): Promise<QzApi | null> {
  const w = window as unknown as { qz?: QzApi }
  if (w.qz) return Promise.resolve(w.qz)
  if (libraryPromise) return libraryPromise
  libraryPromise = new Promise<QzApi | null>((resolve) => {
    const s = document.createElement('script')
    s.src = url
    s.async = true
    const done = (ok: boolean) => {
      clearTimeout(timer)
      resolve(ok ? (w.qz ?? null) : null)
      if (!ok || !w.qz) libraryPromise = null   // se puede volver a intentar después de copiar la librería
    }
    const timer = setTimeout(() => done(false), timeoutMs)
    s.onload = () => done(true)
    s.onerror = () => done(false)
    document.head.appendChild(s)
  })
  return libraryPromise
}

/** Seguridad con el API de Teikem: certificado y firma del servidor; cualquier error = sin firma (QZ Tray pregunta). */
export const apiQzSecurity: QzSecurity = {
  certificate: async () => {
    try {
      return (await unwrap(api.GET('/api/v1/printing/qz/certificate'))).certificate ?? null
    } catch {
      return null
    }
  },
  sign: async (text) => {
    try {
      return (await unwrap(api.POST('/api/v1/printing/qz/sign', { body: { request: text } }))).signature ?? null
    } catch {
      return null
    }
  },
}

/** Opciones de impresión de QZ Tray para un PDF del tamaño exacto de la etiqueta. */
export function qzConfigOptions(o: QzPrintOptions): Record<string, unknown> {
  return {
    size: { width: o.widthIn, height: o.heightIn },
    units: 'in',
    margins: 0,
    scaleContent: false,
    rotation: o.rotate ? 90 : 0,
    copies: o.copies ?? 1,
  }
}

/** Cliente real sobre qz-tray.js (la seguridad se configura una vez por librería cargada). */
export function createQzClient(security: QzSecurity = apiQzSecurity, loader: () => Promise<QzApi | null> = loadQzLibrary): QzClient {
  let configured: QzApi | null = null
  const lib = async (): Promise<QzApi | null> => {
    const qz = await loader()
    if (qz && configured !== qz) {
      qz.security.setCertificatePromise((resolve) => {
        void security.certificate().then((c) => resolve(c ?? undefined))
      })
      qz.security.setSignatureAlgorithm?.(QZ_SIGNATURE_ALGORITHM)
      qz.security.setSignaturePromise((toSign) => (resolve) => {
        void security.sign(toSign).then((s) => resolve(s ?? undefined))
      })
      configured = qz
    }
    return qz
  }
  const ensure = async (): Promise<QzApi> => {
    const state = await client.connect()
    if (state !== 'connected' || !configured) throw new Error(state)
    return configured
  }
  const client: QzClient = {
    async connect() {
      const qz = await lib()
      if (!qz) return 'noLibrary'
      if (qz.websocket.isActive()) return 'connected'
      try {
        await qz.websocket.connect({ retries: 0, delay: 0 })
        return 'connected'
      } catch {
        return 'notRunning'
      }
    },
    async findPrinters() {
      const qz = await ensure()
      const found = await qz.printers.find()
      return (Array.isArray(found) ? found : [found]).filter(Boolean).sort((a, b) => a.localeCompare(b))
    },
    async printPdf(printer, base64, options) {
      const qz = await ensure()
      const config = qz.configs.create(printer, qzConfigOptions(options))
      await qz.print(config, [{ type: 'pixel', format: 'pdf', flavor: 'base64', data: base64 }])
    },
  }
  return client
}

let current: QzClient | null = null

/** El cliente de QZ Tray de la aplicación (real, salvo que una prueba ponga uno falso). */
export function getQzClient(): QzClient {
  current ??= createQzClient()
  return current
}

/** Pruebas: cambia el cliente (null = vuelve al real). */
export function setQzClient(client: QzClient | null) {
  current = client
}
