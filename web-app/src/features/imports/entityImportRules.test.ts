import { describe, expect, it } from 'vitest'
import { entityTemplateIssues, importCounts, rowErrorText, type EntityTemplateMessages } from './entityImportRules'

const M: EntityTemplateMessages = {
  columnsRequired: 'sin columnas',
  requirement: (k) => `falta ${k}`,
  repeated: (f) => `repetido ${f}`,
  noDefault: (f) => `sin default ${f}`,
  defaultValueRequired: (f) => `valor ${f}`,
}
const FIELDS = [
  { field: 'sku', required: true, defaultAllowed: false },
  { field: 'name', required: false, defaultAllowed: true },
  { field: 'brand', required: false, defaultAllowed: true },
]
const cols = (...fields: string[]) => fields.map((field) => ({ field }))

describe('entityTemplateIssues — productos', () => {
  it('pide al menos una columna, luego el sku, luego el nombre (columna o por defecto)', () => {
    expect(entityTemplateIssues('PRODUCT', { columns: cols('', ''), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: 'sin columnas' }])
    expect(entityTemplateIssues('PRODUCT', { columns: cols('name'), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.PRODUCT.sku' }])
    expect(entityTemplateIssues('PRODUCT', { columns: cols('sku'), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.PRODUCT.name' }])
    expect(entityTemplateIssues('PRODUCT', { columns: cols('sku'), defaults: [{ field: 'name', value: 'Sin nombre' }] }, FIELDS, M)).toEqual([])
    expect(entityTemplateIssues('PRODUCT', { columns: cols('sku', 'name'), defaults: [] }, FIELDS, M)).toEqual([])
  })

  it('campo repetido, default de un campo que no lo admite y default sin valor', () => {
    const issues = entityTemplateIssues('PRODUCT', { columns: cols('sku', 'sku', 'name'), defaults: [{ field: 'sku', value: 'X' }, { field: 'brand', value: ' ' }] }, FIELDS, M)
    expect(issues).toEqual([
      { path: 'columns.1.field', message: 'repetido sku' },
      { path: 'defaults.0.field', message: 'sin default sku' },
      { path: 'defaults.1.value', message: 'valor brand' },
    ])
  })
})

describe('entityTemplateIssues — proveedores', () => {
  it('el nombre tiene que ir en una columna (un valor por defecto no basta)', () => {
    const f = [{ field: 'name', required: true, defaultAllowed: false }, { field: 'notes', required: false, defaultAllowed: true }]
    expect(entityTemplateIssues('SUPPLIER', { columns: cols('notes'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.SUPPLIER.name' }])
    expect(entityTemplateIssues('SUPPLIER', { columns: cols('name'), defaults: [] }, f, M)).toEqual([])
  })
})

describe('resultado de la importación', () => {
  it('cuenta filas por estado y arma el texto de errores', () => {
    expect(importCounts([{ status: 'OK' }, { status: 'CREATED' }, { status: 'UPDATE' }, { status: 'UPDATED' }, { status: 'ERROR' }, { status: 'ERROR' }])).toEqual({ ok: 2, created: 1, updated: 1, error: 2 })
    expect(rowErrorText({ sku: 'repetido', salePrice: 'Debe ser un número.' }, (f) => f.toUpperCase())).toBe('SKU: repetido · SALEPRICE: Debe ser un número.')
    expect(rowErrorText(null, (f) => f)).toBe('')
  })
})

describe('entityTemplateIssues — almacenes y posiciones', () => {
  const f = [
    { field: 'code', required: true, defaultAllowed: false },
    { field: 'name', required: true, defaultAllowed: false },
    { field: 'warehouseCode', required: true, defaultAllowed: true },
    { field: 'zoneCode', required: true, defaultAllowed: true },
    { field: 'aisle', required: false, defaultAllowed: false },
  ]
  it('almacén: code y name en columna', () => {
    expect(entityTemplateIssues('WAREHOUSE', { columns: cols('name'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.WAREHOUSE.code' }])
    expect(entityTemplateIssues('WAREHOUSE', { columns: cols('code'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.WAREHOUSE.name' }])
    expect(entityTemplateIssues('WAREHOUSE', { columns: cols('code', 'name'), defaults: [] }, f, M)).toEqual([])
  })
  it('posición: almacén y zona en columna o por defecto, y código o sus partes', () => {
    expect(entityTemplateIssues('BIN', { columns: cols('zoneCode', 'code'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.BIN.warehouse' }])
    expect(entityTemplateIssues('BIN', { columns: cols('warehouseCode', 'code'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.BIN.zone' }])
    expect(entityTemplateIssues('BIN', { columns: cols('warehouseCode', 'zoneCode'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.BIN.code' }])
    expect(entityTemplateIssues('BIN', { columns: cols('code'), defaults: [{ field: 'warehouseCode', value: 'ALM' }, { field: 'zoneCode', value: 'PCK' }] }, f, M)).toEqual([])
    expect(entityTemplateIssues('BIN', { columns: cols('warehouseCode', 'zoneCode', 'aisle'), defaults: [] }, f, M)).toEqual([])
  })

  it('órdenes de compra (Lote 246): documentNumber, sku y quantity en columna; el proveedor en columna o como valor fijo', () => {
    const f = [{ field: 'supplier', defaultAllowed: true }, { field: 'documentNumber' }, { field: 'sku' }, { field: 'quantity' }]
    const all = ['supplier', 'documentNumber', 'sku', 'quantity']
    expect(entityTemplateIssues('PURCHASE_ORDER', { columns: cols('supplier', 'sku', 'quantity'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.PURCHASE_ORDER.documentNumber' }])
    expect(entityTemplateIssues('PURCHASE_ORDER', { columns: cols('supplier', 'documentNumber', 'quantity'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.PURCHASE_ORDER.sku' }])
    expect(entityTemplateIssues('PURCHASE_ORDER', { columns: cols('supplier', 'documentNumber', 'sku'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.PURCHASE_ORDER.quantity' }])
    expect(entityTemplateIssues('PURCHASE_ORDER', { columns: cols('documentNumber', 'sku', 'quantity'), defaults: [] }, f, M)).toEqual([{ path: 'columns', message: 'falta entityImport.errors.PURCHASE_ORDER.supplier' }])
    expect(entityTemplateIssues('PURCHASE_ORDER', { columns: cols('documentNumber', 'sku', 'quantity'), defaults: [{ field: 'supplier', value: 'Acme' }] }, f, M)).toEqual([])
    expect(entityTemplateIssues('PURCHASE_ORDER', { columns: cols(...all), defaults: [] }, f, M)).toEqual([])
    // el número de documento y el SKU no admiten valor fijo
    expect(entityTemplateIssues('PURCHASE_ORDER', { columns: cols('supplier', 'sku', 'quantity'), defaults: [{ field: 'documentNumber', value: 'X' }] }, f, M).map((x) => x.path)).toContain('defaults.0.field')
  })
})
