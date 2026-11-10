// Lote 229 — reglas puras de las ofertas: validación del borrador, cuerpo (sin «hasta» = clearValidTo), qué impide publicar, estado, kit de oferta
// (validación, cuerpo con «No sustituible») y la suma de las piezas a precio normal con el ahorro. Lote 231: biblioteca (precio sugerido, renglón de «usar
// un kit anterior», kit ya en la oferta, composición bloqueada = solo el nombre).
import { describe, expect, it } from 'vitest'
import {
  EMPTY_OFFER_DRAFT,
  isPiecesProblem,
  kitInOffer,
  libraryKitItem,
  newOfferKitPiece,
  offerDraftError,
  offerIsOpenEnded,
  offerKitDraftError,
  offerKitSavings,
  offerPublishBlocker,
  offerStateKey,
  piecesFromKit,
  suggestedKitPrice,
  toOfferKitRequest,
  toOfferKitUpdate,
  toOfferRequest,
  type OfferKitDraft,
} from './offerRules'

const piece = (key: string, id: string | null, quantity: string, salePrice: number | null, notSubstitutable = false) => ({ ...newOfferKitPiece(key), productPublicId: id, quantity, salePrice, notSubstitutable })

describe('offerRules', () => {
  it('borrador: nombre, desde, rango y notas', () => {
    const d = EMPTY_OFFER_DRAFT('2026-11-09')
    expect(offerDraftError(d)).toBe('nameRequired')
    expect(offerDraftError({ ...d, name: 'x'.repeat(121) })).toBe('nameLong')
    expect(offerDraftError({ ...d, name: 'Navidad', validFrom: '' })).toBe('fromRequired')
    expect(offerDraftError({ ...d, name: 'Navidad', validTo: '2026-11-08' })).toBe('range')
    expect(offerDraftError({ ...d, name: 'Navidad', notes: 'x'.repeat(501) })).toBe('notesLong')
    expect(offerDraftError({ ...d, name: 'Navidad' })).toBeNull()
  })

  it('cuerpo: sin «hasta» se manda clearValidTo (sin fecha de fin)', () => {
    expect(toOfferRequest({ name: ' Navidad ', validFrom: '2026-11-09', validTo: '', appliesToAll: true, notes: ' ' })).toEqual({
      name: 'Navidad', validFrom: '2026-11-09', validTo: null, clearValidTo: true, appliesToAll: true, notes: null,
    })
    expect(toOfferRequest({ name: 'N', validFrom: '2026-11-09', validTo: '2026-12-31', appliesToAll: false, notes: 'a' })).toMatchObject({ validTo: '2026-12-31', clearValidTo: false, appliesToAll: false })
  })

  it('publicar exige renglones y clientes (o todos); estado y «sin fecha de fin»', () => {
    expect(offerPublishBlocker(0, true, 0)).toBe('noItems')
    expect(offerPublishBlocker(1, false, 0)).toBe('noClients')
    expect(offerPublishBlocker(1, true, 0)).toBeNull()
    expect(offerStateKey('ACTIVE')).toBe('ACTIVE')
    expect(offerStateKey(null)).toBe('DRAFT')
    expect(offerIsOpenEnded({ hasEndDate: false, validTo: null })).toBe(true)
    expect(offerIsOpenEnded({ hasEndDate: true, validTo: '2026-12-31' })).toBe(false)
    expect(offerIsOpenEnded({ validTo: null })).toBe(true)
  })

  it('kit de oferta: validación y cuerpo con «No sustituible»', () => {
    const base: OfferKitDraft = { sku: 'KIT-1', name: 'Kit', price: '30', pieces: [newOfferKitPiece('a')] }
    expect(offerKitDraftError({ ...base, sku: ' ' })).toBe('skuRequired')
    expect(offerKitDraftError({ ...base, name: '' })).toBe('nameRequired')
    expect(offerKitDraftError({ ...base, price: 'x' })).toBe('priceRequired')
    expect(offerKitDraftError(base)).toBe('noPieces')
    expect(offerKitDraftError({ ...base, pieces: [piece('a', null, '2', null)] })).toBe('pieceProduct')
    expect(offerKitDraftError({ ...base, pieces: [piece('a', 'm', '0', null)] })).toBe('pieceQuantity')
    expect(offerKitDraftError({ ...base, pieces: [piece('a', 'm', '1', null), piece('b', 'm', '1', null)] })).toBe('pieceRepeated')
    // al cambiar un kit ya creado no se piden SKU ni precio
    expect(offerKitDraftError({ ...base, sku: '', price: '', pieces: [piece('a', 'm', '1', null)] }, true)).toBeNull()
    const ok = { ...base, pieces: [piece('a', 'm', '1', 30), piece('b', 't', '2', 5, true), newOfferKitPiece('c')] }
    expect(offerKitDraftError(ok)).toBeNull()
    expect(toOfferKitRequest(ok)).toEqual({
      sku: 'KIT-1', name: 'Kit', price: 30,
      components: [{ productPublicId: 'm', quantity: 1, notSubstitutable: false }, { productPublicId: 't', quantity: 2, notSubstitutable: true }],
      adjustable: false,
    })
    expect(toOfferKitRequest({ ...ok, adjustable: true }).adjustable).toBe(true)
    expect(toOfferKitUpdate(ok).components).toHaveLength(2)
    expect(isPiecesProblem('pieceRepeated')).toBe(true)
    expect(isPiecesProblem('skuRequired')).toBe(false)
  })

  it('lote 231: kit de la biblioteca con la composición bloqueada = solo el nombre', () => {
    const locked: OfferKitDraft = { sku: 'KIT-1', name: ' Kit nuevo ', price: '', pieces: [newOfferKitPiece('a')], adjustable: true }
    // las piezas no se revisan ni viajan
    expect(offerKitDraftError(locked, true, true)).toBeNull()
    expect(offerKitDraftError(locked, true, false)).toBe('noPieces')
    expect(offerKitDraftError({ ...locked, name: '' }, true, true)).toBe('nameRequired')
    expect(toOfferKitUpdate(locked, true)).toEqual({ name: 'Kit nuevo' })
    expect(toOfferKitUpdate({ ...locked, pieces: [piece('a', 'm', '2', null, true)] })).toEqual({
      name: 'Kit nuevo', components: [{ productPublicId: 'm', quantity: 2, notSubstitutable: true }], adjustable: true,
    })
  })

  it('lote 231: usar un kit anterior — último precio sugerido, precio editable y obligatorio, sin repetir en la oferta', () => {
    expect(suggestedKitPrice({ lastPrice: 41.94 })).toBe('41.94')
    expect(suggestedKitPrice({ lastPrice: null })).toBe('')
    expect(libraryKitItem({ productPublicId: 'k1' }, '39.5')).toEqual({ productPublicId: 'k1', price: 39.5 })
    expect(libraryKitItem({ productPublicId: 'k1' }, '0')).toEqual({ productPublicId: 'k1', price: 0 })
    expect(libraryKitItem({ productPublicId: 'k1' }, '')).toBeNull()
    expect(libraryKitItem({ productPublicId: 'k1' }, 'abc')).toBeNull()
    expect(kitInOffer([{ productPublicId: 'k1' }, { productPublicId: 'p2' }], 'k1')).toBe(true)
    expect(kitInOffer([{ productPublicId: 'p2' }], 'k1')).toBe(false)
    expect(kitInOffer(null, 'k1')).toBe(false)
  })

  it('suma a precio normal y ahorro solo si se conocen todos los precios', () => {
    const d: OfferKitDraft = { sku: 'K', name: 'K', price: '30', pieces: [piece('a', 'm', '1', 30), piece('b', 't', '2', 5)] }
    expect(offerKitSavings(d)).toEqual({ normal: 40, offer: 30, saving: 10 })
    expect(offerKitSavings({ ...d, price: '45' })).toEqual({ normal: 40, offer: 45, saving: -5 })
    expect(offerKitSavings({ ...d, pieces: [piece('a', 'm', '1', 30), piece('b', 't', '1', null)] })).toBeNull()
    expect(offerKitSavings({ ...d, pieces: [newOfferKitPiece('a')] })).toBeNull()
  })

  it('piezas desde el kit del servidor (precio desconocido, marca conservada)', () => {
    const rows = piecesFromKit([{ productPublicId: 'm', sku: 'MON', name: 'Monitor', quantity: 2, notSubstitutable: true }], (i) => `k${i}`)
    expect(rows).toEqual([{ key: 'k0', productPublicId: 'm', sku: 'MON', name: 'Monitor', quantity: '2', notSubstitutable: true, salePrice: null }])
    expect(piecesFromKit([], (i) => `k${i}`)).toHaveLength(1)
  })
})
