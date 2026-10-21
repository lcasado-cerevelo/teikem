import { describe, expect, it } from 'vitest'
import { importCounts, productTemplateIssues, rowErrorText, type ProductTemplateMessages } from './productImportRules'

const M: ProductTemplateMessages = {
  columnsRequired: 'sin columnas',
  sku: 'falta sku',
  name: 'falta nombre',
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

describe('productTemplateIssues', () => {
  it('pide al menos una columna, luego el sku, luego el nombre (columna o por defecto)', () => {
    expect(productTemplateIssues({ columns: cols('', ''), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: 'sin columnas' }])
    expect(productTemplateIssues({ columns: cols('name'), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: 'falta sku' }])
    expect(productTemplateIssues({ columns: cols('sku'), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: 'falta nombre' }])
    expect(productTemplateIssues({ columns: cols('sku'), defaults: [{ field: 'name', value: 'Sin nombre' }] }, FIELDS, M)).toEqual([])
    expect(productTemplateIssues({ columns: cols('sku', 'name'), defaults: [] }, FIELDS, M)).toEqual([])
  })

  it('campo repetido, default de un campo que no lo admite y default sin valor', () => {
    const issues = productTemplateIssues({ columns: cols('sku', 'sku', 'name'), defaults: [{ field: 'sku', value: 'X' }, { field: 'brand', value: ' ' }] }, FIELDS, M)
    expect(issues).toEqual([
      { path: 'columns.1.field', message: 'repetido sku' },
      { path: 'defaults.0.field', message: 'sin default sku' },
      { path: 'defaults.1.value', message: 'valor brand' },
    ])
  })
})

describe('resultado de la importación', () => {
  it('cuenta filas por estado y arma el texto de errores', () => {
    expect(importCounts([{ status: 'OK' }, { status: 'CREATED' }, { status: 'ERROR' }, { status: 'ERROR' }])).toEqual({ ok: 1, created: 1, error: 2 })
    expect(rowErrorText({ sku: 'repetido', salePrice: 'Debe ser un número.' }, (f) => f.toUpperCase())).toBe('SKU: repetido · SALEPRICE: Debe ser un número.')
    expect(rowErrorText(null, (f) => f)).toBe('')
  })
})
