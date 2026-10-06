import { fireEvent, render, screen } from '@testing-library/react-native'
import { Text } from 'react-native'

import { KeyboardScreen } from './KeyboardScreen'
import { StickyAlert } from './StickyAlert'

describe('StickyAlert', () => {
  it('se dibuja fuera del área desplazable (no se va al desplazarse) y la ✕ llama a cerrar', async () => {
    const onClose = jest.fn()
    await render(
      <KeyboardScreen banner={<StickyAlert message="A-03 recibe solo 5" onClose={onClose} />}>
        <Text>contenido largo</Text>
      </KeyboardScreen>,
    )
    const alert = screen.getByTestId('sticky-alert')
    // el aviso es hermano del área desplazable, no su hijo: sube por los padres y no se topa con el ScrollView
    let node = alert.parent
    let insideScroll = false
    while (node) {
      if (String(node.type).includes('ScrollView')) insideScroll = true
      node = node.parent
    }
    expect(insideScroll).toBe(false)
    expect(screen.getByText('contenido largo')).toBeTruthy()
    await fireEvent.press(screen.getByLabelText('Cerrar aviso'))
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
