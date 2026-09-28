import { fireEvent, render } from '@testing-library/react-native'
import { useState } from 'react'

import { NumericKeypad, PinDots } from './NumericKeypad'

function Wrapper() {
  const [value, setValue] = useState('')
  return <NumericKeypad value={value} onChange={setValue} maxLength={4} />
}

describe('NumericKeypad', () => {
  it('agrega dígitos hasta el máximo y borra con ⌫', async () => {
    const onChange = jest.fn()
    const { getByText, rerender } = await render(<NumericKeypad value="12" onChange={onChange} maxLength={4} />)
    await fireEvent.press(getByText('3'))
    expect(onChange).toHaveBeenCalledWith('123')

    await rerender(<NumericKeypad value="1234" onChange={onChange} maxLength={4} />)
    onChange.mockClear()
    await fireEvent.press(getByText('5'))
    expect(onChange).not.toHaveBeenCalled()

    await fireEvent.press(getByText('⌫'))
    expect(onChange).toHaveBeenCalledWith('123')
  })

  it('funciona como componente controlado (Wrapper de prueba)', async () => {
    const { getByText } = await render(<Wrapper />)
    await fireEvent.press(getByText('4'))
    await fireEvent.press(getByText('8'))
    expect(getByText('4')).toBeTruthy()
  })
})

describe('PinDots', () => {
  it('describe cuántos dígitos van llenos', async () => {
    const { getByLabelText } = await render(<PinDots length={4} filled={2} />)
    expect(getByLabelText('2 de 4 dígitos')).toBeTruthy()
  })
})
