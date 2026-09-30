// Lote 13 — piezas puras de la lista de recolecciones: texto "SKU ×cant", sugerencias del filtro "No. de orden" y la guarda
// de Eliminar (la empacada exige además orders.cancel).
import { describe, expect, it } from 'vitest'
import { batchProductsText, canDeletePickBatch, orderNumberSuggestions } from './pickBatchView'

describe('batchProductsText', () => {
  it('suma por SKU en el orden de aparición (una línea por serie o por posición)', () => {
    const lines = [
      { sku: 'A-1', quantity: 2 },
      { sku: 'B-2', quantity: 1.5 },
      { sku: 'A-1', quantity: 1 },
    ]
    expect(batchProductsText({ lines }, 'es')).toBe('A-1 ×3, B-2 ×1.5')
    expect(batchProductsText({ lines: null }, 'es')).toBe('')
  })
})

describe('orderNumberSuggestions', () => {
  it('números distintos sin distinguir mayúsculas, sin vacíos, en el orden en que llegan', () => {
    expect(orderNumberSuggestions([{ orderNumber: 'OR-2' }, { orderNumber: null }, { orderNumber: ' or-2 ' }, { orderNumber: 'OR-1' }, { orderNumber: '' }])).toEqual(['OR-2', 'OR-1'])
  })
})

describe('canDeletePickBatch', () => {
  it('canDelete del servidor; si está empacada, además orders.cancel', () => {
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'COLLECTED' }, false)).toBe(true)
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'PACKED' }, false)).toBe(false)
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'PACKED' }, true)).toBe(true)
    expect(canDeletePickBatch({ canDelete: false, statusCode: 'COLLECTED' }, true)).toBe(false)
  })
})
