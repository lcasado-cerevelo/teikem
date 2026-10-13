import { describe, expect, it } from 'vitest'
import type { WarehousePulseDays } from './api'
import { warehouseDayCards, warehouseDayHref, warehouseDayRange, WAREHOUSE_DAY_CARDS } from './warehouseDay'

const DATES = ['2026-09-24', '2026-09-25', '2026-09-26', '2026-09-27', '2026-09-28', '2026-09-29', '2026-09-30']

/** DTO del API con 7 días (el último es hoy) y lo que cada prueba cambie. */
function dto(over: Partial<WarehousePulseDays> = {}): WarehousePulseDays {
  return {
    timeZone: 'America/Puerto_Rico',
    today: '2026-09-30',
    days: DATES.map((date, i) => ({ date, receivedUnits: i, outboundUnits: i === 6 ? 5 : 0, countsWithVariance: i === 6 ? 1 : 0, transferMovements: i, adjustmentMovements: i === 6 ? 4 : 0 })),
    receivedToday: 6,
    receivedTotal: 21,
    outboundToday: 5,
    outboundTotal: 5,
    countsWithVarianceToday: 1,
    countsWithVarianceTotal: 1,
    belowMinProducts: 2,
    countsAlert: true,
    belowMinAlert: true,
    transferToday: 6,
    transferTotal: 21,
    adjustmentToday: 4,
    adjustmentTotal: 4,
    ...over,
  }
}

describe('warehouseDay (Lote 15, franja "Almacén hoy")', () => {
  it('5 tarjetas en orden (la 4.ª es «Transferencias y ajustes»); número = hoy, texto = total de 7 días; 7 barritas y ninguna en bajo mínimo', () => {
    const cards = warehouseDayCards(dto(), null)
    expect(cards.map((c) => c.key)).toEqual([...WAREHOUSE_DAY_CARDS])
    expect(cards.map((c) => c.key)).toEqual(['received', 'outbound', 'countsVariance', 'moves', 'belowMin'])
    expect(cards.map((c) => c.today)).toEqual([6, 5, 1, 10, 2])   // transferencias 6 + ajustes 4 hoy
    expect(cards.map((c) => c.total)).toEqual([21, 5, 1, 25, null])
    expect(cards.map((c) => c.points?.length ?? null)).toEqual([7, 7, 7, null, null])
    expect(cards[0].points).toEqual(DATES.map((date, i) => ({ date, value: i })))
    // la última barrita es hoy
    expect(cards[1].points?.at(-1)).toEqual({ date: '2026-09-30', value: 5 })
  })

  it('naranja: lo decide el servidor (conteos si hoy hubo alguno; bajo mínimo si hay); nunca en recibido ni salida', () => {
    expect(warehouseDayCards(dto(), null).map((c) => c.alert)).toEqual([false, false, true, false, true])
    expect(warehouseDayCards(dto({ countsAlert: false, belowMinAlert: false }), null).map((c) => c.alert)).toEqual([false, false, false, false, false])
  })

  it('sin datos (cargando o 403): números nulos, sin barritas ni naranja', () => {
    const cards = warehouseDayCards(undefined, null)
    expect(cards.map((c) => c.today)).toEqual([null, null, null, null, null])
    expect(cards.every((c) => c.points === null && c.pairs === null && c.split === null && !c.alert)).toBe(true)
  })

  it('un día sin movimiento vale 0 (barrita vacía)', () => {
    const cards = warehouseDayCards(dto({ days: [{ date: '2026-09-30' }] }), null)
    expect(cards[0].points).toEqual([{ date: '2026-09-30', value: 0 }])
  })

  it('ventana: del primer día a hoy', () => {
    expect(warehouseDayRange(dto())).toEqual({ from: '2026-09-24', to: '2026-09-30' })
    expect(warehouseDayRange(undefined)).toEqual({ from: '', to: '' })
  })

  it('enlaces (D4): Kárdex Recepción / Despacho + Cruce con los 7 días; Conteo en Diferencia; Productos bajo mínimo; con el almacén', () => {
    const range = { from: '2026-09-24', to: '2026-09-30' }
    expect(warehouseDayHref('received', range, null)).toBe('/warehouse/kardex?types=RECEIPT&from=2026-09-24&to=2026-09-30')
    expect(warehouseDayHref('outbound', range, 'wh-1')).toBe(
      '/warehouse/kardex?types=ISSUE&types=CROSSDOCK&from=2026-09-24&to=2026-09-30&warehousePublicIds=wh-1',
    )
    expect(warehouseDayHref('countsVariance', range, 'wh-1')).toBe('/warehouse/cycle-counts?status=RECONCILED_VARIANCE&warehousePublicIds=wh-1')
    expect(warehouseDayHref('belowMin', range, null)).toBe('/warehouse/products?kpi=low')
    expect(warehouseDayCards(dto(), 'wh-2').map((c) => c.href)).toEqual([
      '/warehouse/kardex?types=RECEIPT&from=2026-09-24&to=2026-09-30&warehousePublicIds=wh-2',
      '/warehouse/kardex?types=ISSUE&types=CROSSDOCK&from=2026-09-24&to=2026-09-30&warehousePublicIds=wh-2',
      '/warehouse/cycle-counts?status=RECONCILED_VARIANCE&warehousePublicIds=wh-2',
      '/warehouse/transfers-adjustments',   // esa pantalla no toma fechas ni almacén por la URL
      '/warehouse/products?kpi=low&warehousePublicIds=wh-2',
    ])
  })

  it('«Transferencias y ajustes»: dos series por día (una barra de cada tipo) y la cifra de cada uno; hoy y 7 días', () => {
    const moves = warehouseDayCards(dto(), null)[3]
    expect(moves.pairs).toHaveLength(7)
    expect(moves.pairs?.[0]).toEqual({ date: '2026-09-24', transfers: 0, adjustments: 0 })
    expect(moves.pairs?.at(-1)).toEqual({ date: '2026-09-30', transfers: 6, adjustments: 4 })   // la última pareja es hoy
    expect(moves.split).toEqual({ transfersToday: 6, adjustmentsToday: 4, transfersTotal: 21, adjustmentsTotal: 4 })
    // un API viejo sin los campos nuevos: ceros, sin romper
    const old = warehouseDayCards(dto({ transferToday: undefined, adjustmentToday: undefined, transferTotal: undefined, adjustmentTotal: undefined, days: [{ date: '2026-09-30' }] }), null)[3]
    expect(old.today).toBe(0)
    expect(old.pairs).toEqual([{ date: '2026-09-30', transfers: 0, adjustments: 0 }])
  })
})
