import { describe, expect, it } from 'vitest'
import { addDaysIso, DATE_PRESETS, matchPreset, presetRange, type DatePresetContext, type DatePresetId } from './datePresets'

// 2026-10-12 es lunes. Con semana desde el domingo (Puerto Rico) la semana es 11–17 de octubre; desde el lunes, 12–18.
const SUN: DatePresetContext = { today: '2026-10-12', weekStartDay: 0 }
const MON: DatePresetContext = { today: '2026-10-12', weekStartDay: 1 }
const range = (id: DatePresetId, ctx = SUN) => presetRange(id, ctx)

describe('datePresets · cálculo', () => {
  it('todas las fechas = vacío; personalizado no tiene fechas propias', () => {
    expect(range('all')).toEqual({ from: '', to: '' })
    expect(range('custom')).toBeNull()
  })

  it('hoy, ayer, mañana y los últimos/próximos N días (terminan o empiezan hoy)', () => {
    expect(range('today')).toEqual({ from: '2026-10-12', to: '2026-10-12' })
    expect(range('yesterday')).toEqual({ from: '2026-10-11', to: '2026-10-11' })
    expect(range('tomorrow')).toEqual({ from: '2026-10-13', to: '2026-10-13' })
    expect(range('last7')).toEqual({ from: '2026-10-06', to: '2026-10-12' })
    expect(range('last30')).toEqual({ from: '2026-09-13', to: '2026-10-12' })
    expect(range('next7')).toEqual({ from: '2026-10-12', to: '2026-10-18' })
  })

  it('«Esta semana» es la semana COMPLETA (no termina hoy), según el primer día de la compañía', () => {
    expect(range('thisWeek', SUN)).toEqual({ from: '2026-10-11', to: '2026-10-17' })
    expect(range('thisWeek', MON)).toEqual({ from: '2026-10-12', to: '2026-10-18' })
    expect(range('lastWeek', SUN)).toEqual({ from: '2026-10-04', to: '2026-10-10' })
    expect(range('lastWeek', MON)).toEqual({ from: '2026-10-05', to: '2026-10-11' })
    expect(range('nextWeek', SUN)).toEqual({ from: '2026-10-18', to: '2026-10-24' })
    expect(range('nextWeek', MON)).toEqual({ from: '2026-10-19', to: '2026-10-25' })
  })

  it('el primer día de la semana también cuando hoy ES ese día o es el último de la semana', () => {
    // domingo 2026-10-11: con semana desde domingo empieza hoy; desde lunes, la semana empezó el 5
    expect(range('thisWeek', { today: '2026-10-11', weekStartDay: 0 })).toEqual({ from: '2026-10-11', to: '2026-10-17' })
    expect(range('thisWeek', { today: '2026-10-11', weekStartDay: 1 })).toEqual({ from: '2026-10-05', to: '2026-10-11' })
    // sábado 2026-10-17: último día de la semana que empieza el domingo
    expect(range('thisWeek', { today: '2026-10-17', weekStartDay: 0 })).toEqual({ from: '2026-10-11', to: '2026-10-17' })
  })

  it('mes, mes pasado y próximo mes (mes completo)', () => {
    expect(range('thisMonth')).toEqual({ from: '2026-10-01', to: '2026-10-31' })
    expect(range('lastMonth')).toEqual({ from: '2026-09-01', to: '2026-09-30' })
    expect(range('nextMonth')).toEqual({ from: '2026-11-01', to: '2026-11-30' })
  })

  it('cambios de año: enero mira diciembre del año anterior y diciembre mira enero del siguiente', () => {
    const jan: DatePresetContext = { today: '2027-01-05', weekStartDay: 0 }
    expect(presetRange('lastMonth', jan)).toEqual({ from: '2026-12-01', to: '2026-12-31' })
    const dec: DatePresetContext = { today: '2026-12-20', weekStartDay: 0 }
    expect(presetRange('nextMonth', dec)).toEqual({ from: '2027-01-01', to: '2027-01-31' })
    // la semana que cruza el año
    expect(presetRange('thisWeek', { today: '2026-12-31', weekStartDay: 0 })).toEqual({ from: '2026-12-27', to: '2027-01-02' })
    expect(presetRange('last7', { today: '2027-01-03', weekStartDay: 0 })).toEqual({ from: '2026-12-28', to: '2027-01-03' })
  })

  it('febrero y años bisiestos', () => {
    expect(presetRange('thisMonth', { today: '2028-02-10', weekStartDay: 0 })).toEqual({ from: '2028-02-01', to: '2028-02-29' })
    expect(presetRange('thisMonth', { today: '2027-02-10', weekStartDay: 0 })).toEqual({ from: '2027-02-01', to: '2027-02-28' })
    expect(presetRange('lastMonth', { today: '2028-03-01', weekStartDay: 0 })).toEqual({ from: '2028-02-01', to: '2028-02-29' })
    expect(presetRange('yesterday', { today: '2028-03-01', weekStartDay: 0 })).toEqual({ from: '2028-02-29', to: '2028-02-29' })
  })

  it('este año: del 1 de enero al 31 de diciembre', () => {
    expect(range('thisYear')).toEqual({ from: '2026-01-01', to: '2026-12-31' })
  })

  it('addDaysIso cruza meses y años sin desfases', () => {
    expect(addDaysIso('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDaysIso('2026-03-01', -1)).toBe('2026-02-28')
    expect(addDaysIso('2026-10-12', 0)).toBe('2026-10-12')
  })
})

describe('datePresets · juegos y reconocimiento', () => {
  it('cada juego empieza por «Todas las fechas» y termina en «Personalizado»; los juegos tienen lo pedido', () => {
    for (const mode of ['past', 'future', 'both'] as const) {
      expect(DATE_PRESETS[mode][0]).toBe('all')
      expect(DATE_PRESETS[mode].at(-1)).toBe('custom')
    }
    expect(DATE_PRESETS.past).toEqual(['all', 'today', 'yesterday', 'last7', 'thisWeek', 'lastWeek', 'last30', 'thisMonth', 'lastMonth', 'thisYear', 'custom'])
    expect(DATE_PRESETS.future).toContain('next7')
    expect(DATE_PRESETS.both).toEqual(expect.arrayContaining(['yesterday', 'today', 'tomorrow', 'last7', 'next7', 'thisWeek', 'lastWeek', 'nextWeek', 'thisMonth']))
  })

  it('reconoce el rango que le corresponde a las fechas; vacío = todas; lo demás o una fecha sola = personalizado', () => {
    expect(matchPreset({ from: '', to: '' }, SUN, 'past')).toBe('all')
    expect(matchPreset({ from: '2026-10-12', to: '2026-10-12' }, SUN, 'past')).toBe('today')
    expect(matchPreset({ from: '2026-10-01', to: '2026-10-31' }, SUN, 'past')).toBe('thisMonth')
    expect(matchPreset({ from: '2026-10-11', to: '2026-10-17' }, SUN, 'past')).toBe('thisWeek')
    expect(matchPreset({ from: '2026-10-11', to: '2026-10-17' }, MON, 'past')).toBe('custom') // con otro primer día no es «esta semana»
    expect(matchPreset({ from: '2026-10-02', to: '2026-10-09' }, SUN, 'past')).toBe('custom')
    expect(matchPreset({ from: '2026-10-01', to: '' }, SUN, 'past')).toBe('custom')
    expect(matchPreset({ from: '', to: '2026-10-31' }, SUN, 'past')).toBe('custom')
  })

  it('un rango que no está en el juego no se reconoce (mañana no existe en «pasado»)', () => {
    expect(matchPreset({ from: '2026-10-13', to: '2026-10-13' }, SUN, 'past')).toBe('custom')
    expect(matchPreset({ from: '2026-10-13', to: '2026-10-13' }, SUN, 'future')).toBe('tomorrow')
    expect(matchPreset({ from: '2026-10-13', to: '2026-10-13' }, SUN, 'both')).toBe('tomorrow')
  })

  it('si dos opciones coinciden el mismo día gana la primera de la lista', () => {
    // sábado 2026-10-17 con semana desde domingo: «Últimos 7 días» (11–17) = «Esta semana» (11–17)
    const sat: DatePresetContext = { today: '2026-10-17', weekStartDay: 0 }
    expect(matchPreset({ from: '2026-10-11', to: '2026-10-17' }, sat, 'past')).toBe('last7')
  })
})
