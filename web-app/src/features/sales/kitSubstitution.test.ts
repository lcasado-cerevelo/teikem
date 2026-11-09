// Lote 230 — reglas puras de la sustitución de piezas de un kit al pedir.
import { describe, expect, it } from 'vitest'
import { catalogPieceOf, originalOf, piecesToRequest, substituteLabel, substituteOptions, substitutePiece, undoSubstitution, type KitSubstitutesDto } from './kitSubstitution'
import type { KitPiece } from './salesRules'

const SUBS: KitSubstitutesDto = {
  kitPublicId: 'k1',
  sku: 'KIT-1',
  adjustable: false,
  components: [
    {
      productPublicId: 'gm', sku: 'GM', name: 'Guante M', quantity: 2, notSubstitutable: false, categoryId: 4, salePrice: 5,
      substitutes: [
        { productPublicId: 'gs', sku: 'GS', name: 'Guante S', brand: 'Ammex', salePrice: 5 },
        { productPublicId: 'gl', sku: 'GL', name: 'Guante L', brand: null, salePrice: 5 },
      ],
    },
    { productPublicId: 'caja', sku: 'CAJA', name: 'Caja', quantity: 1, notSubstitutable: true, substitutes: [{ productPublicId: 'caja2', sku: 'CAJA2', name: 'Caja 2' }] },
    { productPublicId: 'gx', sku: 'GX', name: 'Guante X', quantity: 1, notSubstitutable: false, substitutes: [{ productPublicId: 'gl', sku: 'GL', name: 'Guante L' }] },
  ],
}
const PIECES: KitPiece[] = [
  { productPublicId: 'gm', sku: 'GM', name: 'Guante M', quantity: '2' },
  { productPublicId: 'caja', sku: 'CAJA', name: 'Caja', quantity: '1' },
  { productPublicId: 'gx', sku: 'GX', name: 'Guante X', quantity: '1' },
]

describe('sustitución de piezas', () => {
  it('opciones: solo los sustitutos válidos de la pieza; «no sustituible» o sin catálogo = ninguno', () => {
    expect(substituteOptions(SUBS, PIECES, PIECES[0]).map((s) => s.sku)).toEqual(['GS', 'GL'])
    expect(substituteOptions(SUBS, PIECES, PIECES[1])).toEqual([])
    expect(substituteOptions(undefined, PIECES, PIECES[0])).toEqual([])
  })

  it('cambiar: la pieza nueva en la misma cantidad, con la original para decir «en lugar de»; deshacer vuelve la original', () => {
    const changed = substitutePiece(PIECES, 'gm', SUBS.components![0].substitutes![1])
    expect(changed[0]).toEqual({ productPublicId: 'gl', sku: 'GL', name: 'Guante L', quantity: '2', substitutesProductPublicId: 'gm', substitutesSku: 'GM', substitutesName: 'Guante M' })
    expect(changed.slice(1)).toEqual(PIECES.slice(1))
    expect(originalOf(changed[0])).toBe('gm')
    expect(catalogPieceOf(SUBS, changed[0])?.sku).toBe('GM')
    // GL ya es la pieza de GM: no se ofrece para GX (dos piezas no quedan con el mismo producto) y GM ya no se ofrece a sí misma
    expect(substituteOptions(SUBS, changed, changed[2])).toEqual([])
    expect(substituteOptions(SUBS, changed, changed[0]).map((s) => s.sku)).toEqual(['GS'])
    // cambiar otra vez conserva la original
    const again = substitutePiece(changed, 'gm', SUBS.components![0].substitutes![0])
    expect(again[0]).toMatchObject({ productPublicId: 'gs', substitutesProductPublicId: 'gm', substitutesSku: 'GM' })
    expect(undoSubstitution(again, 'gm')).toEqual(PIECES)
  })

  it('cuerpo de la línea: todas las piezas; la sustituta con la original', () => {
    const changed = substitutePiece(PIECES, 'gm', SUBS.components![0].substitutes![0])
    expect(piecesToRequest(changed)).toEqual([
      { productPublicId: 'gs', quantity: 2, substitutesProductPublicId: 'gm' },
      { productPublicId: 'caja', quantity: 1 },
      { productPublicId: 'gx', quantity: 1 },
    ])
  })

  it('etiqueta: SKU · nombre · marca (sin marca, sin el separador)', () => {
    expect(substituteLabel({ sku: 'GS', name: 'Guante S', brand: 'Ammex' })).toBe('GS · Guante S · Ammex')
    expect(substituteLabel({ sku: 'GL', name: 'Guante L', brand: null })).toBe('GL · Guante L')
  })
})
