import { fireEvent, render } from '@testing-library/react-native'

import { LineList } from './LineList'

describe('LineList', () => {
  it('muestra el mensaje vacío sin líneas', async () => {
    const { getByText } = await render(<LineList items={[]} removeLabel="Quitar" emptyLabel="Sin líneas" />)
    expect(getByText('Sin líneas')).toBeTruthy()
  })

  it('lista las líneas y llama a onRemove con el id correcto', async () => {
    const onRemove = jest.fn()
    const { getByLabelText, getByText } = await render(
      <LineList
        items={[
          { id: 1, title: 'SKU-1 · 3', subtitle: 'Lote L1' },
          { id: 2, title: 'SKU-2 · 1' },
        ]}
        onRemove={onRemove}
        removeLabel="Quitar"
      />,
    )
    expect(getByText('SKU-1 · 3')).toBeTruthy()
    expect(getByText('Lote L1')).toBeTruthy()
    await fireEvent.press(getByLabelText('Quitar SKU-2 · 1'))
    expect(onRemove).toHaveBeenCalledWith(2)
  })

  it('con Editar y Quitar pinta íconos ✎ y ✕ y llama a cada acción con el id', async () => {
    const onRemove = jest.fn()
    const onEdit = jest.fn()
    const { getByLabelText, getByText, queryByText } = await render(
      <LineList items={[{ id: 7, title: 'Algodón · 3' }]} onRemove={onRemove} removeLabel="Quitar" onEdit={onEdit} editLabel="Editar" />,
    )
    expect(getByText('✎')).toBeTruthy()
    expect(getByText('✕')).toBeTruthy()
    expect(queryByText('Quitar')).toBeNull()
    await fireEvent.press(getByLabelText('Editar Algodón · 3'))
    expect(onEdit).toHaveBeenCalledWith(7)
    await fireEvent.press(getByLabelText('Quitar Algodón · 3'))
    expect(onRemove).toHaveBeenCalledWith(7)
  })
})
