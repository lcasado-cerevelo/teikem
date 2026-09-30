// Exportación de la lista de Recibo con sus líneas: columnas del recibo (sin "Líneas", con "Confirmado" y "Diferencia del
// recibo"), filas planas por línea para Excel/CSV (un recibo sin líneas = una fila con las de línea vacías) y bloques del
// PDF (número y estatus de título; líneas con diferencia con signo y Lote/Serie).
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { buildGroupedExportData, flattenGroupedExport, groupedCellText } from '../../kernel/ui/exportGrouped'
import type { ReceiptListItemDto } from './api'
import { lotSerialText, receiptExportChildren, receiptExportColumns, receiptLineExportColumns } from './receiptExport'

beforeAll(() => setLang('es'))

const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)

type Line = NonNullable<ReceiptListItemDto['lines']>[number]
const line = (over: Partial<Line>): Line =>
  ({ id: 1, productPublicId: 'p', sku: 'SKU', productName: 'Producto', trackingTypeCode: 'NONE', expectedQty: 5, receivedQty: 5, varianceQty: 0, serialNumbers: [], allocatedToCrossDock: 0, ...over }) as Line

const receipt = (over: Partial<ReceiptListItemDto>): ReceiptListItemDto =>
  ({
    id: 1,
    publicId: 'r',
    number: 'REC-00001',
    typeCode: 'BLIND',
    type: 'Ciego',
    origin: 'BLIND',
    originRef: null,
    senderName: null,
    warehouseCode: 'ALM-01',
    statusCode: 'RECEIVING',
    status: 'Recibiendo',
    lineCount: 0,
    varianceQty: 0,
    createdAtUtc: '2026-09-29T15:00:00Z',
    receivedAtUtc: null,
    carrier: null,
    reference: null,
    expectedDate: null,
    lines: [],
    ...over,
  }) as ReceiptListItemDto

const RECEIPTS: ReceiptListItemDto[] = [
  receipt({
    number: 'REC-00010',
    status: 'Completado con diferencia',
    origin: 'PO',
    originRef: 'PO-00003',
    senderName: 'Proveedor Uno',
    carrier: 'DHL',
    reference: 'GUIA-7',
    expectedDate: '2026-09-30',
    receivedAtUtc: '2026-09-30T16:00:00Z',
    lineCount: 2,
    varianceQty: -2,
    lines: [
      line({ id: 11, sku: 'AB-1', productName: 'Guantes', expectedQty: 5, receivedQty: 3, varianceQty: -2 }),
      line({ id: 12, sku: 'AB-2', productName: 'Jeringas', expectedQty: 10, receivedQty: 10, varianceQty: 0, lotNumber: 'L-77' }),
    ],
  }),
  receipt({ number: 'REC-00011', lines: [] }),
]

describe('receiptExport', () => {
  it('columnas del recibo: sin "Líneas"; con "Confirmado" y "Diferencia del recibo" (numérica con signo)', () => {
    const cols = receiptExportColumns(t, 'es')
    const headers = cols.map((c) => c.header)
    expect(headers).not.toContain('Líneas')
    expect(headers).toEqual([
      'Número',
      'Estatus',
      'Tipo',
      'Origen',
      'Documento',
      'Remitente',
      'Almacén',
      'Transporte',
      'Referencia',
      'Llegada esperada',
      'Creado',
      'Confirmado',
      'Diferencia del recibo',
    ])
    const variance = cols[cols.length - 1]
    expect(variance).toMatchObject({ align: 'end', signed: true })
    expect(receiptLineExportColumns(t, 'es').map((c) => c.header)).toEqual(['SKU', 'Producto', 'Esperado', 'Recibido', 'Diferencia', 'Lote/Serie'])
  })

  it('Excel/CSV: una fila por línea repitiendo el recibo; el recibo sin líneas sale con las columnas de línea vacías', () => {
    const flat = flattenGroupedExport(buildGroupedExportData(receiptExportColumns(t, 'es'), RECEIPTS, receiptExportChildren(t, 'es'), { locale: 'es' }))
    expect(flat.headers).toHaveLength(13 + 6)
    expect(flat.headers).not.toContain('Líneas')
    expect(flat.rows).toHaveLength(3)
    const [first, second, empty] = flat.rows
    expect(first.slice(0, 2)).toEqual(['REC-00010', 'Completado con diferencia'])
    expect(first[3]).toBe('Orden de compra')
    expect(first[4]).toBe('PO-00003')
    expect(first[12]).toBe(-2)
    expect(first.slice(13)).toEqual(['AB-1', 'Guantes', 5, 3, -2, null])
    expect(second.slice(0, 13)).toEqual(first.slice(0, 13))
    expect(second.slice(13)).toEqual(['AB-2', 'Jeringas', 10, 10, 0, 'L-77'])
    expect(empty[0]).toBe('REC-00011')
    expect(empty.slice(13)).toEqual([null, null, null, null, null, null])
  })

  it('PDF: un bloque por recibo con número y estatus de título, campos de la banda y la diferencia con signo', () => {
    const data = buildGroupedExportData(receiptExportColumns(t, 'es'), RECEIPTS, receiptExportChildren(t, 'es'), { locale: 'es' })
    expect(data.blocks).toHaveLength(2)
    const [withLines, noLines] = data.blocks
    expect(withLines.title).toEqual(['REC-00010', 'Completado con diferencia'])
    expect(withLines.fields.map((f) => f.label)).toEqual([
      'Tipo',
      'Origen',
      'Documento',
      'Remitente',
      'Almacén',
      'Transporte',
      'Referencia',
      'Llegada esperada',
      'Creado',
      'Confirmado',
      'Diferencia del recibo',
    ])
    const diff = withLines.fields[withLines.fields.length - 1]
    expect(groupedCellText(diff.value, 'es', diff.signed)).toBe('-2')
    expect(withLines.children).toHaveLength(2)
    expect(groupedCellText(withLines.children[0][4], 'es', data.child.signed[4])).toBe('-2')
    expect(noLines.children).toEqual([])
    expect(data.emptyText).toBe('Sin líneas')
  })

  it('Lote/Serie: el lote o, si no hay, las series separadas por coma', () => {
    expect(lotSerialText({ lotNumber: 'L1', serialNumbers: ['S1'] })).toBe('L1')
    expect(lotSerialText({ lotNumber: null, serialNumbers: ['S1', 'S2'] })).toBe('S1, S2')
    expect(lotSerialText({ lotNumber: null, serialNumbers: [] })).toBe('')
  })
})
