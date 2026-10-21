// Lista de teléfonos (teléfonos extra de aviso): prefijo + número, solo dígitos, máscara de la compañía, se guarda en E.164.
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import { resetFormatSettings } from '../format'
import { PhoneListInput, parsePhoneList } from './PhoneListInput'

function Host({ initial = '' }: { initial?: string }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <PhoneListInput id="p" value={value} onChange={setValue} />
      <output data-testid="saved">{value}</output>
    </>
  )
}

afterEach(() => resetFormatSettings())

describe('PhoneListInput', () => {
  it('parsePhoneList separa por coma o punto y coma y quita vacíos y repetidos', () => {
    expect(parsePhoneList('+17875551234, +17875551234; +593991234567,, ')).toEqual(['+17875551234', '+593991234567'])
    expect(parsePhoneList(null)).toEqual([])
  })

  it('agrega un número con la máscara de la compañía y lo guarda como +prefijo y dígitos', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const number = screen.getByLabelText('Phone number')
    expect(screen.getByLabelText('Country code')).toHaveValue('+1')
    await user.type(number, '78a75-55 1234')
    expect(number).toHaveValue('(787) 555-1234')
    await user.click(screen.getByRole('button', { name: 'Add phone' }))
    expect(screen.getByTestId('saved')).toHaveTextContent('+17875551234')
    expect(number).toHaveValue('')
    expect(screen.getByText('(787) 555-1234')).toBeInTheDocument()
  })

  it('con otro prefijo guarda los dígitos sin máscara; Enter agrega; el botón queda apagado si el número no está completo', async () => {
    const user = userEvent.setup()
    render(<Host initial="+17875551234" />)
    const add = screen.getByRole('button', { name: 'Add phone' })
    const cc = screen.getByLabelText('Country code')
    const number = screen.getByLabelText('Phone number')
    await user.clear(cc)
    await user.type(cc, '593')
    await user.type(number, '99123')
    expect(add).toBeDisabled()
    await user.type(number, '4567{Enter}')
    expect(screen.getByTestId('saved')).toHaveTextContent('+17875551234, +593991234567')
    expect(screen.getByText('+593991234567')).toBeInTheDocument()
  })

  it('quitar un teléfono lo saca de la lista', async () => {
    const user = userEvent.setup()
    render(<Host initial="+17875551234, +593991234567" />)
    await user.click(screen.getByRole('button', { name: 'Remove +593991234567' }))
    expect(screen.getByTestId('saved').textContent).toBe('+17875551234')
  })
})
