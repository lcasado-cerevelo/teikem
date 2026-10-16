// Lote 68 (kits) — reglas puras del editor de componentes.
import { describe, expect, it } from 'vitest'
import { bestCanBuild, kitDraftError, newKitRow, parseQuantity, rowsFromKit, toKitRequest, type KitDto, type KitRow } from './kitRules'

const row = (over: Partial<KitRow> = {}): KitRow => ({ key: 'k', productPublicId: 'p1', sku: 'A', name: 'Producto A', quantity: '2', ...over })

describe('cantidad', () => {
  it('acepta coma decimal y rechaza vacío, cero y texto', () => {
    expect(parseQuantity('2,5')).toBe(2.5)
    expect(parseQuantity(' 3 ')).toBe(3)
    expect(parseQuantity('')).toBeNull()
    expect(parseQuantity('0')).toBeNull()
    expect(parseQuantity('x')).toBeNull()
  })
})

describe('kitDraftError', () => {
  it('listo cuando hay productos, cantidades válidas y sin repetir', () => {
    expect(kitDraftError([row(), row({ key: 'k2', productPublicId: 'p2' })])).toBeNull()
  })
  it('una fila sin producto, una cantidad inválida y un producto repetido se dicen antes de enviar', () => {
    expect(kitDraftError([newKitRow('a')])).toBe('product')
    expect(kitDraftError([row(), newKitRow('b')])).toBe('product')
    expect(kitDraftError([row({ quantity: '0' })])).toBe('quantity')
    expect(kitDraftError([row(), row({ key: 'k2' })])).toBe('duplicate')
    expect(kitDraftError([])).toBe('need')
  })
})

describe('cuerpo', () => {
  it('manda isKit, ajustable y solo las filas con producto, con la cantidad como número', () => {
    expect(toKitRequest([row({ quantity: '1,5' }), newKitRow('b')], true)).toEqual({ isKit: true, adjustable: true, components: [{ productPublicId: 'p1', quantity: 1.5 }] })
  })
})

describe('kit existente', () => {
  const kit: KitDto = {
    productPublicId: 'k', sku: 'KIT-1', name: 'Kit', isKit: true, adjustable: false,
    components: [{ productPublicId: 'p1', sku: 'A', name: 'A', quantity: 2, sortOrder: 1, isActive: true }],
    availability: [{ warehousePublicId: 'w1', warehouseCode: 'W1', canBuild: 3 }, { warehousePublicId: 'w2', warehouseCode: 'W2', canBuild: 7 }],
  }
  it('las filas parten de los componentes (o de una fila vacía) y el mejor almacén resume', () => {
    expect(rowsFromKit(kit, (i) => `r${i}`)).toEqual([{ key: 'r0', productPublicId: 'p1', sku: 'A', name: 'A', quantity: '2' }])
    expect(rowsFromKit(undefined, (i) => `r${i}`)).toEqual([newKitRow('r0')])
    expect(bestCanBuild(kit)).toBe(7)
    expect(bestCanBuild(undefined)).toBe(0)
  })
})
