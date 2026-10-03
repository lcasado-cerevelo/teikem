import { act, fireEvent, render } from '@testing-library/react-native'
import { Platform } from 'react-native'

import { ScanField } from './ScanField'

// Lector del Zebra simulado: DataWedge "instalado" y un emisor del evento onScan (el módulo nativo no existe en Jest).
type ScanListener = (e: { data: string; symbology: string }) => void
const mockScanListeners = new Set<ScanListener>()
jest.mock('../../../modules/datawedge', () => ({
  __esModule: true,
  default: {
    isAvailable: () => true,
    createProfile: jest.fn(),
    getProfileStatus: () => ({ state: 'unconfirmed', detail: '' }),
    refreshProfileStatus: jest.fn(),
    addListener: (event: string, listener: ScanListener) => {
      if (event === 'onScan') mockScanListeners.add(listener)
      return { remove: () => mockScanListeners.delete(listener) }
    },
  },
}))

async function scan(data: string) {
  await act(async () => {
    mockScanListeners.forEach((l) => l({ data, symbology: 'CODE128' }))
  })
}

const originalOS = Platform.OS
beforeEach(() => {
  mockScanListeners.clear()
  Platform.OS = 'android'
})
afterAll(() => {
  Platform.OS = originalOS
})

// En un teléfono sin lector no hay un Enter evidente: "Aceptar" y "Usar {sugerida}" confirman sin teclado físico.
describe('ScanField', () => {
  it('"Aceptar" toma lo escrito (recortado) como si se hubiera escaneado y limpia el campo', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText } = await render(<ScanField label="Escanea la posición destino" onSubmit={onSubmit} />)
    const input = getByLabelText('Escanea la posición destino')
    await fireEvent.changeText(input, '  R-01 ')
    await fireEvent.press(getByLabelText('Aceptar'))
    expect(onSubmit).toHaveBeenCalledWith('R-01')
    expect(input.props.value).toBe('')
  })

  it('"Aceptar" no hace nada con el campo vacío', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText } = await render(<ScanField label="Producto" onSubmit={onSubmit} />)
    await fireEvent.press(getByLabelText('Aceptar'))
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('Enter (teclado físico del Zebra o en pantalla) también acepta', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText } = await render(<ScanField label="Producto" onSubmit={onSubmit} />)
    await fireEvent(getByLabelText('Producto'), 'submitEditing', { nativeEvent: { text: 'SKU-9' } })
    expect(onSubmit).toHaveBeenCalledWith('SKU-9')
  })

  it('con valor sugerido muestra "Usar {valor}" y lo envía sin escribirlo', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText, queryByLabelText, rerender } = await render(
      <ScanField label="Escanea la posición destino" onSubmit={onSubmit} suggestedValue="GENERAL" />,
    )
    await fireEvent.press(getByLabelText('Usar GENERAL'))
    expect(onSubmit).toHaveBeenCalledWith('GENERAL')
    await rerender(<ScanField label="Escanea la posición destino" onSubmit={onSubmit} />)
    expect(queryByLabelText(/^Usar /)).toBeNull()
  })
})

describe('ScanField — lector sin teclado (docs/mobile/mejoras-ux-zebra.md §2)', () => {
  it('escanear equivale a escribir el código y tocar Aceptar (sin ningún toque más)', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText } = await render(<ScanField label="Escanea el producto" onSubmit={onSubmit} />)
    await fireEvent.changeText(getByLabelText('Escanea el producto'), 'a medio escribir')
    await scan('7501234567890')
    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit).toHaveBeenCalledWith('7501234567890')
    expect(getByLabelText('Escanea el producto').props.value).toBe('')
  })

  it('el teclado en pantalla NO aparece al enfocar; el botón ⌨ lo muestra y lo vuelve a esconder', async () => {
    const { getByLabelText } = await render(<ScanField label="Producto" onSubmit={jest.fn()} />)
    expect(getByLabelText('Producto').props.showSoftInputOnFocus).toBe(false)
    await fireEvent.press(getByLabelText('Mostrar teclado'))
    expect(getByLabelText('Producto').props.showSoftInputOnFocus).toBe(true)
    await fireEvent.press(getByLabelText('Esconder teclado'))
    expect(getByLabelText('Producto').props.showSoftInputOnFocus).toBe(false)
  })

  it('la misma lectura que llega dos veces seguidas (teclas + intent) se toma una sola vez', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText } = await render(<ScanField label="Producto" onSubmit={onSubmit} />)
    await fireEvent(getByLabelText('Producto'), 'submitEditing', { nativeEvent: { text: 'SKU-1' } })
    await scan('SKU-1')
    expect(onSubmit).toHaveBeenCalledTimes(1)
    await scan('SKU-2')
    expect(onSubmit).toHaveBeenCalledTimes(2)
  })

  it('el error de la lectura sale en un bloque grande (ScanMessage) y el aviso de éxito en verde', async () => {
    const { getByTestId, getByText, rerender, queryByTestId } = await render(<ScanField label="Producto" onSubmit={jest.fn()} error="No hay un producto con ese código." />)
    expect(getByTestId('scan-message-error')).toBeTruthy()
    expect(getByText('No hay un producto con ese código.').props.style).toMatchObject({ fontSize: 20 })
    await rerender(<ScanField label="Producto" onSubmit={jest.fn()} notice="Agregado: 1 SKU-1 desde A-01" />)
    expect(queryByTestId('scan-message-error')).toBeNull()
    expect(getByTestId('scan-message-ok')).toBeTruthy()
  })
})

describe('ScanField — valor desde afuera (prefill, docs/mobile/mejoras-ux-zebra.md §3)', () => {
  it('llena el campo sin enviarlo; se confirma con Aceptar; el mismo valor otra vez con otro contador vuelve a llenarlo', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText, rerender } = await render(<ScanField label="Producto" onSubmit={onSubmit} prefill={{ value: 'SKU-7', seq: 1 }} />)
    const input = () => getByLabelText('Producto')
    expect(input().props.value).toBe('SKU-7')
    expect(onSubmit).not.toHaveBeenCalled()
    await fireEvent.press(getByLabelText('Aceptar'))
    expect(onSubmit).toHaveBeenCalledWith('SKU-7')
    expect(input().props.value).toBe('')

    await rerender(<ScanField label="Producto" onSubmit={onSubmit} prefill={{ value: 'SKU-7', seq: 2 }} />)
    expect(input().props.value).toBe('SKU-7')
  })
})
