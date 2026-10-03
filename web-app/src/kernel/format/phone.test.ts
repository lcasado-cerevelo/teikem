// Teléfono con la máscara y el código de país de la compañía (phoneDigitsOnly/fmtPhone/normPhone de la maqueta).
import { describe, expect, it } from 'vitest'
import {
  formatPhone,
  formatPhoneInput,
  isValidPhone,
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
