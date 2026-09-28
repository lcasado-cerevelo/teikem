import { fireEvent, render } from '@testing-library/react-native'

import { ScanField } from './ScanField'

describe('ScanField', () => {
  it('al escribir y dar Enter, manda el código recortado y limpia el campo', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText } = await render(<ScanField label="Escanea el producto" onSubmit={onSubmit} />)
    const input = getByLabelText('Escanea el producto')
    await fireEvent.changeText(input, '  ABC123  ')
    await fireEvent(input, 'submitEditing', { nativeEvent: { text: '  ABC123  ' } })
    expect(onSubmit).toHaveBeenCalledWith('ABC123')
  })

  it('un código vacío no manda nada', async () => {
    const onSubmit = jest.fn()
    const { getByLabelText } = await render(<ScanField label="Escanea el producto" onSubmit={onSubmit} />)
    const input = getByLabelText('Escanea el producto')
    await fireEvent(input, 'submitEditing', { nativeEvent: { text: '   ' } })
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('muestra el error en vez de la ayuda cuando hay uno', async () => {
    const { getByText, queryByText } = await render(
      <ScanField label="Escanea el producto" help="Código de barras o SKU." error="No hay un producto con ese código." onSubmit={jest.fn()} />,
    )
    expect(getByText('No hay un producto con ese código.')).toBeTruthy()
    expect(queryByText('Código de barras o SKU.')).toBeNull()
  })
})
