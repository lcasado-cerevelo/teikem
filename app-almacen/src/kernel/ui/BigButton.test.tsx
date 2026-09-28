import { fireEvent, render } from '@testing-library/react-native'

import { BigButton } from './BigButton'

// @testing-library/react-native v14: render() y fireEvent() son async (React 19).
describe('BigButton', () => {
  it('llama a onPress al tocarlo', async () => {
    const onPress = jest.fn()
    const { getByRole } = await render(<BigButton label="Confirmar" onPress={onPress} />)
    await fireEvent.press(getByRole('button'))
    expect(onPress).toHaveBeenCalledTimes(1)
  })

  it('deshabilitado no llama a onPress', async () => {
    const onPress = jest.fn()
    const { getByRole } = await render(<BigButton label="Confirmar" onPress={onPress} disabled />)
    await fireEvent.press(getByRole('button'))
    expect(onPress).not.toHaveBeenCalled()
  })

  it('con loading no muestra la etiqueta', async () => {
    const { queryByText } = await render(<BigButton label="Confirmar" onPress={jest.fn()} loading />)
    expect(queryByText('Confirmar')).toBeNull()
  })
})
