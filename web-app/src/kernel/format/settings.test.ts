// Ajustes de formato: lectura del DTO con respaldo campo a campo, regiones, "Personalizada" e intercambio de separadores.
import { describe, expect, it } from 'vitest'
import {
  DEFAULT_FORMAT,
  FORMAT_FIELDS,
  isKnownTimeZone,
  isRegionCustom,
  PR_FORMAT,
  regionDefaults,
  sameFormat,
  toFormatSettings,
  US_FORMAT,
  withSeparator,
} from './settings'

describe('ajustes de formato', () => {
  it('por defecto Puerto Rico (los mismos valores del SQL y de TenantFormatRules)', () => {
    expect(DEFAULT_FORMAT).toEqual({
      regionCode: 'PR',
      timeZoneId: 'America/Puerto_Rico',
      currencyCode: 'USD',
      currencySymbol: '$',
      currencySymbolPosition: 'B',
      currencyDecimals: 2,
      dateOrder: 'MDY',
      dateSeparator: '/',
      timeFormat: 12,
      weekStartDay: 0,
      thousandsSeparator: ',',
      decimalSeparator: '.',
      phoneCountryCode: '+1',
      phoneMask: '(###) ###-####',
    })
    expect(US_FORMAT.timeZoneId).toBe('America/New_York')
    expect(FORMAT_FIELDS).toHaveLength(13)
  })

  it('toFormatSettings: toma lo válido del DTO y lo demás del respaldo', () => {
    const s = toFormatSettings({
      regionCode: 'us',
      timeZoneId: 'Pacific/Honolulu',
      dateOrder: 'DMY',
      timeFormat: 24,
      currencyDecimals: 5,
      thousandsSeparator: '.',
      decimalSeparator: ',',
      phoneMask: 'sin numerales',
    })
    expect(s.regionCode).toBe('US')
    expect(s.timeZoneId).toBe('Pacific/Honolulu')
    expect(s.dateOrder).toBe('DMY')
    expect(s.timeFormat).toBe(24)
    expect(s.currencyDecimals).toBe(2)
    expect(s.thousandsSeparator).toBe('.')
    expect(s.decimalSeparator).toBe(',')
    expect(s.phoneMask).toBe('(###) ###-####')
    expect(toFormatSettings(null)).toBe(DEFAULT_FORMAT)
    expect(toFormatSettings({ timeZoneId: 'Marte/Olympus' }).timeZoneId).toBe('America/Puerto_Rico')
    // separadores iguales (no debería llegar): se usan los del respaldo
    const clash = toFormatSettings({ thousandsSeparator: ',', decimalSeparator: ',' })
    expect([clash.thousandsSeparator, clash.decimalSeparator]).toEqual([',', '.'])
  })

  it('zonas reconocidas por el navegador', () => {
    expect(isKnownTimeZone('America/Puerto_Rico')).toBe(true)
    expect(isKnownTimeZone('Nada/Nada')).toBe(false)
    expect(isKnownTimeZone('')).toBe(false)
  })

  it('valores de una región (de format-options si vienen) y la marca "Personalizada"', () => {
    const options = { regions: [{ ...US_FORMAT, timeZoneId: 'America/Chicago' }] }
    expect(regionDefaults('us', options)?.timeZoneId).toBe('America/Chicago')
    expect(regionDefaults('PR', options)).toEqual(PR_FORMAT)
    expect(regionDefaults('MX', options)).toBeNull()
    expect(isRegionCustom(PR_FORMAT, PR_FORMAT)).toBe(false)
    expect(isRegionCustom({ ...PR_FORMAT, timeFormat: 24 }, PR_FORMAT)).toBe(true)
    expect(isRegionCustom(PR_FORMAT, null)).toBe(false)
    expect(sameFormat(PR_FORMAT, { ...PR_FORMAT })).toBe(true)
    expect(sameFormat(PR_FORMAT, US_FORMAT)).toBe(false)
  })

  it('separadores: si el elegido choca con el otro, el otro se intercambia solo', () => {
    expect(withSeparator(PR_FORMAT, 'thousandsSeparator', '.')).toMatchObject({ thousandsSeparator: '.', decimalSeparator: ',' })
    expect(withSeparator(PR_FORMAT, 'decimalSeparator', ',')).toMatchObject({ thousandsSeparator: '.', decimalSeparator: ',' })
    expect(withSeparator(PR_FORMAT, 'thousandsSeparator', ' ')).toMatchObject({ thousandsSeparator: ' ', decimalSeparator: '.' })
    const eu = { ...PR_FORMAT, thousandsSeparator: '.', decimalSeparator: ',' }
    expect(withSeparator(eu, 'decimalSeparator', '.')).toMatchObject({ thousandsSeparator: ',', decimalSeparator: '.' })
    expect(withSeparator(eu, 'thousandsSeparator', ',')).toMatchObject({ thousandsSeparator: ',', decimalSeparator: '.' })
  })
})
