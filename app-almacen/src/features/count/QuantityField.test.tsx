// Calculadora de cantidad (pedido del dueño 2026-10-05): el botón cambia el campo por «filas × columnas + sueltas» en el mismo lugar, el total va
// llenando la cantidad, y al volver a «Cantidad directa» queda ese número. Con varios bloques para estibas de varias capas.
import { fireEvent, render } from '@testing-library/react-native'
import { useState } from 'react'

import { QuantityField } from './QuantityField'

function Host({ initial = '' }: { initial?: string }) {
  const [value, setValue] = useState(initial)
  return <QuantityField value={value} onChangeText={setValue} accessibilityLabel="Cantidad encontrada" />
}

describe('QuantityField', () => {
  it('sin calculadora es el campo de siempre, con su botón de calculadora al lado', async () => {
    const { getByLabelText, queryByTestId } = await render(<Host />)
    await fireEvent.changeText(getByLabelText('Cantidad encontrada'), '42')
    expect(getByLabelText('Cantidad encontrada').props.value).toBe('42')
    expect(getByLabelText('Calculadora')).toBeTruthy()
    expect(queryByTestId('quantity-calculator')).toBeNull()
  })

  it('el ejemplo del dueño: 5 × 3 + 10 sueltas = 25; el total llena la cantidad y se ve la cuenta', async () => {
    const { getByLabelText, getByText, getByTestId, queryByLabelText } = await render(<Host />)
    await fireEvent.press(getByLabelText('Calculadora'))
    // el campo único se cambió por la calculadora (mismo lugar)
    expect(queryByLabelText('Cantidad encontrada')).toBeNull()
    await fireEvent.changeText(getByLabelText('Filas del bloque 1'), '5')
    // incompleto: sin total y con el aviso
    expect(getByText('Cada bloque necesita filas y columnas.')).toBeTruthy()
    await fireEvent.changeText(getByLabelText('Columnas del bloque 1'), '3')
    await fireEvent.changeText(getByLabelText('Sueltas'), '10')
    expect(getByText('(5 × 3) + 10')).toBeTruthy()
    expect(getByTestId('quantity-calculator-total').props.children).toBe('Total: 25')

    // volver a la cantidad directa: queda el total
    await fireEvent.press(getByLabelText('Cantidad directa'))
    expect(getByLabelText('Cantidad encontrada').props.value).toBe('25')
  })

  it('varios bloques (estiba de varias capas), quitar uno y el total se recalcula', async () => {
    const { getByLabelText, getByTestId, queryByLabelText } = await render(<Host />)
    await fireEvent.press(getByLabelText('Calculadora'))
    await fireEvent.changeText(getByLabelText('Filas del bloque 1'), '5')
    await fireEvent.changeText(getByLabelText('Columnas del bloque 1'), '3')
    await fireEvent.press(getByLabelText('+ Otro bloque'))
    await fireEvent.changeText(getByLabelText('Filas del bloque 2'), '4')
    await fireEvent.changeText(getByLabelText('Columnas del bloque 2'), '3')
    expect(getByTestId('quantity-calculator-total').props.children).toBe('Total: 27')
    await fireEvent.press(getByLabelText('Quitar el bloque 1'))
    expect(queryByLabelText('Filas del bloque 2')).toBeNull()
    expect(getByTestId('quantity-calculator-total').props.children).toBe('Total: 12')
  })

  it('lo que ya estaba escrito pasa a «sueltas» al abrir la calculadora, y un cálculo incompleto deja la cantidad vacía', async () => {
    const { getByLabelText } = await render(<Host initial="7" />)
    await fireEvent.press(getByLabelText('Calculadora'))
    expect(getByLabelText('Sueltas').props.value).toBe('7')
    await fireEvent.changeText(getByLabelText('Filas del bloque 1'), '2')
    await fireEvent.press(getByLabelText('Cantidad directa'))
    // 2 filas sin columnas: no hay total y la cantidad queda en blanco (no deja guardar un número a medias)
    expect(getByLabelText('Cantidad encontrada').props.value).toBe('')
  })

  it('texto inválido en filas avisa y no inventa un total', async () => {
    const { getByLabelText, getByText, queryByTestId } = await render(<Host />)
    await fireEvent.press(getByLabelText('Calculadora'))
    await fireEvent.changeText(getByLabelText('Filas del bloque 1'), '5,5')
    await fireEvent.changeText(getByLabelText('Columnas del bloque 1'), '3')
    expect(getByText('Filas y columnas son números enteros; las sueltas, un número.')).toBeTruthy()
    expect(queryByTestId('quantity-calculator-total')).toBeNull()
  })
})
