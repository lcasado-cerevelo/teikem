// Lote 231 — piezas de un kit (comunes al kit de oferta y al kit armado): validación, cuerpo y piezas desde el servidor.
import { describe, expect, it } from 'vitest'
import { kitPiecesError, newKitPiece, pieceQuantity, piecesFromComponents, piecesToComponents, type KitPiece } from './kitPieces'

const piece = (key: string, id: string | null, quantity: string, notSubstitutable = false): KitPiece => ({ ...newKitPiece(key), productPublicId: id, quantity, notSubstitutable })

describe('kitPieces', () => {
  it('cantidad: número > 0 con hasta 4 decimales (coma o punto)', () => {
    expect(pieceQuantity('2')).toBe(2)
    expect(pieceQuantity('1,5')).toBe(1.5)
    expect(pieceQuantity('0')).toBeNull()
    expect(pieceQuantity('1.23456')).toBeNull()
    expect(pieceQuantity('x')).toBeNull()
  })

  it('problemas en orden: sin piezas, sin producto, cantidad, repetida; el renglón vacío sin tocar no cuenta', () => {
    expect(kitPiecesError([newKitPiece('a')])).toBe('noPieces')
    expect(kitPiecesError([])).toBe('noPieces')
    expect(kitPiecesError([piece('a', null, '2')])).toBe('pieceProduct')
    expect(kitPiecesError([piece('a', 'm', '0')])).toBe('pieceQuantity')
    expect(kitPiecesError([piece('a', 'm', '1'), piece('b', 'm', '3')])).toBe('pieceRepeated')
    expect(kitPiecesError([piece('a', 'm', '1'), newKitPiece('b')])).toBeNull()
  })

  it('cuerpo: solo las piezas con producto, con su «No sustituible»', () => {
    expect(piecesToComponents([piece('a', 'm', '2', true), newKitPiece('b')])).toEqual([{ productPublicId: 'm', quantity: 2, notSubstitutable: true }])
  })

  it('desde el servidor: conserva cantidad y marca; sin piezas, un renglón vacío', () => {
    expect(piecesFromComponents([{ productPublicId: 'm', sku: 'MON', name: 'Monitor', quantity: 3, notSubstitutable: true }], (i) => `k${i}`)).toEqual([
      { key: 'k0', productPublicId: 'm', sku: 'MON', name: 'Monitor', quantity: '3', notSubstitutable: true, salePrice: null },
    ])
    expect(piecesFromComponents(null, (i) => `k${i}`)).toEqual([newKitPiece('k0')])
  })
})
