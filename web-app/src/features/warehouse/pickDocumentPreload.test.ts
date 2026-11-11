import { describe, expect, it } from 'vitest'
import { preloadFromDocument } from './pickDocumentView'

const line = (over: Record<string, unknown>) => ({ productPublicId: 'p1', sku: 'A-1', productName: 'Producto', ordered: 5, reserved: 5, shipped: 0, locations: [], trackingTypeCode: 'NONE', ...over })

describe('precarga del formulario de recolección desde un documento', () => {
  it('una línea por posición reservada, con su cantidad y lote', () => {
    const p = preloadFromDocument(
      { number: 'OV-00002', warehousePublicId: 'w1', lines: [line({ trackingTypeCode: 'LOT', locations: [{ binId: 7, binCode: 'A-01', lotId: 3, lotNumber: 'L1', quantity: 2 }, { binId: 9, binCode: 'B-02', lotId: null, quantity: 3 }] })] } as never,
      1,
    )
    expect(p?.warehousePublicId).toBe('w1')
    expect(p?.lines.map((l) => [l.binId, l.lotId, l.quantity])).toEqual([['7', '3', 2], ['9', '', 3]])
  })

  it('sin posición guardada va una sola línea con lo reservado (el servidor elige por FEFO)', () => {
    const p = preloadFromDocument({ number: 'OV-1', warehousePublicId: 'w1', lines: [line({})] } as never, 1)
    expect(p?.lines).toHaveLength(1)
    expect(p?.lines[0]).toMatchObject({ quantity: 5, binId: '', lotId: '', sku: 'A-1' })
  })

  it('el lote solo se manda si el producto lo maneja', () => {
    const p = preloadFromDocument({ number: 'OV-1', warehousePublicId: 'w1', lines: [line({ locations: [{ binId: 7, binCode: 'A-01', lotId: 3, quantity: 5 }] })] } as never, 1)
    expect(p?.lines[0].lotId).toBe('')
  })

  it('lo ya sacado o sin reserva no se carga; sin nada reservado es null', () => {
    expect(preloadFromDocument({ number: 'OV-1', warehousePublicId: null, lines: [line({ reserved: 0, shipped: 5 })] } as never, 1)).toBeNull()
    expect(preloadFromDocument({ number: 'OV-1', warehousePublicId: null, lines: [] } as never, 1)).toBeNull()
  })
})
