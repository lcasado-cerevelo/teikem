// Hora de la compañía (Puerto Rico, UTC−4 todo el año): un solo punto de zona para la web (Lote 14, compartido en el 15).
import { describe, expect, it } from 'vitest'
import { localDayOf, tenantToday, TENANT_TIME_ZONE, utcFromZonedInput, zonedInputFromUtc } from './tenantZone'

describe('tenantZone', () => {
  it('la zona es la de Puerto Rico', () => {
    expect(TENANT_TIME_ZONE).toBe('America/Puerto_Rico')
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

  it('localDayOf: un instante va a su día local; un día de calendario queda igual (LocalDay.DayOf / GroupKey)', () => {
    // 03:59Z del 30 = 23:59 del 29 en Puerto Rico; 04:00Z ya es el 30
    expect(localDayOf('2026-09-30T03:59:00')).toBe('2026-09-29')
    expect(localDayOf('2026-09-30T04:00:00Z')).toBe('2026-09-30')
    expect(localDayOf('2026-09-30T02:00:00.123+00:00')).toBe('2026-09-29')
    // día de calendario (DateOnly del API): sin conversión
    expect(localDayOf('2026-09-30')).toBe('2026-09-30')
    // otra zona (Nueva York en horario de verano, UTC−4; en invierno UTC−5)
    expect(localDayOf('2026-01-15T04:30:00Z', 'America/New_York')).toBe('2026-01-14')
    expect(localDayOf(null)).toBeNull()
    expect(localDayOf('abc')).toBeNull()
    expect(localDayOf(20260930)).toBeNull()
  })

  it('tenantToday: el día local a partir de la hora actual', () => {
    expect(tenantToday(new Date('2026-09-30T03:30:00Z'))).toBe('2026-09-29')
    expect(tenantToday(new Date('2026-09-30T04:00:00Z'))).toBe('2026-09-30')
  })
})
