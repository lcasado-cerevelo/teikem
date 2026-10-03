// Formatos de la compañía en la app (lote "Región y formatos", parte app): funciones puras con los ajustes como parámetro.
// Puerto Rico (por defecto), Estados Unidos y combinaciones personalizadas (DMY/YMD, separadores, 24 h, símbolo después).
import {
  datePlaceholder,
  formatDate,
  formatDateTime,
  formatDayMonth,
  formatMoney,
  formatNumber,
  formatQuantity,
  formatTime,
  formatWhen,
  parseDateInput,
  todayIso,
} from './format'
import { formatPhone, normalizePhone, phoneDigits } from './phone'
import { PR_FORMAT, toFormatSettings, US_FORMAT, type FormatSettings } from './settings'
import { zonedDay, zonedParts } from './zone'

const EU: FormatSettings = {
  ...PR_FORMAT,
  regionCode: 'ES',
  timeZoneId: 'Europe/Madrid',
  currencyCode: 'EUR',
  currencySymbol: '€',
  currencySymbolPosition: 'A',
  dateOrder: 'DMY',
  dateSeparator: '.',
  timeFormat: 24,
  thousandsSeparator: '.',
  decimalSeparator: ',',
}
const HONOLULU: FormatSettings = { ...US_FORMAT, timeZoneId: 'Pacific/Honolulu' }

describe('números y cantidades', () => {
  it('Puerto Rico: coma de miles siempre, punto decimal, hasta 3 decimales solo si los tiene', () => {
    expect(formatQuantity(61023, PR_FORMAT)).toBe('61,023')
    expect(formatQuantity(1250, PR_FORMAT)).toBe('1,250')
    expect(formatQuantity(999, PR_FORMAT)).toBe('999')
    expect(formatQuantity(1.5, PR_FORMAT)).toBe('1.5')
    expect(formatQuantity(-1250.1234, PR_FORMAT)).toBe('-1,250.123')
    expect(formatQuantity(1234567, PR_FORMAT)).toBe('1,234,567')
    expect(formatQuantity(-0.0001, PR_FORMAT)).toBe('0')
  })

  it('separadores de la compañía: punto de miles y coma decimal; espacio de miles', () => {
    expect(formatNumber(61023.125, {}, EU)).toBe('61.023,125')
    expect(formatNumber(1234567.5, { minDecimals: 2 }, { ...PR_FORMAT, thousandsSeparator: ' ' })).toBe('1 234 567.50')
    expect(formatQuantity(1.5, EU)).toBe('1,5')
  })
})

describe('dinero', () => {
  it('símbolo antes con el signo delante; decimales de la compañía; costo unitario hasta 4', () => {
    expect(formatMoney(1234567.5, {}, PR_FORMAT)).toBe('$1,234,567.50')
    expect(formatMoney(-12, {}, PR_FORMAT)).toBe('-$12.00')
    expect(formatMoney(5, { signed: true }, PR_FORMAT)).toBe('+$5.00')
    expect(formatMoney(0, { signed: true }, PR_FORMAT)).toBe('$0.00')
    expect(formatMoney(1.23456, { unitPrice: true }, PR_FORMAT)).toBe('$1.2346')
    expect(formatMoney(1.2, { unitPrice: true }, PR_FORMAT)).toBe('$1.20')
    expect(formatMoney(1234.5, {}, { ...PR_FORMAT, currencyDecimals: 0 })).toBe('$1,235')
  })

  it('símbolo después del monto (espacio duro) con los separadores de la compañía', () => {
    expect(formatMoney(1234.5, {}, EU)).toBe('1.234,50 €')
    expect(formatMoney(-12, {}, EU)).toBe('-12,00 €')
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
    expect(formatDate('2026-10-03T01:30:00', HONOLULU)).toBe('10/02/2026') // sin zona = UTC (como manda el API)
    expect(formatDate('2026-10-03T05:00:00Z', PR_FORMAT)).toBe('10/03/2026')
    expect(formatDate('2026-10-03', HONOLULU)).toBe('10/03/2026')
  })

  it('hora: 12 h con a. m./p. m. del idioma o 24 h, en la zona de la compañía', () => {
    expect(formatTime('2026-10-03T01:30:00Z', 'en', {}, PR_FORMAT)).toBe('9:30 PM')
    expect(formatTime('2026-10-03T01:30:00Z', 'es', {}, PR_FORMAT)).toBe('9:30 p. m.')
    expect(formatTime('2026-10-03T13:05:00Z', 'es', {}, PR_FORMAT)).toBe('9:05 a. m.')
    expect(formatTime('2026-10-03T04:00:00Z', 'en', {}, PR_FORMAT)).toBe('12:00 AM')
    expect(formatTime('2026-10-03T16:00:00Z', 'en', {}, PR_FORMAT)).toBe('12:00 PM')
    expect(formatTime('2026-10-03T01:30:00Z', 'en', {}, HONOLULU)).toBe('3:30 PM')
    expect(formatTime('2026-10-03T01:30:05Z', 'es', { seconds: true }, { ...PR_FORMAT, timeFormat: 24 })).toBe('21:30:05')
    expect(formatTime('2026-10-03T13:05:00Z', 'es', {}, { ...PR_FORMAT, timeFormat: 24 })).toBe('09:05')
    expect(formatTime('2026-10-03T04:00:00Z', 'es', {}, { ...PR_FORMAT, timeFormat: 24 })).toBe('00:00')
    expect(formatTime('2026-10-03', 'es', {}, PR_FORMAT)).toBe('')
  })

  it('fecha y hora juntas; "cuándo" corto: solo la hora si fue hoy en la zona de la compañía', () => {
    expect(formatDateTime('2026-10-03T01:30:00Z', 'en', PR_FORMAT)).toBe('10/02/2026 9:30 PM')
    expect(formatDateTime('2026-10-03T01:30:00Z', 'en', EU)).toBe('03.10.2026 03:30') // Madrid es UTC+2 en octubre
    expect(formatDateTime('2026-10-02', 'en', PR_FORMAT)).toBe('10/02/2026')
    const now = new Date('2026-10-03T02:00:00Z') // 10:00 p. m. del 2 en Puerto Rico
    expect(formatWhen('2026-10-03T01:30:00Z', 'es', now, PR_FORMAT)).toBe('9:30 p. m.')
    expect(formatWhen('2026-10-01T15:00:00Z', 'es', now, PR_FORMAT)).toBe('10/01/2026 11:00 a. m.')
  })
})

describe('"hoy" en la zona de la compañía', () => {
  it('a la 1:30 UTC del 3 de octubre "hoy" es el 2 de octubre en Puerto Rico (y en Honolulú)', () => {
    const now = new Date('2026-10-03T01:30:00Z')
    expect(todayIso(now, PR_FORMAT)).toBe('2026-10-02')
    expect(todayIso(now, HONOLULU)).toBe('2026-10-02')
    // a las 5:00 UTC ya es el 3 en Puerto Rico, pero sigue siendo el 2 en Honolulú (UTC−10)
    const later = new Date('2026-10-03T05:00:00Z')
    expect(todayIso(later, PR_FORMAT)).toBe('2026-10-03')
    expect(todayIso(later, HONOLULU)).toBe('2026-10-02')
  })

  it('zonedParts: la hora de Nueva York cambia con el horario de verano; la de Puerto Rico no', () => {
    expect(zonedParts(new Date('2026-07-01T12:00:00Z'), 'America/New_York').hour).toBe(8)
    expect(zonedParts(new Date('2026-12-01T12:00:00Z'), 'America/New_York').hour).toBe(7)
    expect(zonedParts(new Date('2026-07-01T12:00:00Z'), 'America/Puerto_Rico').hour).toBe(8)
    expect(zonedParts(new Date('2026-12-01T12:00:00Z'), 'America/Puerto_Rico').hour).toBe(8)
    expect(zonedDay(new Date('2026-10-03T03:59:00Z'), 'America/Puerto_Rico')).toBe('2026-10-02')
  })
})

describe('fecha escrita a mano (vencimiento)', () => {
  it('acepta el orden de la compañía con cualquier separador, y siempre ISO', () => {
    expect(parseDateInput('01/31/2027', PR_FORMAT)).toBe('2027-01-31')
    expect(parseDateInput('1-31-2027', PR_FORMAT)).toBe('2027-01-31')
    expect(parseDateInput('2027-01-31', PR_FORMAT)).toBe('2027-01-31')
    expect(parseDateInput('31.01.2027', EU)).toBe('2027-01-31')
    expect(parseDateInput('2027/01/31', { ...PR_FORMAT, dateOrder: 'YMD' })).toBe('2027-01-31')
  })

  it('rechaza lo que no es una fecha real', () => {
    expect(parseDateInput('31/01/2027', PR_FORMAT)).toBeNull() // mes 31
    expect(parseDateInput('02/30/2027', PR_FORMAT)).toBeNull()
    expect(parseDateInput('01/31/27', PR_FORMAT)).toBeNull() // año de 2 cifras
    expect(parseDateInput('mañana', PR_FORMAT)).toBeNull()
    expect(parseDateInput('', PR_FORMAT)).toBeNull()
  })

  it('el ejemplo del campo sigue el orden y el separador de la compañía', () => {
    expect(datePlaceholder('es', PR_FORMAT)).toBe('MM/DD/AAAA')
    expect(datePlaceholder('en', EU)).toBe('DD.MM.YYYY')
    expect(datePlaceholder('en', { ...PR_FORMAT, dateOrder: 'YMD', dateSeparator: '-' })).toBe('YYYY-MM-DD')
  })
})

describe('teléfono', () => {
  it('se guardan solo los dígitos y se muestra con la máscara de la compañía', () => {
    expect(normalizePhone('(787) 555-1234', PR_FORMAT)).toBe('7875551234')
    expect(normalizePhone('+1 787 555 1234', PR_FORMAT)).toBe('7875551234')
    expect(phoneDigits('787.555.1234')).toBe('7875551234')
    expect(formatPhone('7875551234', PR_FORMAT)).toBe('(787) 555-1234')
    expect(formatPhone('17875551234', PR_FORMAT)).toBe('(787) 555-1234')
    expect(formatPhone('7875551234', { ...PR_FORMAT, phoneMask: '###-###-####' })).toBe('787-555-1234')
  })

  it('un número que no calza con la máscara se deja tal cual', () => {
    expect(formatPhone('555-12', PR_FORMAT)).toBe('555-12')
    expect(normalizePhone(' ext. 12 ', PR_FORMAT)).toBe('ext. 12')
    expect(formatPhone(null, PR_FORMAT)).toBe('')
  })
})

describe('ajustes del servidor → formatos', () => {
  it('campo ausente o inválido toma el del respaldo; separadores iguales vuelven a los del respaldo', () => {
    const s = toFormatSettings({ regionCode: 'us', timeZoneId: 'America/New_York', dateOrder: 'XYZ', timeFormat: 13, thousandsSeparator: '.', decimalSeparator: '.' })
    expect(s.regionCode).toBe('US')
    expect(s.timeZoneId).toBe('America/New_York')
    expect(s.dateOrder).toBe('MDY')
    expect(s.timeFormat).toBe(12)
    expect([s.thousandsSeparator, s.decimalSeparator]).toEqual([',', '.'])
    expect(toFormatSettings({ timeZoneId: 'No/Existe' }).timeZoneId).toBe('America/Puerto_Rico')
    expect(toFormatSettings(null)).toBe(PR_FORMAT)
  })
})
