// Etiquetas de producto (2026-10-09): flujo con dependencias falsas — una etiqueta por producto con el SKU exacto como código, orden natural de SKU, datos
// (nombre y categoría), tope de 500, sin productos, y que NUNCA marca nada.
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import { planBinLabels, type BinLabelSpec } from '../../kernel/ui/binLabelPdf'
import type { ProductListItemDto } from './api'
import { PRODUCT_LABELS_MAX, PRODUCT_LABELS_PERMISSION, printProductLabels, productLabelDetails, toProductLabel } from './productLabels'

beforeAll(() => setLang('es'))

const product = (id: number, sku: string, name: string, categoryName: string | null = null) => ({ id, sku, name, categoryName, isActive: true }) as ProductListItemDto
const spec = { title: 'Etiquetas de producto', company: 'Advance Logistics', locale: 'es', size: '4x2', orientation: 'auto' } as const

function flow(items: ProductListItemDto[]) {
  const download = vi.fn(async (s: BinLabelSpec) => planBinLabels(s.bins, s.size, s.orientation))
  const fetchProducts = vi.fn(async () => ({ items, truncated: false }))
  return { download, fetchProducts }
}

describe('productLabels', () => {
  it('permiso: el de la pantalla de productos y su reporte de códigos de barras', () => {
    expect(PRODUCT_LABELS_PERMISSION).toBe('inventory.view')
  })

  it('el código de la etiqueta es el SKU exacto; los datos son el nombre y la categoría (solo los que existen)', () => {
    expect(toProductLabel(product(1, 'SKU-1', 'Tornillo', 'Ferretería'))).toEqual({
      code: 'SKU-1',
      key: 1,
      details: [
        { label: '', value: 'Tornillo' },
        { label: '', value: 'Ferretería' },
      ],
    })
    expect(productLabelDetails(product(2, 'X', '  ', null))).toEqual([])
  })

  it('una etiqueta por producto, en orden natural de SKU (2 antes que 10), y descarga con el tamaño y la orientación', async () => {
    const d = flow([product(1, 'SKU-10', 'B'), product(2, 'SKU-2', 'A'), product(3, 'SKU-1', 'C')])
    const result = await printProductLabels(d, { spec: { ...spec, size: '4x6', orientation: 'rotate' }, t, lang: 'es' })
    expect(result).toMatchObject({ status: 'printed', labels: 3, withoutCode: 0 })
    const sent = d.download.mock.calls[0][0]
    expect(sent.bins.map((b) => b.code)).toEqual(['SKU-1', 'SKU-2', 'SKU-10'])
    expect(sent).toMatchObject({ size: '4x6', orientation: 'rotate', title: 'Etiquetas de producto' })
  })

  it('sin productos no genera nada', async () => {
    const d = flow([])
    expect(await printProductLabels(d, { spec, t, lang: 'es' })).toEqual({ status: 'nothing' })
    expect(d.download).not.toHaveBeenCalled()
  })

  it('pasado el tope avisa sin generar', async () => {
    const d = flow(Array.from({ length: PRODUCT_LABELS_MAX + 1 }, (_, i) => product(i + 1, `S-${i + 1}`, 'x')))
    expect(await printProductLabels(d, { spec, t, lang: 'es' })).toEqual({ status: 'tooMany', total: PRODUCT_LABELS_MAX + 1, max: PRODUCT_LABELS_MAX })
    expect(d.download).not.toHaveBeenCalled()
  })

  it('un SKU con caracteres que Code 128 no admite sale sin código de barras y se avisa', async () => {
    const d = flow([product(1, 'SKU-ñ', 'Pieza'), product(2, 'SKU-2', 'Otra')])
    const result = await printProductLabels(d, { spec, t, lang: 'es' })
    expect(result).toMatchObject({ status: 'printed', labels: 2, withoutCode: 1 })
    if (result.status === 'printed') expect(result.notices.length).toBeGreaterThan(0)
  })

  it('un error al leer se propaga', async () => {
    const d = { download: vi.fn(), fetchProducts: vi.fn(async () => Promise.reject(new Error('boom'))) }
    await expect(printProductLabels(d, { spec, t, lang: 'es' })).rejects.toThrow('boom')
  })
})
