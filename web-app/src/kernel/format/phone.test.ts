// Teléfono con la máscara y el código de país de la compañía (phoneDigitsOnly/fmtPhone/normPhone de la maqueta).
import { describe, expect, it } from 'vitest'
import {
  callingCodeInput,
  defaultCallingCode,
  formatPhone,
  formatPhoneFor,
  formatPhoneInput,
  isValidCallingCode,
  isValidPhone,
  isValidPhoneFor,
  normalizePhoneFor,
  usesCompanyMask,
  maskDigitCount,
  normalizePhone,
  phoneDigits,
  phoneLocalDigits,
  phonePlaceholder,
} from './phone'
import { PR_FORMAT, type FormatSettings } from './settings'

const DOTS: FormatSettings = { ...PR_FORMAT, phoneMask: '###.###.####' }
const UK: FormatSettings = { ...PR_FORMAT, phoneCountryCode: '+44', phoneMask: '#### ######' }

describe('teléfono de la compañía', () => {
  it('dígitos, dígitos de la máscara y placeholder', () => {
    expect(phoneDigits('(787) 555-1234')).toBe('7875551234')
    expect(maskDigitCount('(###) ###-####')).toBe(10)
    expect(phonePlaceholder(PR_FORMAT)).toBe('(000) 000-0000')
    expect(phonePlaceholder(DOTS)).toBe('000.000.0000')
  })

  it('el código de país se quita si sobra exactamente ese prefijo', () => {
    expect(phoneLocalDigits('+1 787 555 1234', PR_FORMAT)).toBe('7875551234')
    expect(phoneLocalDigits('17875551234', PR_FORMAT)).toBe('7875551234')
    expect(phoneLocalDigits('27875551234', PR_FORMAT)).toBe('27875551234')
    expect(phoneLocalDigits('+44 7911 123456', UK)).toBe('7911123456')
  })

  it('máscara mientras se escribe (parcial, sin los fijos de después) y con otra máscara', () => {
    expect(formatPhoneInput('', PR_FORMAT)).toBe('')
    expect(formatPhoneInput('7', PR_FORMAT)).toBe('(7')
    expect(formatPhoneInput('787', PR_FORMAT)).toBe('(787')
    expect(formatPhoneInput('7875', PR_FORMAT)).toBe('(787) 5')
    expect(formatPhoneInput('787555123499', PR_FORMAT)).toBe('(787) 555-1234')
    expect(formatPhoneInput('+1 (787) 555-1234', PR_FORMAT)).toBe('(787) 555-1234')
    expect(formatPhoneInput('7875551', DOTS)).toBe('787.555.1')
  })

  it('para mostrar: con los dígitos exactos, con máscara; si no, tal cual vino', () => {
    expect(formatPhone('7875550142', PR_FORMAT)).toBe('(787) 555-0142')
    expect(formatPhone('(787)555-0142', DOTS)).toBe('787.555.0142')
    expect(formatPhone('+1 787 555 0142', PR_FORMAT)).toBe('(787) 555-0142')
    expect(formatPhone('555-0142', PR_FORMAT)).toBe('555-0142')
    expect(formatPhone(null, PR_FORMAT)).toBe('')
  })

  it('validación y valor a guardar (solo dígitos si calza; si no, tal cual)', () => {
    expect(isValidPhone('', PR_FORMAT)).toBe(true)
    expect(isValidPhone('(787) 555-0142', PR_FORMAT)).toBe(true)
    expect(isValidPhone('+1 787 555 0142', PR_FORMAT)).toBe(true)
    expect(isValidPhone('555-0142', PR_FORMAT)).toBe(false)
    expect(normalizePhone(' (787) 555-0142 ', PR_FORMAT)).toBe('7875550142')
    expect(normalizePhone('+1 787 555 0142', PR_FORMAT)).toBe('7875550142')
    expect(normalizePhone(' ext. 12 ', PR_FORMAT)).toBe('ext. 12')
    expect(normalizePhone('', PR_FORMAT)).toBe('')
  })
})

describe('prefijo internacional por teléfono (2026-10-20)', () => {
  it('el prefijo de la compañía sale de su código de país', () => {
    expect(defaultCallingCode(PR_FORMAT)).toBe('1')
    expect(defaultCallingCode(UK)).toBe('44')
    expect(callingCodeInput(null, PR_FORMAT)).toBe('+1')
    expect(callingCodeInput('593', PR_FORMAT)).toBe('+593')
    expect(callingCodeInput(null, UK)).toBe('+44')
  })

  it('un prefijo válido es de 1 a 4 dígitos, con o sin «+», y no empieza en 0', () => {
    for (const ok of ['', '+1', '1', '+593', ' + 44 ', '1809']) expect(isValidCallingCode(ok)).toBe(true)
    for (const bad of ['0', '+0', '12345', '+', 'ab', '+1a']) expect(isValidCallingCode(bad)).toBe(bad === '' ? true : false)
  })

  it('con el prefijo de la compañía (o sin prefijo) rige la máscara; con otro, los dígitos y el prefijo delante', () => {
    expect(usesCompanyMask('', PR_FORMAT)).toBe(true)
    expect(usesCompanyMask('+1', PR_FORMAT)).toBe(true)
    expect(usesCompanyMask('593', PR_FORMAT)).toBe(false)
    expect(formatPhoneFor('7875551234', '1', PR_FORMAT)).toBe('(787) 555-1234')
    expect(formatPhoneFor('7875551234', null, PR_FORMAT)).toBe('(787) 555-1234')
    expect(formatPhoneFor('991234567', '593', PR_FORMAT)).toBe('+593 991234567')
    expect(formatPhoneFor('', '593', PR_FORMAT)).toBe('')
  })

  it('validación y valor a guardar según el prefijo', () => {
    expect(isValidPhoneFor('(787) 555-1234', '+1', PR_FORMAT)).toBe(true)
    expect(isValidPhoneFor('555-1234', '+1', PR_FORMAT)).toBe(false) // la máscara pide 10 dígitos
    expect(isValidPhoneFor('99 123 4567', '+593', PR_FORMAT)).toBe(true) // otro prefijo: de 7 dígitos hasta 15 con el prefijo
    expect(isValidPhoneFor('123456', '+593', PR_FORMAT)).toBe(false)
    expect(isValidPhoneFor('1'.repeat(13), '+593', PR_FORMAT)).toBe(false) // 13 + 3 = 16 > 15
    expect(isValidPhoneFor('', '+593', PR_FORMAT)).toBe(true)
    expect(isValidPhoneFor('7875551234', '12345', PR_FORMAT)).toBe(false) // prefijo inválido
    expect(normalizePhoneFor('(787) 555-1234', '+1', PR_FORMAT)).toBe('7875551234')
    expect(normalizePhoneFor('99 123 4567', '+593', PR_FORMAT)).toBe('991234567')
  })
})
