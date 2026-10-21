// Lote 156: los formularios de creación del portal mandan lo escrito (número con coma incluido) y muestran el error del servidor tal cual.
jest.mock('expo-crypto', () => ({ getRandomBytes: (n: number) => new Uint8Array(n).fill(1) }))
jest.mock('../portalApi', () => ({
  createProduct: jest.fn(),
  updateProduct: jest.fn(),
  createArrival: jest.fn(),
  createExit: jest.fn(),
  listWarehouseOptions: jest.fn(),
  listWarehouseProducts: jest.fn(),
}))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { createProduct, listWarehouseOptions, listWarehouseProducts, updateProduct } from '../portalApi'
import { EditProductForm, NewExitForm, NewProductForm, parseNumber } from './CreateForms'

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  ;(listWarehouseOptions as jest.Mock).mockResolvedValue([{ publicId: 'w1', code: 'ALM-01', name: 'Principal' }])
  ;(listWarehouseProducts as jest.Mock).mockResolvedValue({ items: [], total: 0 })
})

describe('parseNumber', () => {
  it('acepta coma decimal y vacío es null', () => {
    expect(parseNumber('4,5')).toBe(4.5)
    expect(parseNumber(' 12 ')).toBe(12)
    expect(parseNumber('')).toBeNull()
    expect(parseNumber('abc')).toBeNull()
  })
})

describe('NewProductForm', () => {
  it('manda lo escrito y avisa al guardar', async () => {
    ;(createProduct as jest.Mock).mockResolvedValue({})
    const onDone = jest.fn()
    await render(<NewProductForm onDone={onDone} onCancel={jest.fn()} />)
    await fireEvent.changeText(screen.getByTestId('create-sku'), 'NUEVO-1')
    await fireEvent.changeText(screen.getByTestId('create-name'), 'Producto nuevo')
    await fireEvent.press(screen.getByTestId('create-save'))
    await waitFor(() => expect(onDone).toHaveBeenCalled())
    expect(createProduct).toHaveBeenCalledWith(expect.objectContaining({ sku: 'NUEVO-1', name: 'Producto nuevo', trackingType: 'NONE', purchaseCost: null }))
  })

  it('muestra el error del servidor y no cierra', async () => {
    ;(createProduct as jest.Mock).mockRejectedValue(new Error('boom'))
    const onDone = jest.fn()
    await render(<NewProductForm onDone={onDone} onCancel={jest.fn()} />)
    await fireEvent.press(screen.getByTestId('create-save'))
    await waitFor(() => expect(createProduct).toHaveBeenCalled())
    expect(onDone).not.toHaveBeenCalled()
  })
})

describe('EditProductForm', () => {
  const product = { publicId: 'p1', sku: 'MED', name: 'Medicina', barcode: '123', brand: 'Marca', category: null, trackingType: 'NONE', isActive: true, onHand: 0, available: 0 }

  it('parte de lo que tiene, manda lo editado y el costo vacío no se manda', async () => {
    ;(updateProduct as jest.Mock).mockResolvedValue({})
    const onDone = jest.fn()
    await render(<EditProductForm product={product} onDone={onDone} onCancel={jest.fn()} />)
    await fireEvent.changeText(screen.getByTestId('edit-name'), 'Medicina nueva')
    await fireEvent.press(screen.getByTestId('create-save'))
    await waitFor(() => expect(onDone).toHaveBeenCalled())
    expect(updateProduct).toHaveBeenCalledWith('p1', { name: 'Medicina nueva', barcode: '123', brand: 'Marca', purchaseCost: null })
  })

  it('muestra el error del servidor y no cierra', async () => {
    ;(updateProduct as jest.Mock).mockRejectedValue(new Error('boom'))
    const onDone = jest.fn()
    await render(<EditProductForm product={product} onDone={onDone} onCancel={jest.fn()} />)
    await fireEvent.press(screen.getByTestId('create-save'))
    await waitFor(() => expect(updateProduct).toHaveBeenCalled())
    expect(onDone).not.toHaveBeenCalled()
  })
})

describe('NewExitForm', () => {
  it('con un solo almacén lo elige solo y muestra el aviso de reserva', async () => {
    await render(<NewExitForm onDone={jest.fn()} onCancel={jest.fn()} />)
    expect(await screen.findByText(/ALM-01/)).toBeTruthy()
    expect(screen.getByText('Se reserva en el acto lo que haya; lo que falte queda pendiente.')).toBeTruthy()
  })
})
