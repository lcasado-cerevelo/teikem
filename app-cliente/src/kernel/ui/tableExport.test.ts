// Exportar tablas: solo las columnas con valor exportable, comillas y comas escapadas, booleanos en palabras, y la paginación trae todo lo filtrado hasta el tope.
import { setLang } from '../i18n/i18n'
import { buildExportData, csvDay, exportFileName, fetchAll, filtersSentence, generatedText, MAX_EXPORT_ROWS, toCsvText } from './tableExport'

describe('toCsvText', () => {
  it('exporta las columnas con csv, en el orden de la tabla, con encabezados y escapes', () => {
    const rows = [{ a: 'Farmacia, Norte', n: 2, ok: true }, { a: 'Colmado "Sur"', n: null, ok: false }]
    const csv = toCsvText([{ title: 'Nombre', csv: (r: (typeof rows)[number]) => r.a }, { title: 'Acción' }, { title: 'Piezas', csv: (r) => r.n }, { title: 'Correo', csv: (r) => r.ok }], rows)
    expect(csv).toBe('Nombre,Piezas,Correo\r\n"Farmacia, Norte",2,Sí\r\n"Colmado ""Sur""",,No\r\n')
    expect(toCsvText([{ title: 'X', csv: (r: { ok: boolean }) => r.ok }], [{ ok: true }], 'Yes', 'No')).toBe('X\r\nYes\r\n')
  })

  it('csvDay deja solo el día', () => {
    expect(csvDay('2026-10-23T14:00:00Z')).toBe('2026-10-23')
    expect(csvDay(null)).toBe('')
  })
})

describe('fetchAll', () => {
  it('pide páginas hasta traer el total del filtro y dice si se pasó del tope', async () => {
    const calls: number[] = []
    const r = await fetchAll(async (skip, take) => {
      calls.push(skip)
      return { items: Array.from({ length: Math.min(take, 250 - skip) }, (_, i) => skip + i), total: 250 }
    })
    expect(calls).toEqual([0, 100, 200])
    expect(r.items.length).toBe(250)
    expect(r.truncated).toBe(false)
    const big = await fetchAll(async (skip, take) => ({ items: Array.from({ length: take }, (_, i) => skip + i), total: MAX_EXPORT_ROWS + 10 }))
    expect(big.items.length).toBe(MAX_EXPORT_ROWS)
    expect(big.truncated).toBe(true)
  })
})

describe('buildExportData', () => {
  it('deja los números como números, los booleanos en palabras, el vacío como null y marca las columnas numéricas', () => {
    const rows = [{ n: 'A', q: 2, ok: true, d: '2026-10-23' }, { n: 'B', q: 10.5, ok: false, d: '' }]
    const data = buildExportData([{ title: 'Nombre', csv: (r: (typeof rows)[number]) => r.n }, { title: 'Acción' }, { title: 'Cant.', csv: (r) => r.q }, { title: 'Sí', csv: (r) => r.ok }, { title: 'Fecha', csv: (r) => r.d }], rows)
    expect(data.headers).toEqual(['Nombre', 'Cant.', 'Sí', 'Fecha'])
    expect(data.rows).toEqual([['A', 2, 'Sí', '2026-10-23'], ['B', 10.5, 'No', null]])
    expect(data.numeric).toEqual([false, true, false, false])
  })
})

describe('encabezado del archivo', () => {
  beforeEach(() => setLang('es'))

  it('el nombre lleva el título sin acentos, la fecha y la extensión del formato', () => {
    expect(exportFileName('Mis envíos', 'pdf', '2026-10-23')).toBe('teikem-mis-envios-2026-10-23.pdf')
    expect(exportFileName('Mis envíos', 'xlsx', '2026-10-23')).toBe('teikem-mis-envios-2026-10-23.xlsx')
    expect(exportFileName('Mis envíos', 'csv', '2026-10-23')).toBe('teikem-mis-envios-2026-10-23.csv')
  })

  it('la oración de filtros lista lo aplicado o dice «Sin filtros»', () => {
    expect(filtersSentence([false, null, undefined])).toBe('Sin filtros')
    expect(filtersSentence(['Búsqueda «farm»', false, 'Fechas 2026-10-01 a 2026-10-15'])).toBe('Filtros: Búsqueda «farm» · Fechas 2026-10-01 a 2026-10-15')
  })

  it('«Generado el …» sale en el idioma de la persona', () => {
    expect(generatedText(new Date(2026, 9, 23, 10, 15), 'es')).toMatch(/^Generado el .*2026/)
    expect(generatedText(new Date(2026, 9, 23, 10, 15), 'en')).toMatch(/^Generated on .*2026/)
  })
})
