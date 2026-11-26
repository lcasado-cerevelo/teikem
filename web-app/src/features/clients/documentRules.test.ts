import { describe, expect, it } from 'vitest'
import { documentError, documentTypeMarksJson, expiryStateKey, expiryTone, parseDocumentTypeMarks, validityRange } from './documentRules'

describe('marcas del tipo de documento', () => {
  it('sin marcas o con un JSON roto, ni vigencia ni impuestos', () => {
    expect(parseDocumentTypeMarks(null)).toEqual({ requiresValidTo: false, isTax: false })
    expect(parseDocumentTypeMarks('{roto')).toEqual({ requiresValidTo: false, isTax: false })
  })
  it('lee las dos marcas sin importar mayúsculas y las escribe de vuelta', () => {
    const marks = parseDocumentTypeMarks('{"RequiresValidTo":true,"istax":true}')
    expect(marks).toEqual({ requiresValidTo: true, isTax: true })
    expect(parseDocumentTypeMarks(documentTypeMarksJson(marks))).toEqual(marks)
  })
})

describe('formulario de subida', () => {
  const marks = { requiresValidTo: true, isTax: true }
  const ok = { type: 'IVU_EXEMPTION_STATE', number: '123', from: '2026-01-01', to: '2026-12-31' }
  it('revisa tipo, archivo, hasta obligatorio y orden de fechas', () => {
    expect(documentError({ ...ok, type: '' }, true, marks)).toBe('type')
    expect(documentError(ok, false, marks)).toBe('file')
    expect(documentError({ ...ok, to: '' }, true, marks)).toBe('toRequired')
    expect(documentError({ ...ok, to: '' }, true, { requiresValidTo: false, isTax: false })).toBeNull()
    expect(documentError({ ...ok, to: '2025-12-31' }, true, marks)).toBe('dates')
    expect(documentError({ ...ok, number: 'x'.repeat(61) }, true, marks)).toBe('number')
    expect(documentError(ok, true, marks)).toBeNull()
  })
})

describe('estado y fechas', () => {
  it('color y texto por estado', () => {
    expect(expiryTone('EXPIRING')).toBe('warn')
    expect(expiryTone('EXPIRED')).toBe('fail')
    expect(expiryTone('???')).toBe('neutral')
    expect(expiryStateKey('VALID')).toBe('clients.documents.states.VALID')
    expect(expiryStateKey(undefined)).toBe('clients.documents.states.NO_EXPIRY')
  })
  it('rango de vigencia', () => {
    expect(validityRange(null, null, (v) => v)).toBe('—')
    expect(validityRange('2026-01-01', null, (v) => v)).toBe('2026-01-01 → —')
  })
})
