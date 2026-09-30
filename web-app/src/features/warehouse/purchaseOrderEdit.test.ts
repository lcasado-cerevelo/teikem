// Proveedor y almacén de la orden de compra editables solo en Borrador (decisión del 2026-09-30): lógica pura.
import { describe, expect, it } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import { isDraftPurchaseOrder, partyErrorField, purchaseOrderPartyChanges, supplierOptionsWithCurrent } from './purchaseOrderEdit'

const WH = '11111111-1111-1111-1111-111111111111'
const WH2 = '22222222-2222-2222-2222-222222222222'
const PO = { supplierId: 1, warehousePublicId: WH }

describe('purchaseOrderEdit', () => {
  it('isDraftPurchaseOrder: solo DRAFT', () => {
    expect(isDraftPurchaseOrder({ statusCode: 'DRAFT' })).toBe(true)
    expect(isDraftPurchaseOrder({ statusCode: 'draft' })).toBe(true)
    expect(isDraftPurchaseOrder({ statusCode: 'SENT' })).toBe(false)
    expect(isDraftPurchaseOrder({ statusCode: null })).toBe(false)
  })

  it('supplierOptionsWithCurrent: activos; el actual dado de baja se agrega primero con la marca', () => {
    const active = [
      { id: 1, name: 'Acme Corp' },
      { id: 2, name: 'Bolt SA' },
    ]
    expect(supplierOptionsWithCurrent(active, { id: 1, name: 'Acme Corp' }, 'Inactivo')).toEqual([
      { value: '1', label: 'Acme Corp' },
      { value: '2', label: 'Bolt SA' },
    ])
    expect(supplierOptionsWithCurrent(active, { id: 9, name: 'Viejo SA' }, 'Inactivo')[0]).toEqual({ value: '9', label: 'Viejo SA', hint: 'Inactivo' })
    expect(supplierOptionsWithCurrent(active, { id: null, name: null }, 'Inactivo')).toHaveLength(2)
  })

  it('purchaseOrderPartyChanges: solo lo que cambió (el almacén sin distinguir mayúsculas)', () => {
    expect(purchaseOrderPartyChanges(PO, { supplierId: '1', warehousePublicId: WH })).toEqual({})
    expect(purchaseOrderPartyChanges(PO, { supplierId: '1', warehousePublicId: WH.toUpperCase() })).toEqual({})
    expect(purchaseOrderPartyChanges(PO, { supplierId: '2', warehousePublicId: WH })).toEqual({ supplierId: 2 })
    expect(purchaseOrderPartyChanges(PO, { supplierId: '1', warehousePublicId: WH2 })).toEqual({ warehousePublicId: WH2 })
    expect(purchaseOrderPartyChanges(PO, { supplierId: '2', warehousePublicId: WH2 })).toEqual({ supplierId: 2, warehousePublicId: WH2 })
    // vacío no es un cambio (lo detiene la validación del formulario)
    expect(purchaseOrderPartyChanges(PO, { supplierId: '', warehousePublicId: null })).toEqual({})
  })

  it('partyErrorField: 404/422 con un solo dato cambiado → ese campo; 409, otros o los dos → aviso', () => {
    const e404 = new ApiError(404, { title: 'Proveedor no encontrado.', code: 'not_found' })
    const e422 = new ApiError(422, { title: 'El almacén está dado de baja; no admite órdenes de compra nuevas.', code: 'status_rule' })
    const e409 = new ApiError(409, { title: 'El proveedor y el almacén solo se cambian mientras la orden de compra está en borrador.', code: 'conflict' })
    expect(partyErrorField(e404, { supplierId: 2 })).toBe('supplierId')
    expect(partyErrorField(e422, { warehousePublicId: WH2 })).toBe('warehousePublicId')
    expect(partyErrorField(e404, { supplierId: 2, warehousePublicId: WH2 })).toBeNull()
    expect(partyErrorField(e404, {})).toBeNull()
    expect(partyErrorField(e409, { supplierId: 2 })).toBeNull()
    expect(partyErrorField(new Error('red'), { supplierId: 2 })).toBeNull()
  })
})
