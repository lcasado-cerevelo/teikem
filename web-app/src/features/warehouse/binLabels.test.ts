// Lote F16 — flujo de las etiquetas de posición con dependencias falsas: alcances (filtro actual con todos sus filtros y
// marcadas en tandas de 200), lectura por tandas de 200 con skip, tope de 500 (sin generar), orden natural, datos de cada
// etiqueta, cancelar, errores, avisos de las que salen sin código y que NUNCA se marca nada (no hay "marcar impresas").
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import { planBinLabels, type BinLabelSpec } from '../../kernel/ui/binLabelPdf'
import type { WarehouseBinDto } from './api'
import {
  BIN_LABELS_MAX,
  BIN_LABELS_PAGE_SIZE,
  BIN_LABELS_PERMISSION,
  binLabelsSources,
  labelDetails,
  parseLabelOrientation,
  parseLabelSize,
  printBinLabels,
  printedLabelsSummary,
  toLabelBin,
  type BinLabelsFlowDeps,
  type BinPage,
} from './binLabels'
import type { BinListQuery } from './locations'

beforeAll(() => setLang('es'))

const bin = (id: number, code: string, extra: Partial<WarehouseBinDto> = {}): WarehouseBinDto =>
  ({ id, code, zoneId: 1, zoneCode: 'RSV', isActive: true, qtyOnHand: 0, productCount: 0, isProvisional: false, ...extra }) as WarehouseBinDto

/** Listado falso: `all` paginado por skip/take; filtra por binIds si vienen. */
function fakeList(all: WarehouseBinDto[]) {
  const calls: BinListQuery[] = []
  const fetchPage = vi.fn(async (q: BinListQuery): Promise<BinPage> => {
    calls.push(q)
    const pool = q.binIds ? all.filter((b) => q.binIds!.includes(b.id as number)) : all
    const skip = q.skip ?? 0
    return { total: pool.length, items: pool.slice(skip, skip + (q.take ?? 100)) }
  })
  return { calls, fetchPage }
}

const spec = { title: 'Etiquetas de posición', company: 'Advance Logistics', locale: 'es', size: '4x2', orientation: 'auto' } as const

function deps(fetchPage: BinLabelsFlowDeps['fetchPage'], extra: Partial<BinLabelsFlowDeps> = {}) {
  const download = vi.fn(async (s: BinLabelSpec) => planBinLabels(s.bins, s.size, s.orientation))
  return { ...extra, fetchPage, download }
}

describe('binLabels · alcances y datos', () => {
  it('permiso: el del reporte de códigos de barras de posiciones', () => {
    expect(BIN_LABELS_PERMISSION).toBe('inventory.view')
  })

  it('filtro actual: la consulta de la tabla tal cual (todos sus filtros y el buscador); marcadas: tandas de 200 ids', () => {
    const q: BinListQuery = { includeInactive: false, zoneIds: [3], occupancy: ['FULL'], productPublicIds: ['p'], search: 'A-0' }
    expect(binLabelsSources('filter', q, [1, 2])).toEqual([q])
    expect(binLabelsSources('filter', null, [1, 2])).toEqual([])
    const ids = Array.from({ length: 450 }, (_, i) => 450 - i)
    const parts = binLabelsSources('selected', q, [...ids, 5, 5])
    expect(parts.map((p) => p.binIds?.length)).toEqual([200, 200, 50])
    expect(parts[0].binIds?.[0]).toBe(1)
    expect(parts[0]).toEqual({ binIds: parts[0].binIds })
  })

  it('datos: almacén primero y luego zona, pasillo, rack, nivel y posición, solo los que tienen valor', () => {
    expect(labelDetails({ zoneCode: 'RSV', aisle: '01', rack: ' ', level: null, position: '4', isActive: true }, 'ALM-01', t)).toEqual([
      { label: 'Almacén', value: 'ALM-01' },
      { label: 'Zona', value: 'RSV' },
      { label: 'Pasillo', value: '01' },
      { label: 'Posición', value: '4' },
    ])
    expect(labelDetails({ zoneCode: null, isActive: true }, null, t)).toEqual([])
    expect(toLabelBin(bin(9, 'A-1'), 'ALM-01', t)).toEqual({ code: 'A-1', key: 9, details: [{ label: 'Almacén', value: 'ALM-01' }, { label: 'Zona', value: 'RSV' }] })
  })

  it('tamaño y orientación guardados: los válidos; si no, 4×2 y automática', () => {
    expect(parseLabelSize('4x6')).toBe('4x6')
    expect(parseLabelSize('8x4')).toBe('4x2')
    expect(parseLabelSize(null)).toBe('4x2')
    expect(parseLabelOrientation('rotate')).toBe('rotate')
    expect(parseLabelOrientation('x')).toBe('auto')
  })
})

describe('binLabels · flujo', () => {
  it('lee por tandas de 200 con skip (431 = 3 lecturas), ordena natural y descarga una etiqueta por posición; no marca nada', async () => {
    const all = Array.from({ length: 431 }, (_, i) => bin(i + 1, `A-${431 - i}`))
    const { calls, fetchPage } = fakeList(all)
    const d = deps(fetchPage)
    const progress: unknown[] = []
    const r = await printBinLabels({ ...d, onProgress: (p) => progress.push(p) }, { sources: [{ includeInactive: false, search: 'A-' }], spec, warehouseCode: 'ALM-01', t, lang: 'es' })
    expect(r).toEqual({ status: 'printed', labels: 431, withoutCode: 0, notices: [] })
    expect(calls.map((c) => [c.skip, c.take])).toEqual([
      [0, BIN_LABELS_PAGE_SIZE],
      [200, BIN_LABELS_PAGE_SIZE],
      [400, BIN_LABELS_PAGE_SIZE],
    ])
    // los filtros de la tabla van en cada tanda
    expect(calls.every((c) => c.search === 'A-' && c.includeInactive === false)).toBe(true)
    expect(d.download).toHaveBeenCalledTimes(1)
    const sent = d.download.mock.calls[0][0]
    expect(sent).toMatchObject({ size: '4x2', orientation: 'auto', title: 'Etiquetas de posición', company: 'Advance Logistics' })
    expect(sent.bins.slice(0, 3).map((b) => b.code)).toEqual(['A-1', 'A-2', 'A-3'])
    expect(sent.bins[9].code).toBe('A-10')
    expect(sent.bins[0].details?.[0]).toEqual({ label: 'Almacén', value: 'ALM-01' })
    expect(progress.at(-1)).toEqual({ phase: 'render', labels: 431 })
    // las dependencias no tienen (ni piden) "marcar impresas"
    expect(Object.keys(d)).toEqual(['fetchPage', 'download'])
  })

  it('marcadas: una lectura por tanda de ids; sin repetir', async () => {
    const all = Array.from({ length: 300 }, (_, i) => bin(i + 1, `B-${i + 1}`))
    const { calls, fetchPage } = fakeList(all)
    const d = deps(fetchPage)
    const ids = Array.from({ length: 250 }, (_, i) => i + 1)
    const r = await printBinLabels(d, { sources: binLabelsSources('selected', null, ids), spec, t, lang: 'es' })
    expect(r).toMatchObject({ status: 'printed', labels: 250 })
    expect(calls).toHaveLength(2)
    expect(calls.map((c) => c.binIds?.length)).toEqual([200, 50])
    expect(calls.every((c) => c.skip === 0 && c.take === 200)).toBe(true)
  })

  it('más de 500: avisa tras la primera lectura y no genera nada', async () => {
    const { calls, fetchPage } = fakeList(Array.from({ length: BIN_LABELS_MAX + 1 }, (_, i) => bin(i + 1, `C-${i}`)))
    const d = deps(fetchPage)
    expect(await printBinLabels(d, { sources: [{}], spec, t, lang: 'es' })).toEqual({ status: 'tooMany', total: 501, max: 500 })
    expect(calls).toHaveLength(1)
    expect(d.download).not.toHaveBeenCalled()
    // exactamente 500 sí
    const ok = fakeList(Array.from({ length: BIN_LABELS_MAX }, (_, i) => bin(i + 1, `C-${i}`)))
    expect(await printBinLabels(deps(ok.fetchPage), { sources: [{}], spec, t, lang: 'es' })).toMatchObject({ status: 'printed', labels: 500 })
  })

  it('sin posiciones: "nada" sin PDF', async () => {
    const d = deps(fakeList([]).fetchPage)
    expect(await printBinLabels(d, { sources: [{}], spec, t, lang: 'es' })).toEqual({ status: 'nothing' })
    expect(await printBinLabels(d, { sources: [], spec, t, lang: 'es' })).toEqual({ status: 'nothing' })
    expect(d.download).not.toHaveBeenCalled()
  })

  it('cancelar mientras lee: no genera; un error al leer o al generar se propaga', async () => {
    const ctrl = new AbortController()
    const list = fakeList(Array.from({ length: 300 }, (_, i) => bin(i + 1, `D-${i}`)))
    const fetchPage = vi.fn(async (q: BinListQuery) => {
      const page = await list.fetchPage(q)
      ctrl.abort()
      return page
    })
    const d = deps(fetchPage, { signal: ctrl.signal })
    expect(await printBinLabels(d, { sources: [{}], spec, t, lang: 'es' })).toEqual({ status: 'cancelled' })
    expect(d.download).not.toHaveBeenCalled()

    const failing = deps(vi.fn(async () => Promise.reject(new Error('caído'))))
    await expect(printBinLabels(failing, { sources: [{}], spec, t, lang: 'es' })).rejects.toThrow('caído')
    const badPdf = deps(fakeList([bin(1, 'E-1')]).fetchPage)
    badPdf.download.mockRejectedValueOnce(new Error('sin memoria'))
    await expect(printBinLabels(badPdf, { sources: [{}], spec, t, lang: 'es' })).rejects.toThrow('sin memoria')
  })

  it('las que no llevan código de barras salen igual (en texto) y se avisan al final con los textos de siempre', async () => {
    const long = 'ZONA-B-PASILLO-12-RACK-07-NIVEL-03-POSICION-04'
    const d = deps(fakeList([bin(1, 'F-1'), bin(2, long), bin(3, 'AÑO')]).fetchPage)
    const r = await printBinLabels(d, { sources: [{}], spec, t, lang: 'es' })
    expect(r.status).toBe('printed')
    if (r.status !== 'printed') return
    expect(r.labels).toBe(3)
    expect(r.withoutCode).toBe(2)
    expect(r.notices).toHaveLength(2)
    expect(r.notices[0]).toContain(long)
    expect(r.notices[1]).toContain('AÑO')
    expect(printedLabelsSummary(r, '4x2', t)).toBe('Se generaron 3 etiquetas de 4 × 2 pulgadas. 2 salieron sin código de barras (solo con el código en texto):')
    expect(printedLabelsSummary({ status: 'printed', labels: 1, withoutCode: 0, notices: [] }, '4x6', t)).toBe('Se generó 1 etiqueta de 4 × 6 pulgadas.')
  })
})
