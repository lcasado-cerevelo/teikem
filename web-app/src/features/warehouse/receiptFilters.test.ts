// Lote 13 — lógica pura de la lista de Recibo: consulta al API con los filtros (variance[], phase), recibo fijado primero
// (`withPinned`), recibo elegido, documento y origen legible.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import {
  EMPTY_RECEIPT_FILTERS,
  hasDocument,
  hasReceiptFilters,
  receiptListQuery,
  receiptOrigin,
  receiptOriginText,
  receiptSender,
  selectedReceiptId,
  withPinned,
} from './receiptFilters'

beforeAll(() => setLang('es'))

describe('receiptListQuery', () => {
  it('sin filtros solo pagina (skip/take)', () => {
    expect(receiptListQuery(EMPTY_RECEIPT_FILTERS, { page: 1, pageSize: 25 })).toEqual({ skip: 0, take: 25 })
    expect(receiptListQuery(EMPTY_RECEIPT_FILTERS, { page: 3, pageSize: 10 })).toEqual({ skip: 20, take: 10 })
  })

  it('manda cada filtro al API: almacén, estatus, tipo, creado, productos, diferencia, fase y búsqueda', () => {
    const q = receiptListQuery(
      {
        warehousePublicId: 'W1',
        status: ['RECEIVING', 'DISCREPANCY'],
        types: ['BLIND'],
        created: { from: '2026-09-01', to: '2026-09-30' },
        products: [{ publicId: 'P1', sku: 'A-1', label: 'A-1 · Tornillo' }],
        variance: ['SHORT', 'OVER'],
      },
      { phase: 'PENDING_PUTAWAY', search: '  REC-1 ', page: 2, pageSize: 25 },
    )
    expect(q).toEqual({
      warehousePublicId: 'W1',
      status: ['RECEIVING', 'DISCREPANCY'],
      types: ['BLIND'],
      from: '2026-09-01',
      to: '2026-09-30',
      productPublicIds: ['P1'],
      variance: ['SHORT', 'OVER'],
      phase: 'PENDING_PUTAWAY',
      search: 'REC-1',
      skip: 25,
      take: 25,
    })
  })

  it('descarta códigos de diferencia desconocidos (el API respondería 400)', () => {
    const q = receiptListQuery({ ...EMPTY_RECEIPT_FILTERS, variance: ['NONE', 'X'] }, { page: 1, pageSize: 25 })
    expect(q.variance).toEqual(['NONE'])
    expect(receiptListQuery({ ...EMPTY_RECEIPT_FILTERS, variance: ['X'] }, { page: 1, pageSize: 25 }).variance).toBeUndefined()
  })

  it('hasReceiptFilters', () => {
    expect(hasReceiptFilters(EMPTY_RECEIPT_FILTERS)).toBe(false)
    expect(hasReceiptFilters({ ...EMPTY_RECEIPT_FILTERS, created: { from: '2026-01-01', to: '' } })).toBe(true)
    expect(hasReceiptFilters({ ...EMPTY_RECEIPT_FILTERS, variance: ['NONE'] })).toBe(true)
  })
})

describe('withPinned', () => {
  const a = { publicId: 'A', number: 'REC-1' }
  const b = { publicId: 'B', number: 'REC-2' }
  const c = { publicId: 'C', number: 'REC-3' }

  it('sin fijado, la lista tal cual', () => {
    expect(withPinned([a, b], null)).toEqual([a, b])
  })

  it('el recibo nuevo va primero aunque los filtros lo excluyan', () => {
    expect(withPinned([a, b], c).map((r) => r.publicId)).toEqual(['C', 'A', 'B'])
  })

  it('si viene en la página se mueve arriba con los datos de la página (sin duplicarlo)', () => {
    const fresh = { publicId: 'B', number: 'REC-2 (al día)' }
    const out = withPinned([a, fresh], b)
    expect(out.map((r) => r.publicId)).toEqual(['B', 'A'])
    expect(out[0]).toBe(fresh)
  })
})

describe('selección, documento y origen', () => {
  it('selectedReceiptId: el de ?receipt= aunque no esté en la página; si no, el primero', () => {
    expect(selectedReceiptId([{ publicId: 'A' }, { publicId: 'B' }], null)).toBe('A')
    expect(selectedReceiptId([{ publicId: 'A' }], 'Z')).toBe('Z')
    expect(selectedReceiptId([], null)).toBeNull()
  })

  it('hasDocument: aviso u orden de compra', () => {
    expect(hasDocument('PO')).toBe(true)
    expect(hasDocument('ASN')).toBe(true)
    expect(hasDocument('BLIND')).toBe(false)
    expect(hasDocument('RETURN')).toBe(false)
    expect(hasDocument(null)).toBe(false)
  })

  it('receiptOrigin normaliza (desconocido = Ciego)', () => {
    expect(receiptOrigin('po')).toBe('PO')
    expect(receiptOrigin('RETURN')).toBe('RETURN')
    expect(receiptOrigin(undefined)).toBe('BLIND')
  })

  it('receiptOriginText: textos de la maqueta', () => {
    expect(receiptOriginText({ origin: 'PO', originRef: 'PO-1038', senderName: 'MedSupply' }, t)).toBe('Origen · Orden de compra: PO-1038')
    expect(receiptOriginText({ origin: 'ASN', originRef: 'AX-48298', senderName: 'TRUSS' }, t)).toBe('Origen · Cliente: TRUSS · AX-48298')
    expect(receiptOriginText({ origin: 'BLIND' }, t)).toBe('Origen · Ciego')
    expect(receiptOriginText({ origin: 'RETURN' }, t)).toBe('Origen · Devolución')
  })

  it('receiptSender: remitente o, en ciegos, el tipo', () => {
    expect(receiptSender({ senderName: 'TRUSS', type: 'Aviso', typeCode: 'ASN' })).toBe('TRUSS')
    expect(receiptSender({ senderName: null, type: 'Ciego', typeCode: 'BLIND' })).toBe('Ciego')
  })
})
