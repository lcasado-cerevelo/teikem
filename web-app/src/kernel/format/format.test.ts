// Formatos de la compañía (región y formatos): funciones puras con los ajustes como parámetro. Puerto Rico (por defecto),
// Estados Unidos y combinaciones personalizadas (DMY/YMD, separadores, 24 h, símbolo después, zona de Honolulú).
import { afterEach, describe, expect, it } from 'vitest'
import { localDayOf, tenantToday, zonedInputFromUtc } from '../api/tenantZone'
import {
  applySeparators,
  formatDate,
  formatDateLong,
  formatDateLongTime,
  formatDateTime,
  formatDayMonth,
  formatMoney,
  formatNumber,
  formatTime,
  formatTimeOfDay,
  parseNumber,
  todayIso,
  utcNowText,
} from './format'
import { PR_FORMAT, US_FORMAT, type FormatSettings } from './settings'
import { getFormatSettings, resetFormatSettings, setFormatSettings, tenantTimeZone } from './store'

/** Espacios duros de Intl ("p. m.") → espacio normal. */
const norm = (s: string) => s.replace(/[  ]/g, ' ')

const EU: FormatSettings = {
  ...PR_FORMAT,
  currencySymbolPosition: 'A',
  currencySymbol: '€',
  currencyCode: 'EUR',
  dateOrder: 'DMY',
  dateSeparator: '.',
  timeFormat: 24,
  thousandsSeparator: '.',
  decimalSeparator: ',',
}
const HONOLULU: FormatSettings = { ...US_FORMAT, timeZoneId: 'Pacific/Honolulu' }

afterEach(() => resetFormatSettings())

describe('números y dinero', () => {
  it('Puerto Rico: coma de miles siempre, punto decimal, hasta 3 decimales solo si los tiene', () => {
    expect(formatNumber(61023, {}, PR_FORMAT)).toBe('61,023')
    expect(formatNumber(1250, {}, PR_FORMAT)).toBe('1,250')
    expect(formatNumber(61023.125, {}, PR_FORMAT)).toBe('61,023.125')
    expect(formatNumber(1.5, {}, PR_FORMAT)).toBe('1.5')
    expect(formatNumber(-3, { signDisplay: 'exceptZero' }, PR_FORMAT)).toBe('-3')
    expect(formatNumber(5, { signDisplay: 'exceptZero' }, PR_FORMAT)).toBe('+5')
  })

  it('separadores de la compañía: punto de miles y coma decimal; espacio de miles', () => {
    expect(formatNumber(61023.125, {}, EU)).toBe('61.023,125')
    expect(formatNumber(1234567.5, { minimumFractionDigits: 2 }, { ...PR_FORMAT, thousandsSeparator: ' ' })).toBe('1 234 567.50')
    expect(applySeparators('1,234.5', EU)).toBe('1.234,5')
    expect(formatNumber(1200000, { notation: 'compact', maximumFractionDigits: 1 }, EU)).toBe('1,2M')
  })

  it('parseNumber lee con los separadores de la compañía', () => {
    expect(parseNumber('12,345.5', PR_FORMAT)).toBe(12345.5)
    expect(parseNumber('12.345,5', EU)).toBe(12345.5)
    expect(parseNumber('+5', PR_FORMAT)).toBe(5)
    expect(parseNumber('−3', PR_FORMAT)).toBe(-3)
    expect(parseNumber('abc', PR_FORMAT)).toBeNull()
  })

  it('dinero: símbolo antes con el signo delante; decimales de la compañía; costo unitario hasta 4', () => {
    expect(formatMoney(1234567.5, 'es', {}, PR_FORMAT)).toBe('$1,234,567.50')
    expect(formatMoney(-12, 'en', {}, PR_FORMAT)).toBe('-$12.00')
    expect(formatMoney(0, 'es', { signed: true }, PR_FORMAT)).toBe('$0.00')
    expect(formatMoney(5, 'es', { signed: true }, PR_FORMAT)).toBe('+$5.00')
    expect(formatMoney(1.23456, 'es', { unitPrice: true }, PR_FORMAT)).toBe('$1.2346')
    expect(formatMoney(1.2, 'es', { unitPrice: true }, PR_FORMAT)).toBe('$1.20')
    expect(formatMoney(-0.001, 'es', {}, PR_FORMAT)).toBe('$0.00')
    expect(formatMoney(1234.5, 'es', {}, { ...PR_FORMAT, currencyDecimals: 0 })).toBe('$1,235')
    expect(formatMoney(1234.5, 'es', {}, { ...PR_FORMAT, currencyDecimals: 3 })).toBe('$1,234.500')
  })

  it('dinero: símbolo después del monto con espacio duro; otra moneda con su símbolo', () => {
    expect(formatMoney(1234.5, 'es', {}, EU)).toBe('1.234,50 €')
    expect(formatMoney(-12, 'es', {}, EU)).toBe('-12,00 €')
    expect(formatMoney(10, 'es', { currency: 'EUR' }, PR_FORMAT)).toBe('€10.00')
    expect(formatMoney(10, 'es', { currency: 'USD' }, PR_FORMAT)).toBe('$10.00')
  })

  it('sin ajustes explícitos usa los vigentes del store (Puerto Rico por defecto)', () => {
    expect(formatMoney(1234.5)).toBe('$1,234.50')
    setFormatSettings(EU)
    expect(formatMoney(1234.5)).toBe('1.234,50 €')
    expect(formatNumber(1234.5)).toBe('1.234,5')
  })
})

describe('fechas', () => {
  it('orden y separador: MDY (Puerto Rico), DMY, YMD', () => {
    expect(formatDate('2026-10-02', PR_FORMAT)).toBe('10/02/2026')
    expect(formatDate('2026-10-02', { ...PR_FORMAT, dateOrder: 'DMY' })).toBe('02/10/2026')
    expect(formatDate('2026-10-02', { ...PR_FORMAT, dateOrder: 'YMD', dateSeparator: '-' })).toBe('2026-10-02')
    expect(formatDate('2026-10-02', EU)).toBe('02.10.2026')
    expect(formatDate(null, PR_FORMAT)).toBe('')
    expect(formatDate('no-es-fecha', PR_FORMAT)).toBe('')
  })

  it('día y mes sin año en el mismo orden', () => {
    expect(formatDayMonth('2026-10-02', PR_FORMAT)).toBe('10/02')
    expect(formatDayMonth('2026-10-02', EU)).toBe('02.10')
    expect(formatDayMonth('2026-10-02', { ...PR_FORMAT, dateOrder: 'YMD' })).toBe('10/02')
  })

  it('un instante va a su día en la zona de la compañía; un día de calendario no se corre', () => {
    // 1:30 UTC del 3 de octubre = 2 de octubre en Puerto Rico (21:30) y en Honolulú (15:30)
    expect(formatDate('2026-10-03T01:30:00Z', PR_FORMAT)).toBe('10/02/2026')
    expect(formatDate('2026-10-03T01:30:00', HONOLULU)).toBe('10/02/2026')
    expect(formatDate('2026-10-03T05:00:00Z', PR_FORMAT)).toBe('10/03/2026')
    expect(formatDate('2026-10-03', HONOLULU)).toBe('10/03/2026')
  })

  it('hora: 12 h con a. m./p. m. del idioma, 24 h, en la zona de la compañía', () => {
    expect(formatTime('2026-10-03T01:30:00Z', 'en', {}, PR_FORMAT)).toBe('9:30 PM')
    expect(norm(formatTime('2026-10-03T01:30:00Z', 'es', {}, PR_FORMAT))).toBe('9:30 p. m.')
    expect(formatTime('2026-10-03T01:30:00Z', 'en', {}, HONOLULU)).toBe('3:30 PM')
    expect(formatTime('2026-10-03T01:30:05Z', 'es', { seconds: true }, { ...PR_FORMAT, timeFormat: 24 })).toBe('21:30:05')
    expect(formatTime('2026-10-03T13:05:00Z', 'es', {}, { ...PR_FORMAT, timeFormat: 24 })).toBe('09:05')
    expect(formatTime('2026-10-03', 'es', {}, PR_FORMAT)).toBe('')
    expect(formatTimeOfDay('14:05', 'en', PR_FORMAT)).toBe('2:05 PM')
    expect(formatTimeOfDay('14:05', 'en', EU)).toBe('14:05')
  })

  it('fecha y hora juntas; fecha larga con nombres del idioma y la zona de la compañía', () => {
    expect(formatDateTime('2026-10-03T01:30:00Z', 'en', PR_FORMAT)).toBe('10/02/2026 9:30 PM')
    expect(formatDateTime('2026-10-03T01:30:00Z', 'en', EU)).toBe('02.10.2026 21:30')
    expect(formatDateTime('2026-10-02', 'en', PR_FORMAT)).toBe('10/02/2026')
    expect(formatDateTime(undefined, 'en', PR_FORMAT)).toBe('')
    expect(formatDateLong('2026-10-03T01:30:00Z', 'es', undefined, PR_FORMAT)).toBe('viernes, 2 de octubre de 2026')
    expect(formatDateLong('2026-10-03T01:30:00Z', 'en', undefined, PR_FORMAT)).toBe('Friday, October 2, 2026')
    expect(formatDateLong('2026-10-02', 'en', { day: 'numeric', month: 'short' }, HONOLULU)).toBe('Oct 2')
    expect(norm(formatDateLongTime('2026-10-03T01:30:00Z', 'es', PR_FORMAT))).toBe('2 de octubre de 2026, 9:30 p. m.')
  })
})

describe('"hoy" y la zona de la compañía', () => {
  it('a la 1:30 UTC del 3 de octubre "hoy" es el 2 de octubre en Puerto Rico y en Honolulú', () => {
    const now = new Date('2026-10-03T01:30:00Z')
    expect(todayIso(now, PR_FORMAT)).toBe('2026-10-02')
    expect(todayIso(now, HONOLULU)).toBe('2026-10-02')
    // a las 5:00 UTC ya es el 3 en Puerto Rico, pero sigue siendo el 2 en Honolulú (UTC−10)
    const later = new Date('2026-10-03T05:00:00Z')
    expect(todayIso(later, PR_FORMAT)).toBe('2026-10-03')
    expect(todayIso(later, HONOLULU)).toBe('2026-10-02')
    expect(utcNowText(now)).toBe('2026-10-03 01:30 UTC')
  })

  it('la zona vigente (store) alimenta tenantZone: localDayOf, tenantToday y los datetime-local', () => {
    expect(tenantTimeZone()).toBe('America/Puerto_Rico')
    expect(localDayOf('2026-10-03T05:00:00Z')).toBe('2026-10-03')
    setFormatSettings(HONOLULU)
    expect(getFormatSettings().timeZoneId).toBe('Pacific/Honolulu')
    expect(localDayOf('2026-10-03T05:00:00Z')).toBe('2026-10-02')
    expect(tenantToday(new Date('2026-10-03T05:00:00Z'))).toBe('2026-10-02')
    expect(zonedInputFromUtc('2026-10-03T05:00:00Z')).toBe('2026-10-02T19:00')
    resetFormatSettings()
    expect(tenantTimeZone()).toBe('America/Puerto_Rico')
  })
})
