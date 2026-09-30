import { describe, expect, it } from 'vitest'
import type { WarehousePulseDays } from './api'
import { warehouseDayCards, warehouseDayHref, warehouseDayRange, WAREHOUSE_DAY_CARDS } from './warehouseDay'

const DATES = ['2026-09-24', '2026-09-25', '2026-09-26', '2026-09-27', '2026-09-28', '2026-09-29', '2026-09-30']

/** DTO del API con 7 días (el último es hoy) y lo que cada prueba cambie. */
function dto(over: Partial<WarehousePulseDays> = {}): WarehousePulseDays {
  return {
    timeZone: 'America/Puerto_Rico',
    today: '2026-09-30',
    days: DATES.map((date, i) => ({ date, receivedUnits: i, outboundUnits: i === 6 ? 5 : 0, countsWithVariance: i === 6 ? 1 : 0 })),
    receivedToday: 6,
    receivedTotal: 21,
    outboundToday: 5,
    outboundTotal: 5,
    countsWithVarianceToday: 1,
    countsWithVarianceTotal: 1,
    belowMinProducts: 2,
    countsAlert: true,
    belowMinAlert: true,
    ...over,
  }
}

describe('warehouseDay (Lote 15, franja "Almacén hoy")', () => {
  it('4 tarjetas en orden; número = hoy, texto = total de 7 días; 7 barritas en las tres primeras y ninguna en bajo mínimo', () => {
    const cards = warehouseDayCards(dto(), null)
    expect(cards.map((c) => c.key)).toEqual([...WAREHOUSE_DAY_CARDS])
    expect(cards.map((c) => c.today)).toEqual([6, 5, 1, 2])
    expect(cards.map((c) => c.total)).toEqual([21, 5, 1, null])
    expect(cards.map((c) => c.points?.length ?? null)).toEqual([7, 7, 7, null])
    expect(cards[0].points).toEqual(DATES.map((date, i) => ({ date, value: i })))
    // la última barrita es hoy
    expect(cards[1].points?.at(-1)).toEqual({ date: '2026-09-30', value: 5 })
  })

  it('naranja: lo decide el servidor (conteos si hoy hubo alguno; bajo mínimo si hay); nunca en recibido ni salida', () => {
    expect(warehouseDayCards(dto(), null).map((c) => c.alert)).toEqual([false, false, true, true])
    expect(warehouseDayCards(dto({ countsAlert: false, belowMinAlert: false }), null).map((c) => c.alert)).toEqual([false, false, false, false])
  })

  it('sin datos (cargando o 403): números nulos, sin barritas ni naranja', () => {
    const cards = warehouseDayCards(undefined, null)
    expect(cards.map((c) => c.today)).toEqual([null, null, null, null])
    expect(cards.every((c) => c.points === null && !c.alert)).toBe(true)
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
      '/warehouse/products?kpi=low&warehousePublicIds=wh-2',
    ])
  })
})
