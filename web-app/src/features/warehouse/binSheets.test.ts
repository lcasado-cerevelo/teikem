// Lote F15 — flujo de las hojas de posición (sin React): estado de la hoja, alcances (filtro, marcadas en tandas de 200,
// desactualizadas), lectura por tandas de 200 con tope de 500, orden natural, marcar impresas SOLO si el PDF salió (con
// el generatedAtUtc más antiguo y solo las posiciones impresas), cancelar sin marcar, "nada que imprimir" y errores.
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import type { BinSheetSpec } from '../../kernel/ui/binSheetPdf'
import type { BinSheetDto, BinSheetPageDto, BinSheetQuery, BinSheetStateDto } from './api'
import {
  BIN_SHEETS_MAX_BINS,
  BIN_SHEETS_PAGE_SIZE,
  binSheetsSources,
  earliestUtc,
  needsPrinting,
  printBinSheets,
  printedSummary,
  sheetDetails,
  sheetStatusOf,
  staleBinsQuery,
  toSheetBin,
  withoutSheetStatus,
  type BinSheetsFlowDeps,
} from './binSheets'

beforeAll(() => setLang('es'))

const sheet = (id: number, code: string, products = 1): BinSheetDto => ({
  binId: id,
  code,
  zoneCode: 'RSV',
  aisle: '01',
  isActive: true,
  sheetStatus: products > 0 ? 'NEVER_PRINTED' : 'STALE',
  products: Array.from({ length: products }, (_, i) => ({ productPublicId: `p-${id}-${i}`, sku: `SKU-${id}-${i}`, name: `Producto ${i}`, barcode: i % 2 ? `75000${id}${i}` : '' })),
})

/** Servidor falso: `all` hojas, paginadas por skip/take; `generatedAtUtc` distinto por tanda (la primera es la más antigua). */
function fakeServer(all: BinSheetDto[], filter: (q: BinSheetQuery, s: BinSheetDto) => boolean = () => true) {
  const calls: BinSheetQuery[] = []
  const fetchPage = vi.fn(async (q: BinSheetQuery): Promise<BinSheetPageDto> => {
    calls.push(q)
    const matching = all.filter((s) => filter(q, s))
    const skip = q.skip ?? 0
    const take = q.take ?? 50
    if (take > 200) throw new Error('400 take')
    return { total: matching.length, skip, take, staleCount: 0, generatedAtUtc: `2026-10-03T14:0${calls.length}:00.123`, items: matching.slice(skip, skip + take) }
  })
  return { calls, fetchPage }
}

const args = (sources: BinSheetQuery[], includeEmpty = false) => ({
  sources,
  includeEmpty,
  spec: { title: 'Hojas de posición', company: 'Advance Logistics', warehouse: 'ALM-01 · Principal', locale: 'es' },
  t,
  lang: 'es',
})

function deps(server: ReturnType<typeof fakeServer>, over: Partial<BinSheetsFlowDeps> = {}) {
  const download = vi.fn(async (_spec: BinSheetSpec) => undefined)
  const markPrinted = vi.fn(async (ids: number[], _g: string | null): Promise<BinSheetStateDto[]> => ids.map((binId) => ({ binId, sheetStatus: 'CURRENT' })))
  return { download, markPrinted, d: { fetchPage: server.fetchPage, download, markPrinted, ...over } as BinSheetsFlowDeps }
}

describe('binSheets · estado y alcances', () => {
  it('estado de la hoja: los cuatro códigos (sin distinguir mayúsculas); desconocido o ausente → null; cuáles piden imprimir', () => {
    expect(['NEVER_PRINTED', 'stale', 'CURRENT', 'EMPTY', 'OTRO', null].map((s) => sheetStatusOf({ sheetStatus: s }))).toEqual(['NEVER_PRINTED', 'STALE', 'CURRENT', 'EMPTY', null, null])
    expect(['NEVER_PRINTED', 'STALE', 'CURRENT', 'EMPTY'].map((s) => needsPrinting({ sheetStatus: s }))).toEqual([true, true, false, false])
  })

  it('alcances: filtro tal cual; desactualizadas = filtros sin "Hoja" + STALE y NEVER_PRINTED; marcadas en tandas de 200 ids', () => {
    const q = { includeInactive: false, zoneIds: [3], sheetStatus: ['CURRENT'], search: 'A-' }
    expect(binSheetsSources('filter', q, [])).toEqual([q])
    expect(binSheetsSources('stale', q, [])).toEqual([{ includeInactive: false, zoneIds: [3], search: 'A-', sheetStatus: ['STALE', 'NEVER_PRINTED'] }])
    expect(staleBinsQuery(q).sheetStatus).toEqual(['STALE', 'NEVER_PRINTED'])
    expect(withoutSheetStatus(q)).toEqual({ includeInactive: false, zoneIds: [3], search: 'A-' })
    expect(binSheetsSources('filter', null, [])).toEqual([])
    const ids = Array.from({ length: 450 }, (_, i) => 1000 - i)
    const sel = binSheetsSources('selected', null, [...ids, 1000])
    expect(sel.map((s) => s.binIds?.length)).toEqual([200, 200, 50])
    expect(sel[0].binIds?.[0]).toBe(551)
  })

  it('detalle del encabezado: solo lo que existe, e "Inactiva"; productos en el orden del servidor', () => {
    expect(sheetDetails({ zoneCode: 'RSV', aisle: ' 01 ', rack: '', level: null, position: 'P1', isActive: false }, t)).toEqual([
      { label: 'Zona', value: 'RSV' },
      { label: 'Pasillo', value: '01' },
      { label: 'Posición', value: 'P1' },
      { label: 'Inactiva', value: '' },
    ])
    const bin = toSheetBin(sheet(7, 'A-7', 2), t)
    expect(bin).toMatchObject({ code: 'A-7', key: 7, products: [{ sku: 'SKU-7-0', barcode: '' }, { sku: 'SKU-7-1', barcode: '7500071' }] })
    expect(earliestUtc([null, '2026-10-03T14:05:00', '2026-10-03T14:01:00.5', 'x'])).toBe('2026-10-03T14:01:00.5')
    expect(earliestUtc([])).toBeNull()
  })
})

describe('binSheets · flujo de impresión', () => {
  it('lee por tandas de 200 (431 → 3 lecturas), ordena por código natural, descarga y DESPUÉS marca las impresas', async () => {
    const all = Array.from({ length: 431 }, (_, i) => sheet(i + 1, `A-${431 - i}`))
    const server = fakeServer(all)
    const order: string[] = []
    const { d, download, markPrinted } = deps(server)
    download.mockImplementation(async () => {
      order.push('download')
    })
    markPrinted.mockImplementation(async (ids) => {
      order.push('mark')
      return ids.map((binId) => ({ binId }))
    })
    const progress: string[] = []
    const r = await printBinSheets({ ...d, onProgress: (p) => progress.push(p.phase) }, args([{ zoneIds: [3] }]))
    expect(server.calls.map((c) => [c.skip, c.take])).toEqual([
      [0, 200],
      [200, 200],
      [400, 200],
    ])
    expect(server.calls.every((c) => c.zoneIds?.[0] === 3)).toBe(true)
    expect(BIN_SHEETS_PAGE_SIZE).toBe(200)
    expect(order).toEqual(['download', 'mark'])
    const spec = download.mock.calls[0][0]
    expect(spec.bins.slice(0, 3).map((b) => b.code)).toEqual(['A-1', 'A-2', 'A-3'])
    expect(spec.bins[spec.bins.length - 1].code).toBe('A-431')
    // marca exactamente las impresas, en una sola llamada, con el generatedAtUtc más antiguo (el de la 1.ª tanda)
    expect(markPrinted).toHaveBeenCalledTimes(1)
    expect(markPrinted.mock.calls[0][0]).toHaveLength(431)
    expect(markPrinted.mock.calls[0][1]).toBe('2026-10-03T14:01:00.123')
    expect(spec.printedAt.toISOString()).toBe('2026-10-03T14:01:00.123Z')
    expect(r).toMatchObject({ status: 'printed', sheets: 431, bins: 431, omittedEmpty: 0 })
    expect(progress).toEqual(['read', 'read', 'read', 'render', 'mark'])
  })

  it('posiciones vacías: no se imprimen ni se marcan; con includeEmpty sí; todo vacío → "nada" sin PDF ni marca', async () => {
    const all = [sheet(1, 'B-1', 0), sheet(2, 'B-2', 3), sheet(3, 'B-3', 0)]
    const off = deps(fakeServer(all))
    const r = await printBinSheets(off.d, args([{}]))
    expect(r).toMatchObject({ status: 'printed', sheets: 1, bins: 1, omittedEmpty: 2 })
    expect(off.markPrinted.mock.calls[0][0]).toEqual([2])
    const on = deps(fakeServer(all))
    expect(await printBinSheets(on.d, args([{}], true))).toMatchObject({ status: 'printed', sheets: 3, bins: 3 })
    expect(on.markPrinted.mock.calls[0][0]).toEqual([1, 2, 3])
    const none = deps(fakeServer([sheet(1, 'B-1', 0)]))
    expect(await printBinSheets(none.d, args([{}]))).toEqual({ status: 'nothing', bins: 1, omittedEmpty: 1 })
    expect(none.download).not.toHaveBeenCalled()
    expect(none.markPrinted).not.toHaveBeenCalled()
  })

  it('más de 10 productos: varias hojas para una posición, una sola marca por posición', async () => {
    const s = deps(fakeServer([sheet(1, 'C-1', 23), sheet(2, 'C-2', 10)]))
    expect(await printBinSheets(s.d, args([{}]))).toMatchObject({ status: 'printed', sheets: 4, bins: 2 })
    expect(s.markPrinted.mock.calls[0][0]).toEqual([1, 2])
  })

  it('más del tope (500): avisa y no sigue leyendo, ni genera, ni marca', async () => {
    const server = fakeServer(Array.from({ length: BIN_SHEETS_MAX_BINS + 1 }, (_, i) => sheet(i + 1, `D-${i}`)))
    const s = deps(server)
    expect(await printBinSheets(s.d, args([{}]))).toEqual({ status: 'tooMany', total: 501, max: 500 })
    expect(server.calls).toHaveLength(1)
    expect(s.download).not.toHaveBeenCalled()
    expect(s.markPrinted).not.toHaveBeenCalled()
  })

  it('marcadas: una lectura por tanda de ids; posiciones repetidas entre fuentes cuentan una vez', async () => {
    const all = Array.from({ length: 250 }, (_, i) => sheet(i + 1, `E-${i + 1}`))
    const server = fakeServer(all, (q, s) => (q.binIds ? q.binIds.includes(s.binId!) : true))
    const s = deps(server)
    const r = await printBinSheets(s.d, args(binSheetsSources('selected', null, all.map((x) => x.binId!))))
    expect(server.calls.map((c) => c.binIds?.length)).toEqual([200, 50])
    expect(r).toMatchObject({ status: 'printed', bins: 250 })
  })

  it('si el PDF falla, el error sale y no se marca nada', async () => {
    const s = deps(fakeServer([sheet(1, 'F-1')]))
    s.download.mockRejectedValueOnce(new Error('jsPDF'))
    await expect(printBinSheets(s.d, args([{}]))).rejects.toThrow('jsPDF')
    expect(s.markPrinted).not.toHaveBeenCalled()
  })

  it('si falla la lectura, el error sale y no se genera ni se marca nada', async () => {
    const s = deps(fakeServer([sheet(1, 'F-1')]), { fetchPage: vi.fn().mockRejectedValue(new Error('400')) })
    await expect(printBinSheets(s.d, args([{}]))).rejects.toThrow('400')
    expect(s.download).not.toHaveBeenCalled()
    expect(s.markPrinted).not.toHaveBeenCalled()
  })

  it('cancelar durante la lectura: no genera ni marca', async () => {
    const ctrl = new AbortController()
    const server = fakeServer(Array.from({ length: 300 }, (_, i) => sheet(i + 1, `G-${i}`)))
    const s = deps(server, { signal: ctrl.signal })
    const original = server.fetchPage.getMockImplementation()!
    server.fetchPage.mockImplementation(async (q) => {
      const page = await original(q)
      ctrl.abort()
      return page
    })
    expect(await printBinSheets(s.d, args([{}]))).toEqual({ status: 'cancelled' })
    expect(server.calls).toHaveLength(1)
    expect(s.download).not.toHaveBeenCalled()
    expect(s.markPrinted).not.toHaveBeenCalled()
    // la lectura abortada (el fetch lanza) también es "cancelada", no un error
    const ctrl2 = new AbortController()
    const aborting = deps(fakeServer([]), {
      signal: ctrl2.signal,
      fetchPage: vi.fn(async () => {
        ctrl2.abort()
        throw new DOMException('aborted', 'AbortError')
      }),
    })
    expect(await printBinSheets(aborting.d, args([{}]))).toEqual({ status: 'cancelled' })
  })

  it('si "marcar impresas" falla, el PDF ya salió: resultado markFailed con el error', async () => {
    const s = deps(fakeServer([sheet(1, 'H-1')]))
    s.markPrinted.mockRejectedValueOnce(new Error('404'))
    const r = await printBinSheets(s.d, args([{}]))
    expect(r).toMatchObject({ status: 'markFailed', sheets: 1, bins: 1 })
    expect(s.download).toHaveBeenCalledTimes(1)
  })

  it('aviso final: hojas, posiciones, vacías omitidas y productos sin código', () => {
    expect(printedSummary({ status: 'printed', sheets: 3, bins: 2, omittedEmpty: 1, withoutCode: 2, states: [] }, t)).toBe(
      'Se generaron 3 hoja(s) de 2 posición(es); quedaron marcadas como impresas. 1 posición(es) sin productos no se imprimieron. 2 producto(s) sin código de barras legible: vea el aviso al pie de su hoja.',
    )
  })
})
