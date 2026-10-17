// Pruebas de las reglas puras de Devoluciones de venta (Lote 82).
import { describe, expect, it } from 'vitest'
import { parseQty, receiveFormError, returnCanAuthorize, returnCanCancel, returnCanCredit, returnCanReceive, returnCanReject, returnFormError, toReceiveRequest, toReturnRequest, windowDaysError } from './returnRules'

const line = (quantity: string, returnable = 5) => ({ salesOrderLineId: 1, quantity, returnable, blocked: false })
const form = (over = {}) => ({ reason: 'DAMAGED', note: '', confirmOutsideWindow: false, lines: [line('2')], ...over })

describe('acciones por estatus', () => {
  it('autorizar y rechazar solo en solicitada', () => {
    expect(returnCanAuthorize('REQUESTED')).toBe(true)
    expect(returnCanReject('AUTHORIZED')).toBe(false)
  })
  it('cancelar solo si no se recibió nada', () => {
    expect(returnCanCancel('AUTHORIZED', 0)).toBe(true)
    expect(returnCanCancel('AUTHORIZED', 1)).toBe(false)
    expect(returnCanCancel('RECEIVED', 0)).toBe(false)
  })
  it('recibir una autorizada o una recibida con pendiente; acreditar una recibida', () => {
    expect(returnCanReceive('AUTHORIZED', 0)).toBe(true)
    expect(returnCanReceive('RECEIVED', 2)).toBe(true)
    expect(returnCanReceive('RECEIVED', 0)).toBe(false)
    expect(returnCanCredit('RECEIVED')).toBe(true)
    expect(returnCanCredit('CREDITED')).toBe(false)
  })
})

describe('pedir', () => {
  it('parseQty acepta coma y rechaza basura', () => {
    expect(parseQty('2,5')).toBe(2.5)
    expect(parseQty('abc')).toBeNull()
    expect(parseQty('')).toBeNull()
  })
  it('valida motivo, líneas, cantidades y tope', () => {
    expect(returnFormError(form({ reason: '' }), false, true)).toBe('reason')
    expect(returnFormError(form({ lines: [line('')] }), false, true)).toBe('lines')
    expect(returnFormError(form({ lines: [line('0')] }), false, true)).toBe('quantity')
    expect(returnFormError(form({ lines: [line('1,2345')] }), false, true)).toBe('quantity')
    expect(returnFormError(form({ lines: [line('6')] }), false, true)).toBe('exceeds')
    expect(returnFormError(form(), false, true)).toBeNull()
  })
  it('fuera de la ventana exige confirmación de quien autoriza', () => {
    expect(returnFormError(form(), true, false)).toBe('outside')
    expect(returnFormError(form(), true, true)).toBe('outside')
    expect(returnFormError(form({ confirmOutsideWindow: true }), true, true)).toBeNull()
  })
  it('arma la solicitud solo con las líneas con cantidad', () => {
    const r = toReturnRequest('O-1', form({ note: ' hola ', lines: [line('2'), line('')] }))
    expect(r.lines).toHaveLength(1)
    expect(r.note).toBe('hola')
    expect(r.confirmOutsideWindow).toBeUndefined()
  })
})

describe('recibir', () => {
  const rl = (over = {}) => ({ lineId: 9, pending: 3, good: '', damaged: '', damageNote: '', discard: false, lot: '', ...over })
  it('pide algo, sin pasar de lo pendiente y con descripción si hay dañado', () => {
    expect(receiveFormError({ lines: [rl()], closeRest: false })).toBe('nothing')
    expect(receiveFormError({ lines: [rl({ good: '4' })], closeRest: false })).toBe('exceeds')
    expect(receiveFormError({ lines: [rl({ good: '1', damaged: '1' })], closeRest: false })).toBe('damageNote')
    expect(receiveFormError({ lines: [rl({ good: 'x' })], closeRest: false })).toBe('quantity')
    expect(receiveFormError({ lines: [rl({ good: '2', damaged: '1', damageNote: 'rota' })], closeRest: false })).toBeNull()
  })
  it('arma la recepción con lo bueno, lo dañado, el lote y el desecho', () => {
    const r = toReceiveRequest({ closeRest: true, lines: [rl({ good: '2', damaged: '1', damageNote: ' rota ', discard: true, lot: ' L1 ' }), rl({ lineId: 10 })] })
    expect(r.closeRest).toBe(true)
    expect(r.lines).toEqual([{ lineId: 9, goodQty: 2, damagedQty: 1, damageNote: 'rota', damageDiscard: true, lot: { number: 'L1' } }])
  })
  it('la ventana es un entero de 0 a 3650', () => {
    expect(windowDaysError('30')).toBe(false)
    expect(windowDaysError('-1')).toBe(true)
    expect(windowDaysError('3651')).toBe(true)
    expect(windowDaysError('2.5')).toBe(true)
    expect(windowDaysError('')).toBe(true)
  })
})
