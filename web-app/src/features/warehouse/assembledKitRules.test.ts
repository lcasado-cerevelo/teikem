// Lote 232 — reglas puras del kit ARMADO. Los primeros casos son los vectores de `AssembledKitTests.Pure_rules_decide_tracking_expiry_and_how_many_kits_fit`
// (tests/Teikem.Tests/AssembledKitTests.cs): la pantalla calcula igual que el servidor el rastreo, el vencimiento del lote del kit, cuántos kits
// cubre una cantidad, la cantidad válida, «se pueden armar N» en un solo armado y el reparto del regreso al desarmar.
import { describe, expect, it } from 'vitest'
import { cartToLines, setCartQuantity, type CatalogItem } from '../sales/wizard/wizardRules'
import {
  assembleRequest,
  assembleSummary,
  assembledKitCreateFields,
  assembledPiecesProblem,
  canAssembleInOne,
  canAssembleTotal,
  choiceKey,
  decideTracking,
  disassembleRequest,
  disassembleReturns,
  kitExpiry,
  kitQuantityProblem,
  kitsCoveredBy,
  parseChoiceKey,
  pieceStocksFromExitOptions,
  piecesFromAssembled,
  piecesSignature,
  problemMessages,
  splitReturn,
  type PieceInfo,
} from './assembledKitRules'
import type { KitPiece } from './kitPieces'

const piece = (key: string, productPublicId: string | null, quantity = '1', sku = ''): KitPiece => ({
  key,
  productPublicId,
  sku,
  name: '',
  quantity,
  notSubstitutable: false,
  salePrice: null,
})

describe('vectores del servidor (AssembledKitTests)', () => {
  it('rastreo, vencimiento, kits cubiertos, cantidad, un lote por pieza y reparto del regreso', () => {
    expect(decideTracking(['NONE', 'LOT'])).toBe('LOT')
    expect(decideTracking(['NONE'])).toBe('NONE')
    expect(kitExpiry([null, '2026-05-01', '2026-01-01'])).toBe('2026-01-01')
    expect(kitExpiry([null])).toBeNull()
    expect(kitsCoveredBy(5, 2)).toBe(2)
    expect(kitsCoveredBy(-1, 2)).toBe(0)
    expect(kitQuantityProblem(null)).toBe('kitQtyRequired')
    expect(kitQuantityProblem(0)).toBe('kitQtyInvalid')
    expect(kitQuantityProblem(100_001)).not.toBeNull()
    expect(kitQuantityProblem(3)).toBeNull()
    // un lote por pieza: A tiene lotes de 5 y 3 (alcanza 2 kits en uno solo de 2 c/u); B sin lote 10 / 1
    expect(
      canAssembleInOne([
        { qtyPerKit: 2, lotTracked: true, byLot: { 1: 5, 2: 3 }, total: 8 },
        { qtyPerKit: 1, lotTracked: false, byLot: {}, total: 10 },
      ]),
    ).toBe(2)
    expect(splitReturn(3, [{ taken: 4, returned: 2 }, { taken: 5, returned: 0 }])).toEqual([
      [0, 2],
      [1, 1],
    ])
  })
})

describe('reglas de la pantalla', () => {
  it('cantidad de kits: no entera, tope y decimales que no deben dar de menos', () => {
    expect(kitQuantityProblem(1.5)).toBe('kitQtyInvalid')
    expect(kitQuantityProblem(-2)).toBe('kitQtyInvalid')
    expect(kitQuantityProblem(100_000)).toBeNull()
    expect(kitQuantityProblem(100_001)).toBe('kitQtyTooLarge')
    expect(kitsCoveredBy(0.3, 0.1)).toBe(3)
    expect(kitsCoveredBy(5, 0)).toBe(0)
  })

  it('«se pueden armar»: en un armado (mejor lote) y juntando lotes; sin piezas, 0', () => {
    const stocks = [
      { qtyPerKit: 2, lotTracked: true, byLot: { 1: 5, 2: 3 }, total: 8 },
      { qtyPerKit: 1, lotTracked: false, byLot: {}, total: 10 },
    ]
    expect(canAssembleInOne(stocks)).toBe(2)
    expect(canAssembleTotal(stocks)).toBe(4)
    expect(canAssembleInOne([])).toBe(0)
    expect(canAssembleTotal([])).toBe(0)
    expect(canAssembleInOne([{ qtyPerKit: 1, lotTracked: true, byLot: {}, total: 0 }])).toBe(0)
  })

  it('existencia por pieza desde las opciones de salida del almacén (suma por lote; sin disponible no cuenta)', () => {
    const rows = [
      { productPublicId: 'A', binId: 1, binCode: 'A-01', lotId: 1, lotNumber: 'L1', available: 3, rank: 1 },
      { productPublicId: 'A', binId: 2, binCode: 'A-02', lotId: 1, lotNumber: 'L1', available: 2, rank: 2 },
      { productPublicId: 'A', binId: 2, binCode: 'A-02', lotId: 2, lotNumber: 'L2', available: 3, rank: 3 },
      { productPublicId: 'B', binId: 3, binCode: 'B-01', available: 10, rank: 1 },
      { productPublicId: 'B', binId: 4, binCode: 'B-02', available: 0, rank: 2 },
    ]
    const stocks = pieceStocksFromExitOptions(
      [
        { productPublicId: 'A', quantity: 2, lotTracked: true },
        { productPublicId: 'B', quantity: 1, lotTracked: false },
      ],
      rows,
    )
    expect(stocks).toEqual([
      { qtyPerKit: 2, lotTracked: true, byLot: { 1: 5, 2: 3 }, total: 8 },
      { qtyPerKit: 1, lotTracked: false, byLot: {}, total: 10 },
    ])
    expect(canAssembleInOne(stocks)).toBe(2)
  })

  it('piezas: sin kits como pieza, sin serie, inactivas, mismo dueño, máximo 50 (las básicas del editor primero)', () => {
    const infos = new Map<string, PieceInfo>([
      ['A', { sku: 'A', trackingTypeCode: 'LOT', isActive: true, ownerClientPublicId: null }],
      ['K', { sku: 'KIT-A', isKit: true, isActive: true }],
      ['AK', { sku: 'KIT-B', isAssembledKit: true, isActive: true }],
      ['S', { sku: 'S', trackingTypeCode: 'SERIAL', isActive: true }],
      ['I', { sku: 'I', isActive: false }],
      ['O', { sku: 'O', isActive: true, ownerClientPublicId: 'cli-1' }],
    ])
    expect(assembledPiecesProblem([piece('1', null)], infos, null)).toEqual({ key: 'noPieces' })
    expect(assembledPiecesProblem([piece('1', 'A', '0')], infos, null)).toEqual({ key: 'pieceQuantity' })
    expect(assembledPiecesProblem([piece('1', 'A'), piece('2', 'A')], infos, null)).toEqual({ key: 'pieceRepeated' })
    expect(assembledPiecesProblem([piece('1', 'A'), piece('2', 'K')], infos, null)).toEqual({ key: 'pieceIsKit', sku: 'KIT-A' })
    expect(assembledPiecesProblem([piece('1', 'AK')], infos, null)).toEqual({ key: 'pieceIsKit', sku: 'KIT-B' })
    expect(assembledPiecesProblem([piece('1', 'S')], infos, null)).toEqual({ key: 'pieceIsSerial', sku: 'S' })
    expect(assembledPiecesProblem([piece('1', 'I')], infos, null)).toEqual({ key: 'pieceInactive', sku: 'I' })
    expect(assembledPiecesProblem([piece('1', 'O')], infos, null)).toEqual({ key: 'pieceOtherOwner', sku: 'O' })
    expect(assembledPiecesProblem([piece('1', 'O')], infos, 'cli-1')).toBeNull()
    expect(assembledPiecesProblem([piece('1', 'A', '2')], infos, null)).toBeNull()
    // una pieza cuya ficha aún no llegó no se revisa aquí (la revisa el servidor)
    expect(assembledPiecesProblem([piece('1', 'X')], infos, null)).toBeNull()
    const many = Array.from({ length: 51 }, (_, i) => piece(`k${i}`, `P${i}`))
    expect(assembledPiecesProblem(many, infos, null)).toEqual({ key: 'tooMany' })
  })

  it('alta: lo que agrega el kit armado al ProductCreateRequest (rastreo y costo null; armar solo con cantidad)', () => {
    const pieces = [piece('1', 'A', '2'), piece('2', 'B', '1,5'), piece('3', null)]
    expect(assembledKitCreateFields({ pieces, assembleQuantity: null, assembleWarehousePublicId: 'W', assembleToBinId: '10' })).toEqual({
      isAssembledKit: true,
      kitPieces: [
        { productPublicId: 'A', quantity: 2 },
        { productPublicId: 'B', quantity: 1.5 },
      ],
      trackingType: null,
      purchaseCost: null,
      assembleQuantity: null,
      assembleWarehousePublicId: null,
      assembleToBinId: null,
    })
    expect(assembledKitCreateFields({ pieces, assembleQuantity: 3, assembleWarehousePublicId: 'W', assembleToBinId: '10' })).toMatchObject({
      assembleQuantity: 3,
      assembleWarehousePublicId: 'W',
      assembleToBinId: 10,
    })
  })

  it('composición: firma para saber si cambió y piezas del servidor en su orden', () => {
    const fromServer = piecesFromAssembled(
      [
        { productPublicId: 'B', sku: 'B', name: 'Bee', quantity: 1, sortOrder: 2 },
        { productPublicId: 'A', sku: 'A', name: 'Ay', quantity: 2, sortOrder: 1 },
      ],
      (i) => `s${i}`,
    )
    expect(fromServer.map((p) => [p.productPublicId, p.quantity])).toEqual([
      ['A', '2'],
      ['B', '1'],
    ])
    expect(piecesSignature(fromServer)).toBe('A×2|B×1')
    expect(piecesSignature([...fromServer, piece('n', null)])).toBe('A×2|B×1')
    expect(piecesSignature([{ ...fromServer[0], quantity: '3' }, fromServer[1]])).not.toBe(piecesSignature(fromServer))
  })

  it('armar: solo las piezas con salida escogida viajan; resumen con la sugerida (rank 1) o la escogida', () => {
    expect(parseChoiceKey('')).toBeNull()
    expect(parseChoiceKey(choiceKey(5, null))).toEqual({ binId: 5, lotId: null })
    expect(parseChoiceKey(choiceKey(5, 9))).toEqual({ binId: 5, lotId: 9 })
    expect(assembleRequest({ quantity: 3, toBinId: '20', warehousePublicId: 'W', notes: '  ', choices: { A: '', B: '' } })).toEqual({
      quantity: 3,
      toBinId: 20,
      warehousePublicId: 'W',
      notes: null,
    })
    expect(assembleRequest({ quantity: 3, toBinId: '20', warehousePublicId: 'W', notes: 'x', choices: { A: choiceKey(2, 7), B: '' } })).toEqual({
      quantity: 3,
      toBinId: 20,
      warehousePublicId: 'W',
      notes: 'x',
      pieces: [{ productPublicId: 'A', lotId: 7, binId: 2 }],
    })
    const rows = [
      { productPublicId: 'A', binId: 2, binCode: 'A-02', lotId: 7, lotNumber: 'L7', available: 9, rank: 2 },
      { productPublicId: 'A', binId: 1, binCode: 'A-01', lotId: 6, lotNumber: 'L6', available: 4, rank: 1 },
      { productPublicId: 'B', binId: 3, binCode: 'B-01', available: 10, rank: 1 },
    ]
    const pieces = [
      { productPublicId: 'A', sku: 'A', name: 'Ay', quantity: 2 },
      { productPublicId: 'B', sku: 'B', name: 'Bee', quantity: 1 },
    ]
    expect(assembleSummary(pieces, 3, rows, {})).toEqual([
      { productPublicId: 'A', sku: 'A', name: 'Ay', qtyPerKit: 2, total: 6, binCode: 'A-01', lotNumber: 'L6', suggested: true },
      { productPublicId: 'B', sku: 'B', name: 'Bee', qtyPerKit: 1, total: 3, binCode: 'B-01', lotNumber: null, suggested: true },
    ])
    expect(assembleSummary(pieces, 3, rows, { A: choiceKey(2, 7) })[0]).toMatchObject({ binCode: 'A-02', lotNumber: 'L7', suggested: false })
  })

  it('mensajes del servidor tal cual: título y los de cada pieza, sin repetir', () => {
    expect(
      problemMessages({
        title: 'El lote A2 de A solo alcanza para 2 kit(s): hay 4 disponible y cada kit lleva 2. Arme 2 con ese lote y el resto en otro armado con otro lote.',
        errors: { 'pieces[0]': ['El lote A2 de A solo alcanza para 2 kit(s): hay 4 disponible y cada kit lleva 2. Arme 2 con ese lote y el resto en otro armado con otro lote.'], 'pieces[1]': ['No hay suficiente B disponible en W1 para 3 kit(s): hay 10 y se necesitan 12; alcanza para 2.'] },
      }),
    ).toHaveLength(2)
    expect(problemMessages(null)).toEqual([])
  })

  it('desarmar: cada pieza vuelve a su lote repartida entre las líneas; a la posición de donde salió o a la indicada', () => {
    const assembly = {
      lines: [
        { productPublicId: 'A', sku: 'A', name: 'Ay', lotNumber: 'L1', fromBinCode: 'A-01', qtyPerKit: 2, quantity: 4, returnedQty: 2 },
        { productPublicId: 'A', sku: 'A', name: 'Ay', lotNumber: 'L1', fromBinCode: 'A-02', qtyPerKit: 2, quantity: 2, returnedQty: 0 },
        { productPublicId: 'B', sku: 'B', name: 'Bee', lotNumber: null, fromBinCode: 'B-01', qtyPerKit: 1, quantity: 3, returnedQty: 1 },
      ],
    }
    expect(disassembleReturns(assembly, 2)).toEqual([
      { productPublicId: 'A', sku: 'A', name: 'Ay', lotNumber: 'L1', binCode: 'A-01', quantity: 2 },
      { productPublicId: 'A', sku: 'A', name: 'Ay', lotNumber: 'L1', binCode: 'A-02', quantity: 2 },
      { productPublicId: 'B', sku: 'B', name: 'Bee', lotNumber: null, binCode: 'B-01', quantity: 2 },
    ])
    expect(disassembleReturns(assembly, 1, 'R-09').map((r) => r.binCode)).toEqual(['R-09', 'R-09'])
    expect(disassembleRequest({ quantity: 2, warehousePublicId: 'W1', assemblyWarehousePublicId: 'W1', toBinId: '', notes: '' })).toEqual({
      quantity: 2,
      warehousePublicId: null,
      toBinId: null,
      notes: null,
    })
    expect(disassembleRequest({ quantity: 1, warehousePublicId: 'W2', assemblyWarehousePublicId: 'W1', toBinId: '30', notes: 'parcial' })).toEqual({
      quantity: 1,
      warehousePublicId: 'W2',
      toBinId: 30,
      notes: 'parcial',
    })
  })
})

describe('pedido guiado: el kit armado se vende como un producto normal', () => {
  it('sin piezas en la línea (no es isKit): no viaja composición ni hay «Cambiar»', () => {
    // el catálogo del pedido marca isKit solo para el kit de oferta (tipo KIT); el kit armado llega como producto normal
    const item: CatalogItem = { productPublicId: 'KA', sku: 'KIT-ARM', name: 'Kit armado', price: 50, priceSource: 'LIST', isKit: false }
    const cart = setCartQuantity([], item, '2')
    expect(cart[0]).toMatchObject({ productPublicId: 'KA', isKit: false, components: null })
    expect(cartToLines(cart)).toEqual([{ productPublicId: 'KA', quantity: 2, components: undefined }])
  })
})
