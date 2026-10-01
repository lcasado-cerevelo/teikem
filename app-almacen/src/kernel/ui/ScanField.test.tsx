import { fireEvent, render } from '@testing-library/react-native'

import { ScanField } from './ScanField'

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
