// Lote 14 (P8) — lógica pura de Conteo cíclico en dos paneles (countView.ts).
import { describe, expect, it } from 'vitest'
import type { CycleCountLineDto } from './api'
import {
  confirmBlocker,
  countFilterQuery,
  countListQuery,
  countParam,
  countWhere,
  EMPTY_COUNT_FILTERS,
  isCountClosed,
  isCountEditable,
  keepZones,
  lineVariance,
  matchCountLine,
  pendingLines,
  scanOptions,
  selectedCountId,
  utcFromZonedInput,
  zonedInputFromUtc,
} from './countView'

const LINES: CycleCountLineDto[] = [
  { id: 1, sku: 'A-1', productName: 'Tornillo', binId: 10, binCode: 'A-01', barcode: '7500001', systemQty: 5, countedQty: null },
  { id: 2, sku: 'A-1', productName: 'Tornillo', binId: 11, binCode: 'A-02', lotNumber: 'L-77', systemQty: 2, countedQty: 2, varianceQty: 0 },
  { id: 3, sku: 'S-9', productName: 'Serie', binId: 10, binCode: 'A-01', trackingTypeCode: 'SERIAL', expectedSerials: ['SN-001', 'SN-002'], systemQty: 2 },
]

describe('countView', () => {
  it('estatus: Concordancia y Diferencia son finales; Pendiente y Contado se capturan y se confirman', () => {
    expect(isCountClosed('RECONCILED')).toBe(true)
    expect(isCountClosed('RECONCILED_VARIANCE')).toBe(true)
    expect(isCountEditable('OPEN')).toBe(true)
    expect(isCountEditable('COUNTED')).toBe(true)
    expect(isCountEditable('RECONCILED_VARIANCE')).toBe(false)
    expect(isCountEditable(null)).toBe(false)
  })

  it('filtros → consulta de /cycle-counts/page (vacíos fuera; zonas y posiciones como números; página)', () => {
    expect(countFilterQuery(EMPTY_COUNT_FILTERS)).toEqual({
      warehousePublicIds: undefined,
      zoneIds: undefined,
      binIds: undefined,
      productPublicIds: undefined,
      status: undefined,
      origins: undefined,
      from: undefined,
      to: undefined,
      search: undefined,
    })
    const q = countListQuery(
      {
        ...EMPTY_COUNT_FILTERS,
        warehousePublicIds: ['W'],
        zoneIds: ['3', 'x'],
        bins: [{ id: 10, label: 'A-01' }],
        products: [{ publicId: 'P', sku: 'A-1', label: 'A-1 · Tornillo' }],
        status: ['OPEN'],
        origins: ['CHANGES'],
        created: { from: '2026-09-01', to: '' },
        search: '  CC-1 ',
      },
      3,
      25,
    )
    expect(q).toMatchObject({ warehousePublicIds: ['W'], zoneIds: [3], binIds: [10], productPublicIds: ['P'], status: ['OPEN'], origins: ['CHANGES'], from: '2026-09-01', search: 'CC-1', skip: 50, take: 25 })
    expect(q.to).toBeUndefined()
    expect(keepZones(['1', '2'], ['2', '3'])).toEqual(['2'])
  })

  it('?count=: id entero positivo; sin él, el primero de la lista', () => {
    expect(countParam(new URLSearchParams('count=27'))).toBe(27)
    expect(countParam(new URLSearchParams('count=abc'))).toBeNull()
    expect(countParam(new URLSearchParams('count=0'))).toBeNull()
    expect(selectedCountId([{ id: 5 }, { id: 6 }], null)).toBe(5)
    expect(selectedCountId([{ id: 5 }], 99)).toBe(99)
    expect(selectedCountId([], null)).toBeNull()
  })

  it('cómo se describe un conteo: su posición, "N posiciones" o nada', () => {
    expect(countWhere({ binCode: 'A-01', zoneCode: 'A', binCount: 1 })).toEqual({ kind: 'bin', code: 'A-01', zone: 'A' })
    expect(countWhere({ binCode: null, binCount: 4 })).toEqual({ kind: 'many', bins: 4 })
    expect(countWhere({ binCount: 0 })).toEqual({ kind: 'none' })
  })

  it('escáner: coincidencia EXACTA por SKU, código de barras, lote o serie (sin mayúsculas); cada línea una vez', () => {
    expect(matchCountLine(LINES, 'a-1').map((m) => [m.line.id, m.by])).toEqual([
      [1, 'sku'],
      [2, 'sku'],
    ])
    expect(matchCountLine(LINES, ' 7500001 ').map((m) => [m.line.id, m.by])).toEqual([[1, 'barcode']])
    expect(matchCountLine(LINES, 'l-77').map((m) => [m.line.id, m.by])).toEqual([[2, 'lot']])
    expect(matchCountLine(LINES, 'sn-002')).toEqual([{ line: LINES[2], by: 'serial', serial: 'SN-002' }])
    expect(matchCountLine(LINES, 'A-')).toEqual([])
    expect(matchCountLine(LINES, '')).toEqual([])
    expect(scanOptions(LINES)[1]).toEqual({ value: '2', label: 'A-1 · Tornillo', hint: 'A-02 · L-77' })
  })

  it('varianza de la línea: con lo tecleado si es número; si no, la del servidor', () => {
    expect(lineVariance(LINES[0], '7')).toBe(2)
    expect(lineVariance(LINES[0], '4,5')).toBe(-0.5)
    expect(lineVariance(LINES[0], 'x')).toBeNull()
    expect(lineVariance(LINES[0])).toBeNull()
    expect(lineVariance(LINES[1])).toBe(0)
    expect(lineVariance({ systemQty: 3, countedQty: 1 })).toBe(-2)
  })

  it('Confirmar: faltan líneas (lo tecleado válido cuenta), sin líneas, cerrado o a ciegas', () => {
    const drafts = new Map<number, string>()
    expect(pendingLines(LINES, drafts)).toBe(2)
    expect(confirmBlocker({ statusCode: 'OPEN', isBlind: false, lines: LINES, drafts })).toEqual({ key: 'pending', params: { n: 2 } })
    const typed = new Map([
      [1, '5'],
      [3, 'nada'],
    ])
    expect(pendingLines(LINES, typed)).toBe(1)
    expect(confirmBlocker({ statusCode: 'COUNTED', isBlind: false, lines: LINES.slice(0, 2), drafts: new Map([[1, '0']]) })).toBeNull()
    expect(confirmBlocker({ statusCode: 'OPEN', isBlind: false, lines: [], drafts })).toEqual({ key: 'noLines' })
    expect(confirmBlocker({ statusCode: 'RECONCILED', isBlind: false, lines: LINES, drafts })).toEqual({ key: 'closed' })
    expect(confirmBlocker({ statusCode: 'OPEN', isBlind: true, lines: LINES, drafts })).toEqual({ key: 'blind' })
  })

  it('ventana de lo cambiado en hora de Puerto Rico (UTC−4): ida y vuelta', () => {
    expect(zonedInputFromUtc('2026-09-30T04:00:00Z')).toBe('2026-09-30T00:00')
    // sin zona = UTC (como el API)
    expect(zonedInputFromUtc('2026-09-30T03:59:00')).toBe('2026-09-29T23:59')
    expect(zonedInputFromUtc(null)).toBe('')
    expect(utcFromZonedInput('2026-09-30T00:00')).toBe('2026-09-30T04:00:00.000Z')
    expect(utcFromZonedInput('2026-01-15T20:30')).toBe('2026-01-16T00:30:00.000Z')
    expect(utcFromZonedInput('')).toBeNull()
    expect(utcFromZonedInput('30/09/2026')).toBeNull()
  })
})
