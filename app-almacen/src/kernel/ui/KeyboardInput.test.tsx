// Pedido del dueño 2026-10-05: donde haya un campo de texto, la opción del teclado en pantalla (el botón «⌨»). Por defecto el teclado NO aparece al
// enfocar (el Zebra tiene teclado físico y lector); el botón lo muestra y lo esconde. Con un solo botón compartido para una lista de campos.
import { fireEvent, render } from '@testing-library/react-native'
import { createRef } from 'react'
import { TextInput, View } from 'react-native'

import { KeyboardInput, KeyboardToggleButton } from './KeyboardInput'
import { useSoftKeyboard } from './useSoftKeyboard'

describe('KeyboardInput', () => {
  it('con autoFocus pide el foco y selecciona todo el valor que trae (el 1 de la cantidad)', async () => {
    jest.useFakeTimers()
    const ref = createRef<TextInput>()
    await render(<KeyboardInput ref={ref} autoFocus accessibilityLabel="Cantidad" keyboardType="decimal-pad" value="125" />)
    const input = ref.current as unknown as { focus: jest.Mock; setSelection: jest.Mock }
    input.focus = jest.fn()
    input.setSelection = jest.fn()
    jest.advanceTimersByTime(400)
    expect(input.focus).toHaveBeenCalled()
    expect(input.setSelection).toHaveBeenCalledWith(0, 3)
    jest.useRealTimers()
  })

  it('en los campos numéricos (cantidades) el valor sale seleccionado al enfocar; en los de texto, no', async () => {
    const { getByLabelText } = await render(
      <View>
        <KeyboardInput accessibilityLabel="Cantidad" keyboardType="decimal-pad" defaultValue="1" />
        <KeyboardInput accessibilityLabel="Piezas" keyboardType="number-pad" defaultValue="1" />
        <KeyboardInput accessibilityLabel="Nota" defaultValue="x" />
      </View>,
    )
    expect(getByLabelText('Cantidad').props.selectTextOnFocus).toBe(true)
    expect(getByLabelText('Piezas').props.selectTextOnFocus).toBe(true)
    expect(getByLabelText('Nota').props.selectTextOnFocus).toBe(false)
  })

  it('trae su botón «⌨»: el teclado en pantalla arranca escondido y el botón lo muestra y lo vuelve a esconder', async () => {
    const { getByLabelText } = await render(<KeyboardInput accessibilityLabel="Cantidad" keyboardType="decimal-pad" />)
    expect(getByLabelText('Cantidad').props.showSoftInputOnFocus).toBe(false)
    await fireEvent.press(getByLabelText('Mostrar teclado'))
    expect(getByLabelText('Cantidad').props.showSoftInputOnFocus).toBe(true)
    await fireEvent.press(getByLabelText('Esconder teclado'))
    expect(getByLabelText('Cantidad').props.showSoftInputOnFocus).toBe(false)
  })

  it('sin botón propio (toggle=false) lo gobierna quien lo usa: un solo botón para varios campos', async () => {
    function List() {
      const kb = useSoftKeyboard()
      return (
        <View>
          <KeyboardToggleButton on={kb.show} onPress={kb.toggle} />
          <KeyboardInput toggle={false} softKeyboard={kb.show} accessibilityLabel="Cantidad en A-01" />
          <KeyboardInput toggle={false} softKeyboard={kb.show} accessibilityLabel="Cantidad en B-02" />
        </View>
      )
    }
    const { getByLabelText, getAllByLabelText } = await render(<List />)
    expect(getAllByLabelText('Mostrar teclado')).toHaveLength(1)
    expect(getByLabelText('Cantidad en A-01').props.showSoftInputOnFocus).toBe(false)
    await fireEvent.press(getByLabelText('Mostrar teclado'))
    expect(getByLabelText('Cantidad en A-01').props.showSoftInputOnFocus).toBe(true)
    expect(getByLabelText('Cantidad en B-02').props.showSoftInputOnFocus).toBe(true)
  })

  it('pasa el resto de las propiedades al campo (valor y cambios)', async () => {
    const onChange = jest.fn()
    const { getByLabelText } = await render(<KeyboardInput accessibilityLabel="Lote" value="L-1" onChangeText={onChange} />)
    expect(getByLabelText('Lote').props.value).toBe('L-1')
    await fireEvent.changeText(getByLabelText('Lote'), 'L-2')
    expect(onChange).toHaveBeenCalledWith('L-2')
  })
})
