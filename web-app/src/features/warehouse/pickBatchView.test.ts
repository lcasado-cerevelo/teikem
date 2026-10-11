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
  const pick = { canCancelOrder: false, canPick: true, canIssue: false }
  it('la empacada exige orders.cancel; sin warehouse.pick no se elimina', () => {
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'COLLECTED' }, pick)).toBe(true)
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'PACKED' }, pick)).toBe(false)
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'PACKED' }, { ...pick, canCancelOrder: true })).toBe(true)
    expect(canDeletePickBatch({ canDelete: false, statusCode: 'COLLECTED' }, { ...pick, canCancelOrder: true })).toBe(false)
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'COLLECTED' }, { ...pick, canPick: false })).toBe(false)
  })
  it('un despacho manual exige warehouse.issue (no warehouse.pick)', () => {
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'COLLECTED', isManual: true }, pick)).toBe(false)
    expect(canDeletePickBatch({ canDelete: true, statusCode: 'COLLECTED', isManual: true }, { ...pick, canPick: false, canIssue: true })).toBe(true)
  })
})
