// Lote A8 — lista de lo que hay en una posición (Consultar): SKU grande, nombre, lotes agregados por producto, cantidades
// solo con permiso, vacío y buscador con más de 6.
import { fireEvent, render, screen } from '@testing-library/react-native'
import { StyleSheet } from 'react-native'

import { BinContentsList } from './BinContentsList'
import { aggregateBinContents, type BalanceRow, type BinContentItem } from './lookupLogic'

function item(sku: string, lots: string[] = [], qtyOnHand = 0, qtyAvailable = qtyOnHand): BinContentItem {
  return { productPublicId: `p-${sku}`, sku, productName: `Nombre ${sku}`, lots, qtyOnHand, qtyAvailable }
}

describe('BinContentsList', () => {
  it('con permiso: SKU, nombre, lotes y las cantidades con los separadores de la compañía', async () => {
    const rows: BalanceRow[] = [
      { id: 1, binCode: 'A-01', productPublicId: 'p1', sku: 'TOR-1', productName: 'Tornillo', lotNumber: 'A', qtyOnHand: 1000, qtyAvailable: 900 },
      { id: 2, binCode: 'A-01', productPublicId: 'p1', sku: 'TOR-1', productName: 'Tornillo', lotNumber: 'B', qtyOnHand: 250, qtyAvailable: 250 },
    ]
    await render(<BinContentsList items={aggregateBinContents(rows)} showQty />)
    expect(screen.getByText('TOR-1')).toBeTruthy()
    expect(screen.getByText('Tornillo')).toBeTruthy()
    expect(screen.getByText('Lote A, B')).toBeTruthy()
    expect(screen.getByText('En mano: 1,250 · Disponible: 1,150')).toBeTruthy()
    expect(screen.getByText('1 producto en esta posición')).toBeTruthy()
  })

  it('sin permiso: la misma lista sin ninguna cantidad', async () => {
    await render(<BinContentsList items={[item('TOR-1', ['A'], 10, 8), item('TUE-2', [], 3)]} showQty={false} />)
    expect(screen.getByText('TOR-1')).toBeTruthy()
    expect(screen.getByText('TUE-2')).toBeTruthy()
    expect(screen.getByText('2 productos en esta posición')).toBeTruthy()
    expect(screen.queryByText(/En mano/)).toBeNull()
    expect(screen.queryByText(/Disponible/)).toBeNull()
    expect(screen.queryByText(/10/)).toBeNull()
  })

  it('muchos lotes: los primeros tres y cuántos más', async () => {
    await render(<BinContentsList items={[item('X', ['L1', 'L2', 'L3', 'L4', 'L5'])]} showQty={false} />)
    expect(screen.getByText('Lote L1, L2, L3 y 2 más')).toBeTruthy()
  })

  it('producto sin lote: no dice nada de lotes', async () => {
    await render(<BinContentsList items={[item('X')]} showQty={false} />)
    expect(screen.queryByText(/Lote/)).toBeNull()
  })

  it('posición vacía: "No hay productos en esta posición."', async () => {
    await render(<BinContentsList items={[]} showQty />)
    expect(screen.getByText('No hay productos en esta posición.')).toBeTruthy()
  })

  it('sin buscador con 6 productos; con 7 aparece y filtra por SKU, nombre o lote', async () => {
    const six = ['A1', 'A2', 'A3', 'A4', 'A5', 'A6'].map((s) => item(s))
    const first = await render(<BinContentsList items={six} showQty={false} />)
    expect(screen.queryByLabelText('Buscar producto o lote')).toBeNull()
    await first.unmount()

    await render(<BinContentsList items={[...six, item('ZZ-7', ['LOTE-Q'])]} showQty={false} />)
    const search = screen.getByLabelText('Buscar producto o lote')
    await fireEvent.changeText(search, 'lote-q')
    expect(screen.getByText('ZZ-7')).toBeTruthy()
    expect(screen.queryByText('A1')).toBeNull()
    // el conteo de arriba sigue diciendo todos los productos de la posición
    expect(screen.getByText('7 productos en esta posición')).toBeTruthy()
    await fireEvent.changeText(search, 'nada')
    expect(screen.getByText('Ningún producto coincide con «nada».')).toBeTruthy()
  })

  it('letras grandes: SKU de 20 y ningún texto por debajo de 16', async () => {
    await render(<BinContentsList items={[item('TOR-1', ['A'], 5)]} showQty />)
    expect(StyleSheet.flatten(screen.getByText('TOR-1').props.style).fontSize).toBe(20)
    for (const text of ['Nombre TOR-1', 'Lote A', 'En mano: 5 · Disponible: 5', '1 producto en esta posición']) {
      expect(StyleSheet.flatten(screen.getByText(text).props.style).fontSize).toBeGreaterThanOrEqual(16)
    }
  })
})
