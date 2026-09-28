import { buildCollectAndPackBody, buildPickLine, canAddPickLine, newPickLineDraft, uniqueBinCodes } from './dispatchLogic'

const PRODUCT = { publicId: 'p1', sku: 'SKU-1', name: 'Producto 1' }

describe('newPickLineDraft', () => {
  it('arranca con cantidad 1 y sin posición', () => {
    const draft = newPickLineDraft(PRODUCT)
    expect(draft).toMatchObject({ qtyText: '1', fromBinCode: '' })
  })
})

describe('canAddPickLine', () => {
  it('necesita cantidad mayor que 0 y una posición escaneada', () => {
    const draft = newPickLineDraft(PRODUCT)
    expect(canAddPickLine(draft)).toBe(false)
    expect(canAddPickLine({ ...draft, fromBinCode: 'B-1' })).toBe(true)
    expect(canAddPickLine({ ...draft, fromBinCode: 'B-1', qtyText: '0' })).toBe(false)
    expect(canAddPickLine({ ...draft, fromBinCode: '   ' })).toBe(false)
  })
})

describe('buildPickLine', () => {
  it('arma la línea con la cantidad numérica y el código de posición recortado', () => {
    const line = buildPickLine({ ...newPickLineDraft(PRODUCT), qtyText: '4', fromBinCode: ' B-9 ' })
    expect(line).toEqual({ productPublicId: 'p1', sku: 'SKU-1', productName: 'Producto 1', quantity: 4, fromBinCode: 'B-9' })
  })
})

describe('uniqueBinCodes', () => {
  it('devuelve los códigos sin repetir, en el orden en que aparecen', () => {
    const lines = [
      buildPickLine({ ...newPickLineDraft(PRODUCT), fromBinCode: 'B-1' }),
      buildPickLine({ ...newPickLineDraft(PRODUCT), fromBinCode: 'B-2' }),
      buildPickLine({ ...newPickLineDraft(PRODUCT), fromBinCode: 'B-1' }),
    ]
    expect(uniqueBinCodes(lines)).toEqual(['B-1', 'B-2'])
  })
})

describe('buildCollectAndPackBody', () => {
  it('arma el cuerpo de collect-and-pack con las líneas ya resueltas y el consignatario elegido', () => {
    const line = { ...buildPickLine({ ...newPickLineDraft(PRODUCT), qtyText: '2', fromBinCode: 'B-3' }), fromBinId: 3 }
    const body = buildCollectAndPackBody('wh-1', 'client-1', 'loc-1', 2, [line])
    expect(body).toEqual({
      warehousePublicId: 'wh-1',
      lines: [{ productPublicId: 'p1', quantity: 2, binId: 3 }],
      pack: {
        order: {
          clientPublicId: 'client-1',
          consigneeLocationPublicId: 'loc-1',
          serviceType: 'STANDARD',
          packages: [{ packageType: 'BOX', pieces: 2 }],
          confirmNow: true,
        },
      },
    })
  })
})
