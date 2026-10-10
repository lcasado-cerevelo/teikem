// Informe "Productos por posición" (sin React): alcances (filtro, marcadas en tandas de 200), lectura por tandas de 200 con
// SIN tope, orden natural, reporte con el formato de «Códigos de barras» (un grupo por posición, una celda por producto), cancelar sin
// descargar, "nada que imprimir" y errores.
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import type { BarcodeReportSpec } from '../../kernel/ui/barcodeReportPdf'
import type { BinProductsDto, BinProductsPageDto, BinProductsQuery } from './api'
import {
  BIN_PRODUCTS_NOTICE,
  BIN_PRODUCTS_PAGE_SIZE,
  binDetails,
  binDetailsText,
  buildBinProductsReport,
  productReportRow,
  binProductsSources,
  earliestUtc,
  printBinProducts,
  printedSummary,
  type BinProductsFlowDeps,
} from './binProducts'

beforeAll(() => setLang('es'))

const bin = (id: number, code: string, products = 1): BinProductsDto => ({
  binId: id,
  code,
  zoneCode: 'RSV',
  aisle: '01',
  isActive: true,
  products: Array.from({ length: products }, (_, i) => ({ productPublicId: `p-${id}-${i}`, sku: `SKU-${id}-${i}`, name: `Producto ${i}`, barcode: i % 2 ? `75000${id}${i}` : '' })),
})

/** Servidor falso: `all` posiciones, paginadas por skip/take; `generatedAtUtc` distinto por tanda (la primera es la más antigua). */
function fakeServer(all: BinProductsDto[], filter: (q: BinProductsQuery, s: BinProductsDto) => boolean = () => true) {
  const calls: BinProductsQuery[] = []
  const fetchPage = vi.fn(async (q: BinProductsQuery): Promise<BinProductsPageDto> => {
    calls.push(q)
    const matching = all.filter((s) => filter(q, s))
    const skip = q.skip ?? 0
    const take = q.take ?? 50
    if (take > 200) throw new Error('400 take')
    return { total: matching.length, skip, take, generatedAtUtc: `2026-10-03T14:0${calls.length}:00.123`, items: matching.slice(skip, skip + take) }
  })
  return { calls, fetchPage }
}

const args = (sources: BinProductsQuery[], includeEmpty = false) => ({
  sources,
  includeEmpty,
  spec: { title: 'Productos por posición', company: 'Advance Logistics', user: 'Luis', locale: 'es', filters: [{ label: 'Almacén', value: 'ALM-01 · Principal' }] },
  t,
  lang: 'es',
})

function deps(server: ReturnType<typeof fakeServer>, over: Partial<BinProductsFlowDeps> = {}) {
  const download = vi.fn(async (_spec: BarcodeReportSpec) => undefined)
  return { download, d: { fetchPage: server.fetchPage, download, ...over } as BinProductsFlowDeps }
}

describe('binProducts · alcances y datos', () => {
  it('alcances: filtro tal cual; marcadas en tandas de 200 ids sin repetir; filtros imposibles → nada', () => {
    const q = { includeInactive: false, zoneIds: [3], aisle: 'A', search: 'A-' }
    expect(binProductsSources('filter', q, [])).toEqual([q])
    expect(binProductsSources('filter', null, [])).toEqual([])
    const ids = Array.from({ length: 450 }, (_, i) => 1000 - i)
    const sel = binProductsSources('selected', null, [...ids, 1000])
    expect(sel.map((s) => s.binIds?.length)).toEqual([200, 200, 50])
    expect(sel[0].binIds?.[0]).toBe(551)
  })

  it('detalle del encabezado: solo lo que existe, e "Inactiva"; productos en el orden del servidor', () => {
    expect(binDetails({ zoneCode: 'RSV', aisle: ' 01 ', rack: '', level: null, position: 'P1', isActive: false }, t)).toEqual([
      { label: 'Zona', value: 'RSV' },
      { label: 'Pasillo', value: '01' },
      { label: 'Posición', value: 'P1' },
      { label: 'Inactiva', value: '' },
    ])
    expect(binDetailsText({ zoneCode: 'RSV', aisle: ' 01 ', rack: '', level: null, position: 'P1', isActive: false }, t)).toBe('Zona RSV · Pasillo 01 · Posición P1 · Inactiva')
    // la celda de un producto: el código de barras del producto si Code 128 lo admite (si no, el SKU); SKU en negrita y nombre debajo
    const [p0, p1] = bin(7, 'A-7', 2).products!
    expect(productReportRow(p0)).toEqual({ value: 'SKU-7-0', title: 'SKU-7-0', description: 'Producto 0', meta: null })
    expect(productReportRow(p1)).toEqual({ value: '7500071', title: 'SKU-7-1', description: 'Producto 1', meta: '7500071' })
    expect(productReportRow({ ...p1, barcode: 'ÑANDÚ' }).value).toBe('SKU-7-1')
    expect(earliestUtc([null, '2026-10-03T14:05:00', '2026-10-03T14:01:00.5', 'x'])).toBe('2026-10-03T14:01:00.5')
    expect(earliestUtc([])).toBeNull()
  })
})

describe('binProducts · flujo de impresión', () => {
  it('lee por tandas de 200 (431 → 3 lecturas), ordena por código natural y descarga el PDF', async () => {
    const all = Array.from({ length: 431 }, (_, i) => bin(i + 1, `A-${431 - i}`))
    const server = fakeServer(all)
    const { d, download } = deps(server)
    const progress: string[] = []
    const r = await printBinProducts({ ...d, onProgress: (p) => progress.push(p.phase) }, args([{ zoneIds: [3] }]))
    expect(server.calls.map((c) => [c.skip, c.take])).toEqual([
      [0, 200],
      [200, 200],
      [400, 200],
    ])
    expect(server.calls.every((c) => c.zoneIds?.[0] === 3)).toBe(true)
    expect(BIN_PRODUCTS_PAGE_SIZE).toBe(200)
    const spec = download.mock.calls[0][0]
    expect(spec.groups.slice(0, 3).map((g) => g.title.split(' ')[1])).toEqual(['A-1', 'A-2', 'A-3'])
    expect(spec.groups[spec.groups.length - 1].title).toContain('A-431')
    expect(spec.generatedAt?.toISOString()).toBe('2026-10-03T14:01:00.123Z')
    expect(spec).toMatchObject({ title: 'Productos por posición', company: 'Advance Logistics', user: 'Luis', locale: 'es', columns: 'auto' })
    expect(spec.filters).toEqual([{ label: 'Almacén', value: 'ALM-01 · Principal' }])
    expect(r).toMatchObject({ status: 'printed', bins: 431, products: 431, omittedEmpty: 0 })
    expect(progress).toEqual(['read', 'read', 'read', 'render'])
  })

  it('posiciones vacías: no llevan códigos, solo un aviso (nombradas con el interruptor, contadas sin él); todo vacío → "nada" sin PDF', async () => {
    const all = [bin(1, 'B-1', 0), bin(2, 'B-2', 3), bin(3, 'B-3', 0)]
    const off = deps(fakeServer(all))
    expect(await printBinProducts(off.d, args([{}]))).toMatchObject({ status: 'printed', bins: 1, products: 3, omittedEmpty: 2 })
    expect(off.download.mock.calls[0][0].groups).toHaveLength(1)
    expect(off.download.mock.calls[0][0].notices).toEqual(['2 posición(es) sin productos no llevan códigos: B-1, B-3.'])
    const on = deps(fakeServer(all))
    await printBinProducts(on.d, args([{}], true))
    expect(on.download.mock.calls[0][0].notices).toEqual(['Posiciones sin productos (2): B-1, B-3.'])
    const none = deps(fakeServer([bin(1, 'B-1', 0)]))
    expect(await printBinProducts(none.d, args([{}]))).toEqual({ status: 'nothing', bins: 1, omittedEmpty: 1 })
    expect(none.download).not.toHaveBeenCalled()
  })

  it('el título del grupo lleva el código, el detalle de la posición y cuántos productos tiene; cada producto es una celda', () => {
    const { spec, products, withProducts } = buildBinProductsReport([bin(1, 'C-1', 3)], { spec: args([]).spec, t, includeEmpty: false })
    expect(spec.groups[0].title).toBe('Posición C-1 · Zona RSV · Pasillo 01 — 3 producto(s)')
    expect(spec.groups[0].rows.map((r) => r.title)).toEqual(['SKU-1-0', 'SKU-1-1', 'SKU-1-2'])
    expect([products, withProducts]).toEqual([3, 1])
  })

  it('sin tope: más de 500 posiciones también se imprimen (la pantalla solo avisa que el PDF es grande)', async () => {
    const server = fakeServer(Array.from({ length: BIN_PRODUCTS_NOTICE + 1 }, (_, i) => bin(i + 1, `D-${i}`, 1)))
    const s = deps(server)
    expect(await printBinProducts(s.d, args([{}]))).toMatchObject({ status: 'printed', bins: 501 })
    expect(s.download).toHaveBeenCalledTimes(1)
  })

  it('marcadas: una lectura por tanda de ids; posiciones repetidas entre fuentes cuentan una vez', async () => {
    const all = Array.from({ length: 250 }, (_, i) => bin(i + 1, `E-${i + 1}`))
    const server = fakeServer(all, (q, s) => (q.binIds ? q.binIds.includes(s.binId!) : true))
    const s = deps(server)
    const r = await printBinProducts(s.d, args(binProductsSources('selected', null, all.map((x) => x.binId!))))
    expect(server.calls.map((c) => c.binIds?.length)).toEqual([200, 50])
    expect(r).toMatchObject({ status: 'printed', bins: 250 })
  })

  it('si el PDF falla, el error sale', async () => {
    const s = deps(fakeServer([bin(1, 'F-1')]))
    s.download.mockRejectedValueOnce(new Error('jsPDF'))
    await expect(printBinProducts(s.d, args([{}]))).rejects.toThrow('jsPDF')
  })

  it('si falla la lectura, el error sale y no se genera nada', async () => {
    const s = deps(fakeServer([bin(1, 'F-1')]), { fetchPage: vi.fn().mockRejectedValue(new Error('400')) })
    await expect(printBinProducts(s.d, args([{}]))).rejects.toThrow('400')
    expect(s.download).not.toHaveBeenCalled()
  })

  it('cancelar durante la lectura: no genera', async () => {
    const ctrl = new AbortController()
    const server = fakeServer(Array.from({ length: 300 }, (_, i) => bin(i + 1, `G-${i}`)))
    const s = deps(server, { signal: ctrl.signal })
    const original = server.fetchPage.getMockImplementation()!
    server.fetchPage.mockImplementation(async (q) => {
      const page = await original(q)
      ctrl.abort()
      return page
    })
    expect(await printBinProducts(s.d, args([{}]))).toEqual({ status: 'cancelled' })
    expect(server.calls).toHaveLength(1)
    expect(s.download).not.toHaveBeenCalled()
    // la lectura abortada (el fetch lanza) también es "cancelada", no un error
    const ctrl2 = new AbortController()
    const aborting = deps(fakeServer([]), {
      signal: ctrl2.signal,
      fetchPage: vi.fn(async () => {
        ctrl2.abort()
        throw new DOMException('aborted', 'AbortError')
      }),
    })
    expect(await printBinProducts(aborting.d, args([{}]))).toEqual({ status: 'cancelled' })
  })

  it('aviso final: productos, posiciones y vacías omitidas', () => {
    expect(printedSummary({ status: 'printed', bins: 2, products: 7, omittedEmpty: 1 }, t)).toBe(
      'PDF generado: 7 producto(s) en 2 posición(es). 1 posición(es) sin productos no se imprimieron.',
    )
  })
})
