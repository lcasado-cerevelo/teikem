import { describe, expect, it } from 'vitest'
import { documentProgress } from './pickDocumentView'

describe('documentProgress (Lote 173)', () => {
  it('pedido, sacado, por sacar y lo que falta reservar', () => {
    expect(documentProgress({ ordered: 10, reserved: 4, shipped: 5 })).toEqual({ ordered: 10, shipped: 5, toCollect: 4, unreserved: 1, percent: 50 })
  })
  it('todo sacado: 100 % y nada pendiente', () => {
    expect(documentProgress({ ordered: 8, reserved: 0, shipped: 8 })).toEqual({ ordered: 8, shipped: 8, toCollect: 0, unreserved: 0, percent: 100 })
  })
  it('un envío no tiene pedido: solo lo recolectado y sin porcentaje', () => {
    expect(documentProgress({ ordered: null, reserved: null, shipped: 7 })).toEqual({ ordered: null, shipped: 7, toCollect: 0, unreserved: 0, percent: null })
  })
  it('sin pedido en cero no divide ni pasa de 100 %', () => {
    expect(documentProgress({ ordered: 0, reserved: 0, shipped: 0 }).percent).toBe(0)
    expect(documentProgress({ ordered: 5, reserved: 0, shipped: 9 }).percent).toBe(100)
  })
  it('sin decimales sueltos al restar', () => {
    expect(documentProgress({ ordered: 0.3, reserved: 0.1, shipped: 0.1 }).unreserved).toBe(0.1)
  })
})
