// Lote F-C3 — reglas puras de la importación de órdenes: plantilla estándar, reglas de la definición (consignatario y paquete, en columna
// o en defaults), plantillas que sirven a un cliente, cuerpo del confirm (permisos y dependencias de las opciones) y estado de cada fila.
import { describe, expect, it } from 'vitest'
import {
  buildConfirmRequest,
  confirmSignature,
  failedRows,
  orderStandardTemplateRequest,
  orderTemplateIssues,
  rowStatus,
  templatesForClient,
  type OrderImportRow,
} from './orderImportRules'

const FIELDS = [
  { field: 'orderNumber', required: false, defaultAllowed: false },
  { field: 'serviceType', required: false, defaultAllowed: true },
  { field: 'consigneeName', required: false, defaultAllowed: false },
  { field: 'consigneeCode', required: false, defaultAllowed: true },
  { field: 'packageType', required: false, defaultAllowed: true },
  { field: 'pieces', required: false, defaultAllowed: true },
]
const M = {
  columnsRequired: 'Indique al menos una columna.',
  consignee: 'La plantilla debe incluir consigneeName o consigneeCode.',
  packages: 'La plantilla debe indicar el paquete: packageType o pieces, en una columna o en defaults.',
  repeated: (f: string) => `El campo ${f} está repetido.`,
  noDefault: (f: string) => `El campo ${f} no admite valor por defecto.`,
  defaultValueRequired: (f: string) => `Escriba el valor por defecto de ${f}.`,
}
const cols = (...fields: string[]) => fields.map((field) => ({ field }))

describe('plantilla estándar', () => {
  it('trae lo que exige el servidor (consignatario + paquete) en columnas por posición', () => {
    const r = orderStandardTemplateRequest('Formato estándar')
    expect(r.name).toBe('Formato estándar')
    expect(r.delimiter).toBe(',')
    expect(r.hasHeader).toBe(true)
    expect(r.columns).toEqual([
      { position: 1, field: 'consigneeName' },
      { position: 2, field: 'line1' },
      { position: 3, field: 'city' },
      { position: 4, field: 'serviceType' },
      { position: 5, field: 'packageType' },
      { position: 6, field: 'pieces' },
      { position: 7, field: 'codAmount' },
    ])
    // y pasa su propia validación
    expect(orderTemplateIssues({ columns: cols(...r.columns!.map((c) => c.field!)), defaults: [] }, FIELDS, M)).toEqual([])
  })
})

describe('orderTemplateIssues (espejo de ImportMapping.ValidateColumns)', () => {
  it('sin columnas', () => {
    expect(orderTemplateIssues({ columns: cols('', ''), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: M.columnsRequired }])
  })
  it('sin consignatario (nombre o número), en columna o en defaults', () => {
    expect(orderTemplateIssues({ columns: cols('pieces', 'packageType'), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: M.consignee }])
    expect(orderTemplateIssues({ columns: cols('pieces', 'packageType'), defaults: [{ field: 'consigneeCode', value: 'C-001' }] }, FIELDS, M)).toEqual([])
    expect(orderTemplateIssues({ columns: cols('consigneeCode', 'pieces'), defaults: [] }, FIELDS, M)).toEqual([])
  })
  it('sin paquete (tipo o piezas), en columna o en defaults', () => {
    expect(orderTemplateIssues({ columns: cols('consigneeName'), defaults: [] }, FIELDS, M)).toEqual([{ path: 'columns', message: M.packages }])
    expect(orderTemplateIssues({ columns: cols('consigneeName'), defaults: [{ field: 'packageType', value: 'BOX' }] }, FIELDS, M)).toEqual([])
    expect(orderTemplateIssues({ columns: cols('consigneeName', 'pieces'), defaults: [] }, FIELDS, M)).toEqual([])
  })
  it('campo repetido, valor por defecto de un campo que no lo admite y valor vacío', () => {
    const issues = orderTemplateIssues(
      { columns: cols('consigneeName', 'pieces', 'pieces'), defaults: [{ field: 'orderNumber', value: 'A' }, { field: 'serviceType', value: ' ' }] },
      FIELDS,
      M,
    )
    expect(issues).toContainEqual({ path: 'columns.2.field', message: 'El campo pieces está repetido.' })
    expect(issues).toContainEqual({ path: 'defaults.0.field', message: 'El campo orderNumber no admite valor por defecto.' })
    expect(issues).toContainEqual({ path: 'defaults.1.value', message: 'Escriba el valor por defecto de serviceType.' })
  })
})

describe('templatesForClient', () => {
  it('las generales y las del cliente; sin cliente, solo las generales', () => {
    const list = [{ publicId: 'g' }, { publicId: 'a', clientPublicId: 'c-1' }, { publicId: 'b', clientPublicId: 'c-2' }]
    expect(templatesForClient(list, 'c-1').map((t) => t.publicId)).toEqual(['g', 'a'])
    expect(templatesForClient(list, '').map((t) => t.publicId)).toEqual(['g'])
  })
})

describe('buildConfirmRequest', () => {
  const off = { overrideCredit: false, confirmDuplicateInvoice: false }
  it('sin nada marcado el cuerpo va vacío (se crean todas las válidas Sometidas)', () => {
    expect(buildConfirmRequest(off, true)).toEqual({})
  })
  it('manda solo lo marcado', () => {
    expect(buildConfirmRequest({ ...off, confirmDuplicateInvoice: true }, false)).toEqual({ confirmDuplicateInvoice: true })
  })
  it('overrideCredit exige el permiso orders.credit_override (si no, 403): sin él nunca se manda, y nunca se manda confirmNow', () => {
    expect(buildConfirmRequest({ overrideCredit: true, confirmDuplicateInvoice: false }, false)).toEqual({})
    expect(buildConfirmRequest({ overrideCredit: true, confirmDuplicateInvoice: true }, true)).toEqual({
      overrideCredit: true,
      confirmDuplicateInvoice: true,
    })
  })
  it('la firma distingue las opciones (otra operación = otra Idempotency-Key)', () => {
    const a = confirmSignature('b-1', {})
    expect(confirmSignature('b-1', {})).toBe(a)
    expect(confirmSignature('b-1', { overrideCredit: true })).not.toBe(a)
    expect(confirmSignature('b-2', {})).not.toBe(a)
  })
})

describe('estado de las filas', () => {
  const row = (r: Partial<OrderImportRow>): OrderImportRow => ({ row: 1, line: 2, isValid: true, ...r })
  it('antes de confirmar: válida, con aviso o con errores', () => {
    expect(rowStatus(row({}), false)).toBe('ok')
    expect(rowStatus(row({ warnings: ['aviso'] }), false)).toBe('warn')
    expect(rowStatus(row({ isValid: false, errors: { pieces: 'x' } }), false)).toBe('bad')
  })
  it('después: creada, fallida u omitida', () => {
    expect(rowStatus(row({ orderPublicId: 'o-1', orderNumber: 'ORD-1' }), true)).toBe('created')
    expect(rowStatus(row({ error: 'x' }), true)).toBe('failed')
    expect(rowStatus(row({ skipped: true }), true)).toBe('skipped')
    expect(failedRows([row({ orderPublicId: 'o-1' }), row({ error: 'x' }), row({ skipped: true })])).toHaveLength(1)
    expect(failedRows(null)).toEqual([])
  })
})
