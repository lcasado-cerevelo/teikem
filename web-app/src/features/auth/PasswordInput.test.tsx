// El ojito del campo de contraseña (primer ingreso y restablecer): muestra lo escrito para revisar que no hay un error, y lo vuelve a ocultar.
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { PasswordInput } from './PasswordInput'

function Host() {
  const [v, setV] = useState('')
  return (
    <>
      <label htmlFor="p1">Nueva</label>
      <PasswordInput id="p1" value={v} onChange={setV} />
      <label htmlFor="p2">Confirmar</label>
      <PasswordInput id="p2" value={v} onChange={setV} />
    </>
  )
}

describe('PasswordInput', () => {
  it('empieza oculto, el ojo lo muestra y otro clic lo oculta (cada campo con su propio ojo)', async () => {
    await setLang('es')
    const user = userEvent.setup()
    render(<Host />)
    const nueva = screen.getByLabelText('Nueva')
    const confirmar = screen.getByLabelText('Confirmar')
    expect(nueva).toHaveAttribute('type', 'password')
    await user.type(nueva, 'Secreta123')

    const ojos = screen.getAllByRole('button', { name: 'Mostrar contraseña' })
    expect(ojos).toHaveLength(2)
    await user.click(ojos[0])
    expect(nueva).toHaveAttribute('type', 'text')
    expect(nueva).toHaveValue('Secreta123')
    expect(confirmar).toHaveAttribute('type', 'password') // el otro campo sigue oculto
    await user.click(screen.getByRole('button', { name: 'Ocultar contraseña' }))
    expect(nueva).toHaveAttribute('type', 'password')
  })
})
