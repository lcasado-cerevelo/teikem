// Lote F14 — reportes de códigos de barras: agrupación y orden de productos (por categoría con su ruta, "Sin categoría" al
// final, SKU en orden natural) y de posiciones (por el primer número del código: 01 y 1 juntos, 2 antes que 10, GENERAL en
// "Otras posiciones"), filas (SKU exacto en el código; zona y almacén de la posición) y "Filtros aplicados".
import { beforeAll, describe, expect, it } from 'vitest'
import { t } from '../../kernel/i18n'
import { setLang } from '../../kernel/i18n/i18n'
import type { ProductListItemDto, WarehouseBinDto } from './api'
import {
  binBarcodeRow,
  binFirstNumber,
  buildBinBarcodeReport,
  buildProductBarcodeReport,
  groupBinsForBarcodes,
  groupProductsForBarcodes,
  productBarcodeRow,
} from './barcodeReports'
import { EMPTY_PRODUCT_FILTERS } from './productFilters'

beforeAll(() => setLang('es'))

const product = (sku: string, categoryId: number | null, extra: Partial<ProductListItemDto> = {}): ProductListItemDto =>
  ({ id: 1, publicId: sku, sku, name: `Producto ${sku}`, categoryId, categoryName: categoryId ? `Cat ${categoryId}` : null, isActive: true, ...extra }) as ProductListItemDto
const bin = (code: string, extra: Partial<WarehouseBinDto> = {}): WarehouseBinDto => ({ id: 1, zoneId: 7, zoneCode: 'RSV', code, isActive: true, ...extra }) as WarehouseBinDto

const PATHS = new Map([
  ['1', 'Médico / Diabetes'],
  ['2', 'Ferretería'],
])

describe('productos · agrupación y orden', () => {
  it('por categoría (título = ruta), grupos en orden natural, "Sin categoría" al final, SKU en orden natural', () => {
    const groups = groupProductsForBarcodes(
      [product('SKU-10', 1), product('X-1', null), product('SKU-2', 1), product('tor-9', 2), product('TOR-10', 2), product('SKU-1', 1), product('A-1', 3)],
      PATHS,
      'es',
    )
    expect(groups.map((g) => g.category)).toEqual(['Cat 3', 'Ferretería', 'Médico / Diabetes', null])
    expect(groups[2].items.map((p) => p.sku)).toEqual(['SKU-1', 'SKU-2', 'SKU-10'])
    expect(groups[1].items.map((p) => p.sku)).toEqual(['tor-9', 'TOR-10'])
    expect(groups[3].items.map((p) => p.sku)).toEqual(['X-1'])
  })

  it('reporte: una sola lista corrida en el orden del filtro (sin títulos), el código lleva el SKU exacto (no el código de barras del producto), inactivo marcado', () => {
    const items = [product('GLU-100', 1, { barcode: '7501234567890', name: 'Medidor' }), product('OLD-1', null, { isActive: false })]
    const spec = buildProductBarcodeReport(items, true, {
      t,
      lang: 'es',
      company: 'Advance Logistics',
      user: 'Ana',
      filters: { ...EMPTY_PRODUCT_FILTERS, name: 'tor', warehouses: ['w1'] },
      names: { warehouses: new Map([['w1', 'ALM-01 · Principal']]), categories: PATHS },
    }, 2)
    expect(spec.title).toBe('Códigos de barras de productos')
    expect(spec.columns).toBe(2)
    expect(spec.groups.map((g) => g.title)).toEqual([''])
    expect(spec.groups[0].rows[0]).toEqual({ value: 'GLU-100', title: 'GLU-100', description: 'Medidor', meta: null })
    expect(spec.groups[0].rows[1].meta).toBe('Inactivo')
    // el orden es el que devuelve el filtro, no SKU ni categoría
    const reversed = buildProductBarcodeReport([product('Z-9', 2), product('A-1', 1), product('M-5', null)], false, { t, lang: 'es', company: 'x', user: 'y', filters: EMPTY_PRODUCT_FILTERS, names: { warehouses: new Map(), categories: PATHS } })
    expect(reversed.groups[0].rows.map((r) => r.value)).toEqual(['Z-9', 'A-1', 'M-5'])
    // almacén sin la nota de cantidades (este reporte no tiene cantidades)
    expect(spec.filters).toEqual([
      { label: 'Almacén', value: 'ALM-01 · Principal' },
      { label: 'Nombre', value: 'contiene «tor»' },
    ])
    expect(spec.notices).toEqual(['El reporte incluye solo los primeros 2 productos (el servidor no devolvió más). Vuelva a generarlo.'])
    expect(productBarcodeRow({ sku: null, name: null, isActive: true }, t).value).toBe('')
  })
})

describe('posiciones · agrupación y orden', () => {
  it('primer número: primera secuencia de dígitos, clave sin ceros a la izquierda', () => {
    expect(binFirstNumber('A01-R01-N1-P01')).toEqual({ key: '1', text: '01' })
    expect(binFirstNumber('R1')).toEqual({ key: '1', text: '1' })
    expect(binFirstNumber('B-06')).toEqual({ key: '6', text: '06' })
    expect(binFirstNumber('00')).toEqual({ key: '0', text: '00' })
    expect(binFirstNumber('GENERAL')).toBeNull()
  })

  it('01 y 1 juntos (título del primer código), 2 antes que 10, GENERAL en "Otras posiciones" al final, orden natural dentro', () => {
    const groups = groupBinsForBarcodes(
      ['R1', 'A01-R10', 'GENERAL', 'B-10', 'A01-R2', 'C2-1', 'MUELLE', 'A01-R01-N1-P01', 'R1-B', 'A2'].map((c) => bin(c)),
      'es',
    )
    expect(groups.map((g) => [g.label, g.bins.map((b) => b.code)])).toEqual([
      ['01', ['A01-R01-N1-P01', 'A01-R2', 'A01-R10', 'R1', 'R1-B']],
      ['2', ['A2', 'C2-1']],
      ['10', ['B-10']],
      [null, ['GENERAL', 'MUELLE']],
    ])
  })

  it('números largos se comparan como número sin desbordar; el grupo se titula con el texto del primer código', () => {
    const groups = groupBinsForBarcodes([bin('Z99999999999999999999'), bin('Z100000000000000000000'), bin('X3'), bin('A003')], 'es')
    expect(groups.map((g) => g.label)).toEqual(['003', '99999999999999999999', '100000000000000000000'])
  })

  it('reporte: títulos "Grupo 01 (n)" y "Otras posiciones (n)", celda con zona (nombre) y almacén, marcas de provisional e inactiva', () => {
    const ctx = {
      t,
      lang: 'es',
      filters: [{ label: 'Almacén', value: 'ALM-01 (Principal)' }],
      warehouse: { code: 'ALM-01', name: 'Principal' },
      zones: [{ id: 7, code: 'RSV', name: 'Reserva' }],
    }
    const spec = buildBinBarcodeReport([bin('A01-1'), bin('GENERAL', { isActive: false }), bin('A1-2', { isProvisional: true })], false, ctx)
    expect(spec.title).toBe('Códigos de barras de posiciones')
    expect(spec.groups.map((g) => g.title)).toEqual(['Grupo 01 (2)', 'Otras posiciones (1)'])
    expect(spec.groups[0].rows.map((r) => r.value)).toEqual(['A01-1', 'A1-2'])
    expect(spec.groups[0].rows[1].meta).toBe('Zona RSV (Reserva) · Almacén ALM-01 · Pendiente de revisión')
    expect(spec.groups[1].rows[0].meta).toBe('Zona RSV (Reserva) · Almacén ALM-01 · Inactiva')
    expect(spec.filters).toEqual(ctx.filters)
    expect(spec.notices).toEqual([])
    expect(binBarcodeRow(bin('X', { zoneId: 99, zoneCode: 'PCK' }), { ...ctx, warehouse: {} }).meta).toBe('Zona PCK')
    const truncated = buildBinBarcodeReport([bin('A1')], true, ctx)
    expect(truncated.notices).toEqual(['El reporte incluye solo las primeras 1 posiciones (el servidor no devolvió más). Vuelva a generarlo.'])
  })
})
