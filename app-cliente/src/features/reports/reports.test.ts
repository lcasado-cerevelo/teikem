// Informes: CSV (comillas, comas, totales), rangos de fechas, páginas del servidor y qué informes ofrece cada compañía.
jest.mock('../portalApi', () => ({ listOrders: jest.fn(), listInvoices: jest.fn(), listMovements: jest.fn(), listStock: jest.fn(), getValuation: jest.fn() }))

import { csvCell, csvFileName, toCsv } from './csv'
import { rangeOf, monthRange } from './ranges'
import { availableReports, endExclusive, MAX_ROWS, pageAll, REPORTS, totalsOf } from './reportDefs'
import { listInvoices, listOrders } from '../portalApi'

describe('csv', () => {
  it('escapa comillas, comas y saltos de línea', () => {
    expect(csvCell('a,b')).toBe('"a,b"')
    expect(csvCell('di "hola"')).toBe('"di ""hola"""')
    expect(csvCell('x\ny')).toBe('"x\ny"')
    expect(csvCell(null)).toBe('')
    expect(csvCell(12.5)).toBe('12.5')
  })

  it('arma encabezados, filas y la fila de totales', () => {
    const cols = [{ key: 'name', title: 'a', type: 'text' as const }, { key: 'qty', title: 'b', type: 'int' as const, total: true }, { key: 'cod', title: 'c', type: 'money' as const, total: true }]
    const csv = toCsv(cols, ['Nombre', 'Cant', 'COD'], [{ name: 'Ana, S.A.', qty: 2, cod: 10.5 }, { name: 'Luis', qty: 3, cod: 0 }], 'Total')
    expect(csv.split('\r\n')).toEqual(['Nombre,Cant,COD', '"Ana, S.A.",2,10.5', 'Luis,3,0', 'Total,5,10.5', ''])
  })

  it('el nombre del archivo lleva el informe y la fecha sin caracteres raros', () => {
    expect(csvFileName('Envíos del período', '2026-10-22')).toBe('teikem-envios-del-periodo-2026-10-22.csv')
  })
})

describe('rangos', () => {
  const now = new Date(2026, 9, 22)
  it('este mes, el mes pasado, los últimos 30 días y este año', () => {
    expect(monthRange(now)).toEqual({ from: '2026-10-01', to: '2026-10-31' })
    expect(rangeOf('lastMonth', now)).toEqual({ from: '2026-09-01', to: '2026-09-30' })
    expect(rangeOf('last30', now)).toEqual({ from: '2026-09-23', to: '2026-10-22' })
    expect(rangeOf('thisYear', now)).toEqual({ from: '2026-01-01', to: '2026-12-31' })
  })
  it('el servidor recibe el día siguiente como fin exclusivo', () => {
    expect(endExclusive('2026-10-31')).toBe('2026-11-01T00:00:00.000Z')
    expect(endExclusive('')).toBeUndefined()
    expect(endExclusive('no')).toBeUndefined()
  })
})

describe('pageAll', () => {
  it('trae todas las páginas hasta completar el total', async () => {
    const load = jest.fn(async (skip: number) => ({ items: Array.from({ length: Math.min(100, 250 - skip) }, (_, i) => skip + i), total: 250 }))
    const res = await pageAll(load)
    expect(res.items).toHaveLength(250)
    expect(res.truncated).toBe(false)
    expect(load).toHaveBeenCalledTimes(3)
  })

  it('se detiene en el tope y avisa que hay más', async () => {
    const res = await pageAll(async (skip) => ({ items: Array.from({ length: 100 }, (_, i) => skip + i), total: MAX_ROWS + 500 }))
    expect(res.items).toHaveLength(MAX_ROWS)
    expect(res.truncated).toBe(true)
  })

  it('una página vacía corta el ciclo', async () => {
    const res = await pageAll(async () => ({ items: [], total: 10 }))
    expect(res.items).toEqual([])
  })
})

describe('informes', () => {
  it('cada compañía ve solo lo que ofrece: transporte, almacén y el valor dentro del almacén', () => {
    const ids = (o: { transport: boolean; warehouse: boolean; canSeeValue: boolean }) => availableReports(o).map((r) => r.id)
    expect(ids({ transport: true, warehouse: false, canSeeValue: false })).toEqual(['shipments', 'byStatus', 'cod', 'invoices'])
    expect(ids({ transport: false, warehouse: true, canSeeValue: false })).toEqual(['stock', 'movements'])
    expect(ids({ transport: false, warehouse: true, canSeeValue: true })).toEqual(['stock', 'movements', 'valuation'])
    expect(ids({ transport: true, warehouse: true, canSeeValue: true })).toHaveLength(REPORTS.length)
  })

  it('suma solo las columnas que suman', () => {
    const cols = [{ key: 'a', title: 'x', type: 'text' as const }, { key: 'n', title: 'y', type: 'qty' as const, total: true }]
    expect(totalsOf(cols, [{ a: 'p', n: 1.25 }, { a: 'q', n: 2.5 }, { a: 'r', n: null }])).toEqual({ n: 3.75 })
  })

  it('envíos por estatus agrupa y ordena por cantidad; COD solo trae los que cobran', async () => {
    ;(listOrders as jest.Mock).mockResolvedValue({
      total: 3,
      items: [
        { statusLabel: 'Entregada', totalPieces: 2, codAmount: 10, consigneeName: 'A', createdAtUtc: '2026-10-02T10:00:00Z' },
        { statusLabel: 'Entregada', totalPieces: 1, codAmount: 0, consigneeName: 'B', createdAtUtc: '2026-10-03T10:00:00Z' },
        { statusLabel: 'En tránsito', totalPieces: 4, codAmount: 5, consigneeName: 'C', createdAtUtc: '2026-10-04T10:00:00Z' },
      ],
    })
    const params = { from: '2026-10-01', to: '2026-10-31', search: '' }
    const by = await REPORTS.find((r) => r.id === 'byStatus')!.run(params, 'es')
    expect(by.rows).toEqual([{ status: 'Entregada', count: 2, pieces: 3, cod: 10 }, { status: 'En tránsito', count: 1, pieces: 4, cod: 5 }])
    const cod = await REPORTS.find((r) => r.id === 'cod')!.run(params, 'es')
    expect(cod.rows.map((r) => r.consignee)).toEqual(['A', 'C'])
    // las fechas viajan al servidor: desde el inicio del día y hasta el inicio del día siguiente
    expect((listOrders as jest.Mock).mock.calls[0][0]).toMatchObject({ fromUtc: '2026-10-01T00:00:00Z', toUtc: '2026-11-01T00:00:00.000Z' })
  })

  it('facturas filtra por la fecha de emisión y deja en cero el saldo de las anuladas', async () => {
    ;(listInvoices as jest.Mock).mockResolvedValue({
      total: 3,
      items: [
        { number: 'F-1', issueDate: '2026-10-05', total: 100, paid: 40, balance: 60, status: 'ISSUED' },
        { number: 'F-2', issueDate: '2026-09-05', total: 50, paid: 0, balance: 50, status: 'ISSUED' },
        { number: 'F-3', issueDate: '2026-10-10', total: 70, paid: 0, balance: 70, status: 'VOID' },
      ],
    })
    const res = await REPORTS.find((r) => r.id === 'invoices')!.run({ from: '2026-10-01', to: '2026-10-31', search: '' }, 'es')
    expect(res.rows.map((r) => [r.number, r.balance])).toEqual([['F-1', 60], ['F-3', 0]])
  })
})
