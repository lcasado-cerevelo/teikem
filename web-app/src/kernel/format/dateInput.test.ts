// Captura de fechas y horas con el formato de la compañía: lógica pura (máscara, lectura, pegado, calendario).
import { describe, expect, it } from 'vitest'
import {
  datePlaceholder,
  dateToText,
  inRange,
  isIsoDate,
  isRealDate,
  isoFromDigits,
  maskDate,
  maskTime,
  monthGrid,
  parsePastedDate,
  parsePastedTime,
  parseTimeText,
  shiftDay,
  shiftMonth,
  timeToText,
} from './dateInput'
import { PR_FORMAT, type FormatSettings } from './settings'

const MDY = PR_FORMAT
const DMY: FormatSettings = { ...PR_FORMAT, dateOrder: 'DMY', dateSeparator: '.', timeFormat: 24, weekStartDay: 1 }
const YMD: FormatSettings = { ...PR_FORMAT, dateOrder: 'YMD', dateSeparator: '-' }

describe('máscara de la fecha según la compañía', () => {
  it('MDY (Puerto Rico): solo dígitos y los separadores aparecen solos', () => {
    expect(maskDate('', MDY)).toBe('')
    expect(maskDate('1', MDY)).toBe('1')
    expect(maskDate('10', MDY)).toBe('10')
    expect(maskDate('102', MDY)).toBe('10/2')
    expect(maskDate('1021', MDY)).toBe('10/21')
    expect(maskDate('10212026', MDY)).toBe('10/21/2026')
    expect(maskDate('1021202699', MDY)).toBe('10/21/2026')   // no pasa de 8 dígitos
    expect(maskDate('1a0-2/1x', MDY)).toBe('10/21')            // letras y signos no entran
  })
  it('DMY con punto y YMD con guion', () => {
    expect(maskDate('21102026', DMY)).toBe('21.10.2026')
    expect(maskDate('20261021', YMD)).toBe('2026-10-21')
    expect(maskDate('202610', YMD)).toBe('2026-10')
  })
  it('el ejemplo del campo sale del formato de la compañía', () => {
    expect(datePlaceholder(MDY)).toBe('MM/DD/YYYY')
    expect(datePlaceholder(DMY)).toBe('DD.MM.YYYY')
    expect(datePlaceholder(YMD)).toBe('YYYY-MM-DD')
  })
  it('ISO ↔ texto del campo', () => {
    expect(dateToText('2026-10-21', MDY)).toBe('10/21/2026')
    expect(dateToText('2026-10-21', DMY)).toBe('21.10.2026')
    expect(dateToText('2026-10-21', YMD)).toBe('2026-10-21')
    expect(dateToText('', MDY)).toBe('')
    expect(dateToText('nada', MDY)).toBe('')
  })
})

describe('fecha completa tecleada', () => {
  it('8 dígitos en el orden de la compañía dan el ISO; un día que no existe no', () => {
    expect(isoFromDigits('10212026', MDY)).toBe('2026-10-21')
    expect(isoFromDigits('21102026', DMY)).toBe('2026-10-21')
    expect(isoFromDigits('20261021', YMD)).toBe('2026-10-21')
    expect(isoFromDigits('02302026', MDY)).toBeNull()   // 30 de febrero
    expect(isoFromDigits('13012026', MDY)).toBeNull()   // mes 13
    expect(isoFromDigits('1021202', MDY)).toBeNull()    // incompleta
  })
  it('años bisiestos', () => {
    expect(isRealDate(2024, 2, 29)).toBe(true)
    expect(isRealDate(2026, 2, 29)).toBe(false)
    expect(isIsoDate('2024-02-29')).toBe(true)
    expect(isIsoDate('2026-02-29')).toBe(false)
    expect(isIsoDate('10/21/2026')).toBe(false)
  })
})

describe('fecha pegada: se cambia al formato de la compañía o se rechaza', () => {
  it('el orden de la compañía con cualquier separador y días o meses de un dígito', () => {
    expect(parsePastedDate('10/21/2026', MDY)).toBe('2026-10-21')
    expect(parsePastedDate('10-21-2026', MDY)).toBe('2026-10-21')
    expect(parsePastedDate('10.21.2026', MDY)).toBe('2026-10-21')
    expect(parsePastedDate(' 1/5/2026 ', MDY)).toBe('2026-01-05')
    expect(parsePastedDate('10212026', MDY)).toBe('2026-10-21')
    expect(parsePastedDate('21/10/2026', DMY)).toBe('2026-10-21')
  })
  it('ISO se entiende siempre (no deja duda)', () => {
    expect(parsePastedDate('2026-10-21', MDY)).toBe('2026-10-21')
    expect(parsePastedDate('2026/10/21', DMY)).toBe('2026-10-21')
  })
  it('se rechaza lo que no se entiende: año de 2 dígitos, el orden contrario cuando no cabe, letras, vacío', () => {
    expect(parsePastedDate('21/10/2026', MDY)).toBeNull()   // mes 21
    expect(parsePastedDate('10/21/26', MDY)).toBeNull()
    expect(parsePastedDate('Oct 21, 2026', MDY)).toBeNull()
    expect(parsePastedDate('mañana', MDY)).toBeNull()
    expect(parsePastedDate('', MDY)).toBeNull()
    expect(parsePastedDate('02/30/2026', MDY)).toBeNull()
  })
  it('cuando los dos órdenes caben manda el de la compañía', () => {
    expect(parsePastedDate('03/04/2026', MDY)).toBe('2026-03-04')
    expect(parsePastedDate('03/04/2026', DMY)).toBe('2026-04-03')
  })
})

describe('hora', () => {
  it('máscara: hasta 4 dígitos', () => {
    expect(maskTime('')).toBe('')
    expect(maskTime('2')).toBe('2')           // podría ser 20–23
    expect(maskTime('9')).toBe('09')          // ya no puede ser decena: se completa con el cero
    expect(maskTime('930')).toBe('09:30')
    expect(maskTime('093')).toBe('09:3')
    expect(maskTime('0930')).toBe('09:30')
    expect(maskTime('09:30pm')).toBe('09:30')
    expect(maskTime('2', 12)).toBe('02')      // con 12 horas la decena solo puede ser 0 o 1
    expect(maskTime('1', 12)).toBe('1')
  })
  it('24 horas ↔ texto y AM/PM de 12', () => {
    expect(timeToText('21:05', 24)).toEqual({ text: '21:05', period: null })
    expect(timeToText('21:05', 12)).toEqual({ text: '09:05', period: 'PM' })
    expect(timeToText('00:15', 12)).toEqual({ text: '12:15', period: 'AM' })
    expect(timeToText('12:00', 12)).toEqual({ text: '12:00', period: 'PM' })
    expect(timeToText('', 12)).toEqual({ text: '', period: null })
  })
  it('lectura con rango: 12 horas necesita 1–12 y 24 horas 0–23; los minutos hasta 59', () => {
    expect(parseTimeText('09:30', 'PM', 12)).toBe('21:30')
    expect(parseTimeText('12:00', 'AM', 12)).toBe('00:00')
    expect(parseTimeText('12:00', 'PM', 12)).toBe('12:00')
    expect(parseTimeText('13:00', 'PM', 12)).toBeNull()
    expect(parseTimeText('00:30', 'AM', 12)).toBeNull()
    expect(parseTimeText('23:59', null, 24)).toBe('23:59')
    expect(parseTimeText('24:00', null, 24)).toBeNull()
    expect(parseTimeText('09:60', null, 24)).toBeNull()
    expect(parseTimeText('09:3', null, 24)).toBeNull()
  })
  it('hora pegada', () => {
    expect(parsePastedTime('9:30 pm', 12)).toEqual({ hhmm: '21:30', period: 'PM' })
    expect(parsePastedTime('21:05', 24)).toEqual({ hhmm: '21:05', period: null })
    expect(parsePastedTime('0930', 24)).toEqual({ hhmm: '09:30', period: null })
    expect(parsePastedTime('25:00', 24)).toBeNull()
    expect(parsePastedTime('abc', 24)).toBeNull()
  })
})

describe('calendario', () => {
  it('cuadrícula de 6 semanas que empieza donde dice la compañía (octubre de 2026: jueves 1)', () => {
    const sun = monthGrid(2026, 9, 0)
    expect(sun).toHaveLength(6)
    expect(sun[0]).toEqual([null, null, null, null, '2026-10-01', '2026-10-02', '2026-10-03'])   // domingo primero
    const mon = monthGrid(2026, 9, 1)
    expect(mon[0]).toEqual([null, null, null, '2026-10-01', '2026-10-02', '2026-10-03', '2026-10-04'])   // lunes primero
    expect(sun.flat().filter(Boolean)).toHaveLength(31)
  })
  it('mover meses y días cruza el año', () => {
    expect(shiftMonth(2026, 11, 1)).toEqual({ year: 2027, month: 0 })
    expect(shiftMonth(2026, 0, -1)).toEqual({ year: 2025, month: 11 })
    expect(shiftDay('2026-12-31', 1)).toBe('2027-01-01')
    expect(shiftDay('2026-03-01', -1)).toBe('2026-02-28')
  })
  it('mínimo y máximo inclusivos', () => {
    expect(inRange('2026-10-21', '2026-10-21', '2026-10-21')).toBe(true)
    expect(inRange('2026-10-20', '2026-10-21')).toBe(false)
    expect(inRange('2026-10-22', undefined, '2026-10-21')).toBe(false)
    expect(inRange('2026-10-22')).toBe(true)
  })
})
