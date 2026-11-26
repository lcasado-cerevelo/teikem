// Lote 249 — reglas puras de «Modificar la orden»: operaciones, términos, validación y traducción de los textos del servidor.
import { describe, expect, it } from 'vitest'
import { authError, buildChangeRequest, buildOps, buildTerms, EMPTY_AUTH, EMPTY_DRAFT, hasChanges, lineError, minQuantity, serverText, type ChangeDraft } from './changeRules'
import type { ChangeLinePositionDto } from './changesApi'

const pos = (over: Partial<ChangeLinePositionDto> = {}): ChangeLinePositionDto => ({
  lineId: 1, lineNo: 1, productPublicId: 'p-1', sku: 'A1', name: 'Guantes', qtyOrdered: 8, minQuantity: 3, minQuantityWithReopen: 1, inCollected: 0, inPacked: 2, out: 1, invoiced: 0,
  isRemoved: false, unitPrice: 12.5, taxRatePct: 11.5, hasTaxOverride: false, isKit: false, ...over,
})
const draft = (d: Partial<ChangeDraft>): ChangeDraft => ({ ...EMPTY_DRAFT, ...d })

describe('operaciones de la modificación', () => {
  it('solo viaja lo que cambió de verdad (misma cantidad o precio = sin operación)', () => {
    const p = [pos()]
    expect(buildOps(draft({ edits: { 1: { quantity: '8', price: '12.5' } } }), p)).toEqual([])
    expect(buildOps(draft({ edits: { 1: { quantity: '10', price: '13' } } }), p)).toEqual([
      { kind: 'QTY', lineId: 1, quantity: 10 },
      { kind: 'PRICE', lineId: 1, unitPrice: 13 },
    ])
  })

  it('quitar, IVU manual con motivo, quitar el IVU manual y agregar un producto', () => {
    const p = [pos(), pos({ lineId: 2, lineNo: 2 }), pos({ lineId: 3, lineNo: 3 })]
    const ops = buildOps(
      draft({
        edits: { 1: { removed: true }, 2: { taxPct: '0', taxNote: ' exento ' }, 3: { clearTax: true } },
        added: [{ key: 1, productPublicId: 'p-9', sku: 'Z', name: 'Z', listPrice: 4, quantity: '2', price: '' }],
      }),
      p,
    )
    expect(ops).toEqual([
      { kind: 'REMOVE', lineId: 1 },
      { kind: 'TAX', lineId: 2, taxPct: 0, note: 'exento' },
      { kind: 'CLEAR_TAX', lineId: 3 },
      { kind: 'ADD', productPublicId: 'p-9', quantity: 2 },
    ])
  })

  it('los términos y las notas solo viajan si cambiaron; sin crédito el término se limpia', () => {
    const base = { paymentMethod: 'CREDIT', paymentTerm: 'NET30', deliveryMode: 'DELIVERY', deliveryModeSet: true, consigneePublicId: 'c-1', notes: 'n' }
    expect(buildTerms({ paymentMethod: 'CREDIT', notes: 'n' }, base)).toBeUndefined()
    expect(buildTerms({ paymentMethod: 'CASH' }, base)).toEqual({ paymentMethod: 'CASH', paymentTerm: '' })
    expect(buildTerms({ deliveryMode: 'PICKUP', consigneePublicId: '' }, base)).toEqual({ deliveryMode: 'PICKUP', clearConsignee: true })
    expect(hasChanges(draft({ terms: { notes: 'otra' } }), [pos()], base)).toBe(true)
    const req = buildChangeRequest(draft({ terms: { notes: 'otra' } }), [pos()], base, { ...EMPTY_AUTH, name: ' Marta ', channel: 'PHONE', reason: 'x', contactId: '7' }, '2026-11-26T10:00:00Z', { reopenPack: false, allowBackorder: true, overrideCredit: false, rowVersion: 'rv' })
    expect(req).toMatchObject({ notes: 'otra', authorizedByName: 'Marta', authorizedByContactId: 7, authorizationChannel: 'PHONE', allowBackorder: true, rowVersion: 'rv' })
    expect(req.reopenPack).toBeUndefined()
  })
})

describe('validación en pantalla', () => {
  it('el mínimo depende de si se reabre el empaque', () => {
    expect(minQuantity(pos(), false)).toBe(3)
    expect(minQuantity(pos(), true)).toBe(1)
    expect(lineError(pos(), { quantity: '2' }, false)).toBe('belowMin')
    expect(lineError(pos(), { quantity: '2' }, true)).toBeNull()
    expect(lineError(pos(), { quantity: '0' }, false)).toBe('quantity')
    expect(lineError(pos(), { taxPct: '101', taxNote: 'x' }, false)).toBe('taxPct')
    expect(lineError(pos(), { taxPct: '5', taxNote: ' ' }, false)).toBe('taxNote')
  })

  it('la autorización exige nombre, canal, fecha, motivo y, si se pide, la evidencia', () => {
    const ok = { ...EMPTY_AUTH, name: 'M', channel: 'EMAIL', at: '2026-11-26T09:00', reason: 'r' }
    expect(authError(EMPTY_AUTH, false)).toBe('name')
    expect(authError({ ...ok, channel: '' }, false)).toBe('channel')
    expect(authError({ ...ok, reason: '' }, false)).toBe('reason')
    expect(authError(ok, true)).toBe('attachment')
    expect(authError({ ...ok, attachmentId: 'f' }, true)).toBeNull()
  })
})

describe('textos del servidor en el idioma de la interfaz', () => {
  const t = (key: string, p?: Record<string, string | number>) => `${key}:${JSON.stringify(p ?? {})}`
  it('en español queda tal cual; en inglés se reconoce el mensaje y se arma con sus datos', () => {
    const msg = 'No se puede bajar A1 a 2: ya salieron 3.'
    expect(serverText(t, 'es', msg)).toBe(msg)
    expect(serverText(t, 'en', msg)).toBe('salesChanges.blockers.belowLeft:{"sku":"A1","qty":"2","left":"3"}')
    expect(serverText(t, 'en', 'La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito.')).toBe('salesChanges.blockers.billed:{}')
    expect(serverText(t, 'en', 'Un mensaje que no se conoce')).toBe('Un mensaje que no se conoce')
    expect(serverText(t, 'en', null)).toBe('')
  })
})
