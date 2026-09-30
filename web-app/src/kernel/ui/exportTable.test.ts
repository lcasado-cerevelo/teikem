import { createElement, type ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import {
  buildExportData,
  csvCell,
  exportCellValue,
  exportFileName,
  nodeToText,
  parseLocaleNumber,
  pdfSafeText,
  sheetName,
  toCsv,
  type ExportableColumn,
} from './exportTable'

interface Row {
  code: string
  qty: number | null
  status: string
  active: boolean
}

/** Componente sin children (como `StatusChip`): no aporta texto, decide `sortValue`. */
function Badge(props: { label: string }): ReactNode {
  return props.label
}

const ROWS: Row[] = [
  { code: 'A-1', qty: 1234, status: 'OPEN', active: true },
  { code: 'B-2', qty: null, status: 'CLOSED', active: false },
]

describe('csvCell', () => {
  it('vacíos y números tal cual', () => {
    expect(csvCell(null)).toBe('')
    expect(csvCell(12.5)).toBe('12.5')
    expect(csvCell('texto')).toBe('texto')
  })

  it('entre comillas con comas, comillas o saltos de línea (comillas duplicadas)', () => {
    expect(csvCell('a,b')).toBe('"a,b"')
    expect(csvCell('dijo "hola"')).toBe('"dijo ""hola"""')
    expect(csvCell('línea 1\nlínea 2')).toBe('"línea 1\nlínea 2"')
    expect(csvCell('a\r\nb')).toBe('"a\r\nb"')
  })

  it('protege contra inyección de fórmulas (= + - @) salvo números', () => {
    expect(csvCell('=SUM(A1:A9)')).toBe("'=SUM(A1:A9)")
    expect(csvCell('+cmd')).toBe("'+cmd")
    expect(csvCell('-abc')).toBe("'-abc")
    expect(csvCell('@user')).toBe("'@user")
    expect(csvCell('=HYPERLINK("x","y")')).toBe(`"'=HYPERLINK(""x"",""y"")"`)
    // un número con signo no es fórmula
    expect(csvCell('-5')).toBe('-5')
    expect(csvCell('+1.234,5')).toBe('"+1.234,5"')
  })
})

describe('toCsv', () => {
  it('encabezados y filas separados por CRLF', () => {
    const csv = toCsv({ headers: ['Código', 'Nota'], numeric: [false, false], rows: [['A', 'x, y'], ['B', null]] })
    expect(csv).toBe('Código,Nota\r\nA,"x, y"\r\nB,')
  })
})

describe('parseLocaleNumber', () => {
  it('lee números formateados en español', () => {
    expect(parseLocaleNumber('12.345,5', 'es')).toBe(12345.5)
    expect(parseLocaleNumber('1.234', 'es')).toBe(1234)
    expect(parseLocaleNumber('-3,25', 'es')).toBe(-3.25)
  })

  it('lee números formateados en inglés', () => {
    expect(parseLocaleNumber('12,345.5', 'en')).toBe(12345.5)
    expect(parseLocaleNumber('+5', 'en')).toBe(5)
    expect(parseLocaleNumber('−7', 'en')).toBe(-7)
  })

  it('null si no es un número', () => {
    expect(parseLocaleNumber('abc', 'es')).toBeNull()
    expect(parseLocaleNumber('12 kg', 'en')).toBeNull()
    expect(parseLocaleNumber('', 'en')).toBeNull()
  })
})

describe('exportFileName', () => {
  const day = new Date(2026, 8, 29)

  it('quita acentos y símbolos y agrega la fecha local', () => {
    expect(exportFileName('Almacenes y Categorías', 'xlsx', day)).toBe('almacenes-y-categorias-2026-09-29.xlsx')
    expect(exportFileName('Órdenes · Recepción (ñ)', 'csv', day)).toBe('ordenes-recepcion-n-2026-09-29.csv')
  })

  it('sin base usa "export"', () => {
    expect(exportFileName(null, 'pdf', day)).toBe('export-2026-09-29.pdf')
    expect(exportFileName('···', 'pdf', day)).toBe('export-2026-09-29.pdf')
  })
})

describe('nodeToText', () => {
  it('texto de cadenas, números, arreglos y children de elementos', () => {
    expect(nodeToText('hola')).toBe('hola')
    expect(nodeToText(42)).toBe('42')
    expect(nodeToText(['a', null, false, 'b'])).toBe('ab')
    expect(nodeToText(createElement('span', null, 'SKU ', createElement('b', null, '123')))).toBe('SKU 123')
    // bloques hermanos quedan separados por un espacio
    expect(nodeToText(createElement('div', null, createElement('p', null, 'uno'), createElement('p', null, 'dos')))).toBe('uno dos')
  })

  it('un componente sin children no aporta texto', () => {
    expect(nodeToText(createElement(Badge, { label: 'Abierta' }))).toBe('')
    expect(nodeToText(null)).toBe('')
  })
})

describe('buildExportData', () => {
  const columns: ExportableColumn<Row>[] = [
    { header: 'Código', cell: (r) => createElement('span', { className: 'ref' }, r.code) },
    { header: 'Cantidad', cell: (r) => (r.qty === null ? '—' : r.qty.toLocaleString('es')), sortValue: (r) => r.qty, align: 'end' },
    // sin texto en la celda: decide sortValue
    { header: 'Estado', cell: (r) => createElement(Badge, { label: r.status }), sortValue: (r) => r.status },
    { header: 'Activo', cell: () => null, sortValue: (r) => r.active },
    // exportValue gana sobre la celda
    { header: 'Clave', cell: () => 'visible', exportValue: (r) => `K-${r.code}` },
    // no exportable
    { header: 'Selección', cell: () => createElement('input', { type: 'checkbox' }), exportable: false },
    // sin encabezado y sin datos: se descarta
    { header: '', cell: () => createElement('svg') },
  ]

  it('arma encabezados, flags numéricos y valores planos', () => {
    const data = buildExportData(columns, ROWS, { locale: 'es', yes: 'Sí', no: 'No' })
    expect(data.headers).toEqual(['Código', 'Cantidad', 'Estado', 'Activo', 'Clave'])
    expect(data.numeric).toEqual([false, true, false, false, false])
    expect(data.rows).toEqual([
      // "1.234" en es con sortValue 1234 → número (Excel lo puede sumar)
      ['A-1', 1234, 'OPEN', 'Sí', 'K-A-1'],
      // "—" = vacío
      ['B-2', null, 'CLOSED', 'No', 'K-B-2'],
    ])
  })

  it('conserva una columna sin encabezado si tiene datos', () => {
    const data = buildExportData([{ header: '', cell: (r: Row) => r.code }], ROWS)
    expect(data.headers).toEqual([''])
    expect(data.rows).toEqual([['A-1'], ['B-2']])
  })

  it('exportCellValue: un texto que no coincide con el sortValue numérico queda como texto', () => {
    const col: ExportableColumn<Row> = { header: 'Qty', cell: () => '12 uds', sortValue: () => 12 }
    expect(exportCellValue(col, ROWS[0], { locale: 'es' })).toBe('12 uds')
  })
})

describe('pdfSafeText y sheetName', () => {
  it('reemplaza lo que no cubre Latin-1', () => {
    expect(pdfSafeText('A → B … “x” – ▲')).toBe('A -> B ... "x" - ')
    expect(pdfSafeText('Año ñ')).toBe('Año ñ')
  })

  it('nombre de hoja sin caracteres prohibidos y hasta 31', () => {
    expect(sheetName('Saldos [2026]: a/b')).toBe('Saldos  2026   a b')
    expect(sheetName('x'.repeat(40))).toHaveLength(31)
    expect(sheetName('')).toBe('Sheet1')
  })
})
