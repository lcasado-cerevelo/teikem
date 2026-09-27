import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { Filters } from './Filters'
import { SearchMultiSelect, SearchSelect } from './SearchSelect'

const OPTIONS = [
  { value: 'SJU', label: 'San Juan' },
  { value: 'PON', label: 'Ponce' },
  { value: 'MAY', label: 'Mayagüez' },
]

beforeAll(() => setLang('es'))

function Host({ onChange }: { onChange: (v: string[]) => void }) {
  const [value, setValue] = useState<string[]>([])
  return (
    <>
      <label id="lbl" htmlFor="ctl">
        Bodegas
      </label>
      <SearchMultiSelect
        id="ctl"
        labelledBy="lbl"
        options={OPTIONS}
        value={value}
        placeholder="Seleccione…"
        onChange={(v) => {
          setValue(v)
          onChange(v)
        }}
      />
    </>
  )
}

describe('SearchMultiSelect / SearchSelect', () => {
  it('control sin etiqueta propia: se asocia al <label> externo, busca sin acentos y elige varias', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Host onChange={onChange} />)
    const button = screen.getByLabelText('Bodegas')
    expect(button).toHaveTextContent('Seleccione…')
    await user.click(button)
    await user.type(screen.getByRole('searchbox'), 'mayaguez')
    expect(screen.queryByLabelText('San Juan')).toBeNull()
    await user.click(screen.getByLabelText('Mayagüez'))
    expect(onChange).toHaveBeenLastCalledWith(['MAY'])
    await user.clear(screen.getByRole('searchbox'))
    await user.click(screen.getByLabelText('Ponce'))
    expect(onChange).toHaveBeenLastCalledWith(['MAY', 'PON'])
    await user.keyboard('{Escape}')
    expect(button).toHaveTextContent('2 elegidos')
  })

  it('SearchSelect conserva su etiqueta y "Todos" sin selección', () => {
    render(<SearchSelect label="Estatus" options={OPTIONS} value={[]} onChange={() => {}} />)
    expect(screen.getByLabelText('Estatus')).toHaveTextContent('Todos')
  })

  it('Filters: "Limpiar" lleva la clase que lo deja al ancho de su contenido', () => {
    render(
      <Filters onClear={() => {}}>
        <SearchSelect label="Estatus" options={OPTIONS} value={[]} onChange={() => {}} />
      </Filters>,
    )
    expect(screen.getByRole('button', { name: 'Limpiar' })).toHaveClass('btn', 'sm', 'filters-clear')
  })
})
