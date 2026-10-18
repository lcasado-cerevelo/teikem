import { describe, expect, it } from 'vitest'
import { createFormError, parseAmount, resolveFormError, toResolveRequest } from './claimsRules'

describe('claimsRules', () => {
  it('lee montos con coma o punto', () => {
    expect(parseAmount('12,5')).toBe(12.5)
    expect(parseAmount(' 100 ')).toBe(100)
    expect(parseAmount('')).toBeNull()
    expect(parseAmount('abc')).toBeNaN()
  })
  it('valida la reclamación', () => {
    expect(createFormError({ declaredValue: '', notes: '' })).toBeNull()
    expect(createFormError({ declaredValue: 'x', notes: '' })).toBe('badValue')
    expect(createFormError({ declaredValue: '-3', notes: '' })).toBe('negativeValue')
    expect(createFormError({ declaredValue: '5', notes: 'x'.repeat(1001) })).toBe('notesTooLong')
  })
  it('valida la resolución: pagada pide monto', () => {
    expect(resolveFormError({ resolution: '', amountPaid: '', notes: '' })).toBe('resolution')
    expect(resolveFormError({ resolution: 'PAID', amountPaid: '', notes: '' })).toBe('amountRequired')
    expect(resolveFormError({ resolution: 'PAID', amountPaid: '-1', notes: '' })).toBe('badValue')
    expect(resolveFormError({ resolution: 'PAID', amountPaid: '40', notes: '' })).toBeNull()
    expect(resolveFormError({ resolution: 'REJECTED', amountPaid: '', notes: '' })).toBeNull()
  })
  it('arma la solicitud: el monto solo viaja si fue pagada', () => {
    expect(toResolveRequest({ resolution: 'PAID', amountPaid: '40,5', notes: ' ok ' }, 'rv')).toEqual({ resolution: 'PAID', amountPaid: 40.5, notes: 'ok', rowVersion: 'rv' })
    expect(toResolveRequest({ resolution: 'CLOSED', amountPaid: '40', notes: '' }).amountPaid).toBeNull()
  })
})
