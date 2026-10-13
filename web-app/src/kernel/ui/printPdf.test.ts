// Lote F-G1 — imprimir: directo con QZ Tray cuando la impresora es QZ_TRAY y QZ Tray está conectado; si no, el navegador (o la
// descarga en teléfonos), con el motivo para avisar. El PDF para QZ va sin /Rotate (lo gira QZ), el del navegador con él.
import type { jsPDF } from 'jspdf'
import { describe, expect, it, vi } from 'vitest'
import { pdfBase64, printPdf, isMobileBrowser, type PrintTarget } from './printPdf'
import { qzConfigOptions, type QzClient } from './qzTray'

const fakeDoc = () => ({ output: vi.fn((kind: string) => (kind === 'datauristring' ? 'data:application/pdf;filename=x;base64,QUJD' : '')), save: vi.fn() }) as unknown as jsPDF

function qz(state: Awaited<ReturnType<QzClient['connect']>>, fail = false): QzClient & { printed: unknown[] } {
  const printed: unknown[] = []
  return {
    printed,
    connect: vi.fn(async () => state),
    findPrinters: vi.fn(async () => ['ZDesigner ZD420']),
    printPdf: vi.fn(async (...args: unknown[]) => {
      if (fail) throw new Error('impresora sin papel')
      printed.push(args)
    }),
  }
}

const QZ_TARGET: PrintTarget = { connection: 'QZ_TRAY', systemName: 'ZDesigner ZD420', widthIn: 4, heightIn: 6, rotate: true }

describe('printPdf', () => {
  it('QZ Tray conectado: directo a la impresora del sistema, sin /Rotate en el PDF', async () => {
    const make = vi.fn(async () => fakeDoc())
    const client = qz('connected')
    const browserPrint = vi.fn(async () => true)
    const r = await printPdf(make, 'x.pdf', QZ_TARGET, { qz: client, browserPrint, isMobile: () => false })
    expect(r).toEqual({ via: 'qz', printer: 'ZDesigner ZD420' })
    expect(make).toHaveBeenCalledWith(false)
    expect(client.printed).toEqual([['ZDesigner ZD420', 'QUJD', { widthIn: 4, heightIn: 6, rotate: true }]])
    expect(browserPrint).not.toHaveBeenCalled()
  })

  it('QZ Tray no abierto o sin librería: cae al navegador con el motivo', async () => {
    const browserPrint = vi.fn(async () => true)
    expect(await printPdf(async () => fakeDoc(), 'x.pdf', QZ_TARGET, { qz: qz('notRunning'), browserPrint, isMobile: () => false })).toEqual({
      via: 'browser',
      fallback: 'qzNotRunning',
      detail: undefined,
    })
    const r = await printPdf(async () => fakeDoc(), 'x.pdf', QZ_TARGET, { qz: qz('noLibrary'), browserPrint, isMobile: () => false })
    expect(r).toMatchObject({ via: 'browser', fallback: 'qzNoLibrary' })
  })

  it('QZ Tray falla al imprimir: navegador con el detalle; nunca se rompe', async () => {
    const make = vi.fn(async () => fakeDoc())
    const r = await printPdf(make, 'x.pdf', QZ_TARGET, { qz: qz('connected', true), browserPrint: async () => true, isMobile: () => false })
    expect(r).toEqual({ via: 'browser', fallback: 'qzFailed', detail: 'impresora sin papel' })
    expect(make).toHaveBeenLastCalledWith(true)
  })

  it('BROWSER no toca QZ Tray; en un teléfono (o si el navegador no imprime) se descarga el PDF', async () => {
    const client = qz('connected')
    const doc = fakeDoc()
    const target: PrintTarget = { ...QZ_TARGET, connection: 'BROWSER' }
    expect(await printPdf(async () => doc, 'x.pdf', target, { qz: client, browserPrint: async () => true, isMobile: () => false })).toEqual({ via: 'browser' })
    expect(client.connect).not.toHaveBeenCalled()
    expect(await printPdf(async () => doc, 'x.pdf', target, { qz: client, browserPrint: async () => true, isMobile: () => true })).toEqual({ via: 'download' })
    expect(await printPdf(async () => doc, 'y.pdf', target, { qz: client, browserPrint: async () => false, isMobile: () => false })).toEqual({ via: 'download' })
    expect(doc.save).toHaveBeenCalledWith('y.pdf')
  })

  it('utilidades: base64 sin prefijo, teléfonos y opciones de QZ Tray del tamaño exacto', () => {
    expect(pdfBase64(fakeDoc())).toBe('QUJD')
    expect(isMobileBrowser('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0)')).toBe(true)
    expect(isMobileBrowser('Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130')).toBe(false)
    expect(qzConfigOptions({ widthIn: 4, heightIn: 2, rotate: true })).toEqual({ size: { width: 4, height: 2 }, units: 'in', margins: 0, scaleContent: false, rotation: 90, copies: 1 })
  })
})
