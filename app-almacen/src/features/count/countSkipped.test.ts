// Lote A7 — aviso persistente de un lote de conteo parcial: lectura de `skippedLines`, guardado en el kv del aparato y descarte.
import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../../kernel/db/database'
import { dismissSkippedNotice, listSkippedNotices, parseSkippedLines, recordSkippedFromResult } from './countSkipped'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
})

const SKIPPED = {
  lineId: 9,
  binCode: 'A-01',
  sku: 'SKU-B',
  lotNumber: null,
  sentQty: 6,
  currentQty: 5,
  reasonCode: 'CORRECTED_BY_SUPERVISOR',
  message: 'La línea ya fue corregida por el supervisor; no se puede volver a capturar.',
}
const PATH = '/api/v1/cycle-counts/300/lines/batch'

describe('parseSkippedLines', () => {
  it('lee las líneas omitidas con lo que se mandó y lo que dejó el supervisor', () => {
    expect(parseSkippedLines({ lines: [], skippedLines: [SKIPPED, { ...SKIPPED, lineId: 10, sku: 'SKU-C', lotNumber: 'L-1', currentQty: null }] })).toEqual([
      { lineId: 9, sku: 'SKU-B', binCode: 'A-01', lotNumber: null, sentQty: 6, currentQty: 5 },
      { lineId: 10, sku: 'SKU-C', binCode: 'A-01', lotNumber: 'L-1', sentQty: 6, currentQty: null },
    ])
  })

  it('sin skippedLines, vacío, nulo o con formas raras: no hay nada', () => {
    expect(parseSkippedLines({ lines: [] })).toEqual([])
    expect(parseSkippedLines({ skippedLines: null })).toEqual([])
    expect(parseSkippedLines({ skippedLines: [] })).toEqual([])
    expect(parseSkippedLines(null)).toEqual([])
    expect(parseSkippedLines('x')).toEqual([])
    expect(parseSkippedLines({ skippedLines: 'x' })).toEqual([])
    expect(parseSkippedLines({ skippedLines: [null, 3, { sku: '' }, {}] })).toEqual([])
  })

  it('no toma ninguna cantidad esperada aunque el servidor la mandara', () => {
    const [line] = parseSkippedLines({ skippedLines: [{ ...SKIPPED, systemQty: 99 }] })
    expect(Object.keys(line).sort()).toEqual(['binCode', 'currentQty', 'lineId', 'lotNumber', 'sentQty', 'sku'])
  })
})

describe('avisos persistentes', () => {
  it('con skippedLines guarda un aviso con el id del conteo; sin ellas no guarda nada', () => {
    expect(recordSkippedFromResult(1, PATH, { lines: [] })).toBeNull()
    expect(recordSkippedFromResult(2, PATH, { lines: [], skippedLines: null })).toBeNull()
    expect(listSkippedNotices()).toEqual([])

    const n = recordSkippedFromResult(3, PATH, { skippedLines: [SKIPPED] }, new Date('2026-10-03T15:00:00Z'))
    expect(n).toMatchObject({ outboxId: 3, countId: 300, createdAtUtc: '2026-10-03T15:00:00.000Z' })
    expect(listSkippedNotices()).toEqual([n])
  })

  it('sobrevive al cierre de la app (se relee de la base) y no duplica por la misma fila de la cola', () => {
    recordSkippedFromResult(3, PATH, { skippedLines: [SKIPPED] })
    recordSkippedFromResult(3, PATH, { skippedLines: [SKIPPED] })
    recordSkippedFromResult(4, '/api/v1/cycle-counts/301/lines/batch', { skippedLines: [{ ...SKIPPED, sku: 'SKU-Z' }] })
    expect(listSkippedNotices().map((x) => [x.outboxId, x.countId])).toEqual([
      [3, 300],
      [4, 301],
    ])
  })

  it('descartar quita solo ese aviso', () => {
    const a = recordSkippedFromResult(3, PATH, { skippedLines: [SKIPPED] })!
    recordSkippedFromResult(4, PATH, { skippedLines: [SKIPPED] })
    dismissSkippedNotice(a.id)
    expect(listSkippedNotices().map((x) => x.outboxId)).toEqual([4])
    dismissSkippedNotice('no-existe')
    expect(listSkippedNotices()).toHaveLength(1)
  })

  it('ruta que no es de un lote: el aviso se guarda sin id de conteo', () => {
    expect(recordSkippedFromResult(5, '/otra', { skippedLines: [SKIPPED] })?.countId).toBeNull()
  })
})
