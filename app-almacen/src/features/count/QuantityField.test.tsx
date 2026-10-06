// Calculadora de cantidad (pedido del dueño 2026-10-05): el botón cambia el campo por «filas × columnas + sueltas» en el mismo lugar, el total va
// llenando la cantidad, y al volver a «Cantidad directa» queda ese número. Con varios bloques para estibas de varias capas.
import { fireEvent, render, within } from '@testing-library/react-native'
import { useState } from 'react'
import { StyleSheet } from 'react-native'

import { colors } from '../../kernel/ui/theme'
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
    const { getByLabelText, getByRole, getByText, getByTestId, queryByLabelText } = await render(<Host />)
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
    await fireEvent.press(getByRole('button', { name: 'Cantidad directa' }))
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
    const { getByLabelText, getByRole } = await render(<Host initial="7" />)
    await fireEvent.press(getByLabelText('Calculadora'))
    expect(getByLabelText('Sueltas').props.value).toBe('7')
    await fireEvent.changeText(getByLabelText('Filas del bloque 1'), '2')
    await fireEvent.press(getByRole('button', { name: 'Cantidad directa' }))
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

  // Lote A9 (pruebas en el Zebra): el icono de la calculadora no se veía (🧮 oscuro sobre fondo oscuro), «Calculadora» se partía en dos
  // líneas y «Cantidad directa» era un enlace de texto.
  it('el botón de la calculadora: icono dibujado en blanco sobre azul oscuro (sin emoji) y de al menos 48 × 48 dp', async () => {
    const { getByRole, queryByText } = await render(<Host />)
    const btn = getByRole('button', { name: 'Calculadora' })
    const style = StyleSheet.flatten(btn.props.style)
    expect(style.backgroundColor).toBe(colors.brandDark)
    expect(style.minHeight).toBeGreaterThanOrEqual(48)
    expect(style.width).toBeGreaterThanOrEqual(48)
    const icon = within(btn).getByTestId('calculator-icon', { includeHiddenElements: true })
    expect(StyleSheet.flatten(icon.props.style).borderColor).toBe(colors.onStrong)
    expect(queryByText('🧮')).toBeNull()
  })

  it('la cabecera: «Calculadora» en UNA línea (se achica, no se parte) y volver es un botón con icono llamado «Cantidad directa»', async () => {
    const { getByLabelText, getByRole, getByTestId, queryByText } = await render(<Host initial="3" />)
    await fireEvent.press(getByLabelText('Calculadora'))
    const title = getByTestId('quantity-calculator-title')
    expect(title.props.children).toBe('Calculadora')
    expect(title.props.numberOfLines).toBe(1)
    expect(title.props.adjustsFontSizeToFit).toBe(true)
    // el contenedor del título cede espacio (no empuja los botones fuera de la fila)
    const box = StyleSheet.flatten(title.parent?.props.style)
    expect(box).toMatchObject({ flex: 1, flexShrink: 1, minWidth: 0 })

    // ya no hay un enlace con el texto «Cantidad directa»: es un botón con icono y ese nombre accesible
    expect(queryByText('Cantidad directa')).toBeNull()
    const back = getByRole('button', { name: 'Cantidad directa' })
    const backStyle = StyleSheet.flatten(back.props.style)
    expect(backStyle.minHeight).toBeGreaterThanOrEqual(48)
    expect(backStyle.width).toBeGreaterThanOrEqual(48)
    expect(backStyle.backgroundColor).toBe(colors.brandDark)
    expect(StyleSheet.flatten(within(back).getByText('←').props.style).color).toBe(colors.onStrong)

    await fireEvent.press(back)
    expect(getByLabelText('Cantidad encontrada').props.value).toBe('3')
  })
})
