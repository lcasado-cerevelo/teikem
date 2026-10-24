// Rangos predefinidos: fechas concretas de cada uno (semana que empieza el domingo, mes, año) y cómo se deduce el rango a partir de las fechas.
import { dayEndExclusiveUtc, dayStartUtc, presetOf, rangeOf } from './dateRanges'

// miércoles 2026-10-14
const NOW = new Date(2026, 9, 14, 15, 30)

describe('rangeOf', () => {
  it('hoy, ayer y últimos N días terminan hoy', () => {
    expect(rangeOf('today', NOW)).toEqual({ from: '2026-10-14', to: '2026-10-14' })
    expect(rangeOf('yesterday', NOW)).toEqual({ from: '2026-10-13', to: '2026-10-13' })
    expect(rangeOf('last7', NOW)).toEqual({ from: '2026-10-08', to: '2026-10-14' })
    expect(rangeOf('last30', NOW)).toEqual({ from: '2026-09-15', to: '2026-10-14' })
  })

  it('la semana completa empieza el domingo; la semana pasada es la anterior completa', () => {
    expect(rangeOf('thisWeek', NOW)).toEqual({ from: '2026-10-11', to: '2026-10-17' })
    expect(rangeOf('lastWeek', NOW)).toEqual({ from: '2026-10-04', to: '2026-10-10' })
    // un domingo es el primer día de su semana
    expect(rangeOf('thisWeek', new Date(2026, 9, 11))).toEqual({ from: '2026-10-11', to: '2026-10-17' })
  })

  it('mes, mes pasado (también en enero) y año', () => {
    expect(rangeOf('thisMonth', NOW)).toEqual({ from: '2026-10-01', to: '2026-10-31' })
    expect(rangeOf('lastMonth', NOW)).toEqual({ from: '2026-09-01', to: '2026-09-30' })
    expect(rangeOf('lastMonth', new Date(2026, 0, 5))).toEqual({ from: '2025-12-01', to: '2025-12-31' })
    expect(rangeOf('thisMonth', new Date(2028, 1, 5))).toEqual({ from: '2028-02-01', to: '2028-02-29' })
    expect(rangeOf('thisYear', NOW)).toEqual({ from: '2026-01-01', to: '2026-12-31' })
    expect(rangeOf('all', NOW)).toEqual({ from: '', to: '' })
    expect(rangeOf('custom', NOW)).toEqual({ from: '', to: '' })
  })
})

describe('presetOf', () => {
  it('vacío es «todas», fechas de un rango es ese rango y cualquier otra cosa es personalizado', () => {
    expect(presetOf({ from: '', to: '' }, NOW)).toBe('all')
    expect(presetOf({ from: '2026-10-14', to: '2026-10-14' }, NOW)).toBe('today')
    expect(presetOf({ from: '2026-10-01', to: '2026-10-31' }, NOW)).toBe('thisMonth')
    expect(presetOf({ from: '2026-10-02', to: '2026-10-09' }, NOW)).toBe('custom')
    expect(presetOf({ from: '2026-10-02', to: '' }, NOW)).toBe('custom')
  })
})

describe('días a UTC', () => {
  it('el inicio es la medianoche UTC y el fin es el inicio del día siguiente; lo mal escrito no filtra', () => {
    expect(dayStartUtc('2026-10-14')).toBe('2026-10-14T00:00:00Z')
    expect(dayEndExclusiveUtc('2026-10-31')).toBe('2026-11-01T00:00:00.000Z')
    expect(dayStartUtc('')).toBeUndefined()
    expect(dayEndExclusiveUtc('14/10/2026')).toBeUndefined()
  })
})
