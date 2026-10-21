// Búsqueda global: a dónde lleva cada resultado y cuándo se abre solo.
import { describe, expect, it } from 'vitest'
import { autoOpenHit, hitHref, SEARCH_TYPES, type SearchHit } from './searchRules'

const hit = (over: Partial<SearchHit>): SearchHit => ({ type: 'ORDER', id: 'abc', title: 'ORD-1', subtitle: null, matchedBy: 'ORDER_NUMBER', exact: true, keys: null, ...over })

describe('hitHref', () => {
  it('lo que tiene ficha directa lleva a su ficha', () => {
    expect(hitHref(hit({ type: 'ORDER', id: 'g-1' }))).toBe('/orders/g-1')
    expect(hitHref(hit({ type: 'CLIENT', id: 'g-2' }))).toBe('/catalog/clients?client=g-2')
    expect(hitHref(hit({ type: 'PRODUCT', id: 'g-3' }))).toBe('/warehouse/products/g-3')
    expect(hitHref(hit({ type: 'WAREHOUSE', id: 'g-4' }))).toBe('/warehouse/warehouses?warehouse=g-4')
    expect(hitHref(hit({ type: 'RECEIPT', id: 'g-5' }))).toBe('/warehouse/receipts?receipt=g-5')
    expect(hitHref(hit({ type: 'PICK_BATCH', id: 'g-6' }))).toBe('/warehouse/pick-batches/g-6')
    expect(hitHref(hit({ type: 'EXIT_NOTICE', id: 'g-7' }))).toBe('/warehouse/notices?notice=g-7')
    expect(hitHref(hit({ type: 'PURCHASE_ORDER', id: 'g-8' }))).toBe('/warehouse/purchase-orders/g-8')
    expect(hitHref(hit({ type: 'SALES_ORDER', id: 'g-9' }))).toBe('/sales-orders?order=g-9')
    expect(hitHref(hit({ type: 'RENTAL', id: 'g-10' }))).toBe('/warehouse/rentals?rental=g-10')
    expect(hitHref(hit({ type: 'TRIP', id: 'g-11' }))).toBe('/ops/monitor?trip=g-11')
  })

  it('lo que no tiene ficha abre su lista ya filtrada por el número o el nombre', () => {
    expect(hitHref(hit({ type: 'CLAIM', title: 'RCL-00012' }))).toBe('/ops/claims?q=RCL-00012')
    expect(hitHref(hit({ type: 'SUPPLIER', title: 'Acme & Hijos' }))).toBe('/warehouse/suppliers?q=Acme%20%26%20Hijos')
    expect(hitHref(hit({ type: 'INVOICE', title: 'FAC-00007' }))).toBe('/money/billing?q=FAC-00007')
    expect(hitHref(hit({ type: 'CONSIGNEE', title: 'Colmado Uno' }))).toBe('/catalog/consignees?q=Colmado%20Uno')
  })

  it('zonas y posiciones llevan a su almacén y zona', () => {
    const keys = { warehouse: 'w-1', zone: '12' }
    expect(hitHref(hit({ type: 'ZONE', keys }))).toBe('/warehouse/locations?warehouse=w-1&zone=12')
    expect(hitHref(hit({ type: 'BIN', keys: { ...keys, bin: 'A-01-01' } }))).toBe('/warehouse/locations?warehouse=w-1&zone=12')
  })

  it('todos los tipos del servidor tienen ruta', () => {
    for (const type of SEARCH_TYPES) expect(hitHref(hit({ type, keys: { warehouse: 'w', zone: '1' } }))).not.toBe('/')
  })
})

describe('autoOpenHit', () => {
  const exact = hit({ exact: true })
  it('se abre solo con un único resultado exacto y la búsqueda ya completa', () => {
    expect(autoOpenHit([exact], 'ORD-1', true)).toBe(exact)
  })
  it('no se abre: varios, uno parecido, texto corto o búsqueda en curso', () => {
    expect(autoOpenHit([exact, hit({ id: 'x', exact: false })], 'ORD-1', true)).toBeNull()   // uno exacto y otros parecidos: se muestran
    expect(autoOpenHit([hit({ exact: false })], 'ORD-1', true)).toBeNull()
    expect(autoOpenHit([exact], 'OR', true)).toBeNull()
    expect(autoOpenHit([exact], 'ORD-1', false)).toBeNull()
    expect(autoOpenHit([], 'ORD-1', true)).toBeNull()
    expect(autoOpenHit(undefined, 'ORD-1', true)).toBeNull()
  })
})
