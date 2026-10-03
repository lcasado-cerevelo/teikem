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

  it('layout "tile" (Inicio): 88 dp de alto, icono arriba y texto debajo, etiqueta de 20', async () => {
    const { getByRole, getByText } = await render(<BigButton layout="tile" icon="📥" label="Recibir" onPress={jest.fn()} />)
    const style = Object.assign({}, ...[getByRole('button').props.style].flat(3).filter(Boolean))
    expect(style.minHeight).toBe(88)
    expect(getByText('Recibir').props.style).toEqual(expect.arrayContaining([expect.objectContaining({ fontSize: 20 })]))
    // icono grande del layout "tile" (arriba del texto, en columna)
    expect(getByText('📥').props.style).toMatchObject({ fontSize: 30 })
  })
})
