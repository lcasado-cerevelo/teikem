// El campo de contraseña de toda la web (Lote 219): ojito para ver lo escrito y aviso de mayúsculas.
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { PasswordBox } from './PasswordBox'
import { Field, Form, TextInput } from './Form'

beforeAll(() => setLang('es'))

describe('PasswordBox', () => {
  it('oculta lo escrito y el ojito lo muestra y lo vuelve a ocultar', async () => {
    const user = userEvent.setup()
    render(<PasswordBox id="p" defaultValue="Secreta-1" />)
    const input = document.getElementById('p') as HTMLInputElement
    expect(input.type).toBe('password')
    await user.click(screen.getByRole('button', { name: 'Mostrar contraseña' }))
    expect(input.type).toBe('text')
    expect(input.value).toBe('Secreta-1')
    await user.click(screen.getByRole('button', { name: 'Ocultar contraseña' }))
    expect(input.type).toBe('password')
  })

  it('avisa cuando las mayúsculas están activadas y lo quita al apagarlas o al salir del campo', () => {
    render(<PasswordBox id="p" />)
    const input = document.getElementById('p') as HTMLInputElement
    expect(screen.queryByTestId('caps-warning')).not.toBeInTheDocument()
    fireEvent.keyDown(input, { key: 'a', modifierCapsLock: true })
    expect(screen.getByTestId('caps-warning')).toHaveTextContent('Las mayúsculas están activadas.')
    fireEvent.keyUp(input, { key: 'a', modifierCapsLock: false })
    expect(screen.queryByTestId('caps-warning')).not.toBeInTheDocument()
    fireEvent.keyDown(input, { key: 'a', modifierCapsLock: true })
    fireEvent.blur(input)
    expect(screen.queryByTestId('caps-warning')).not.toBeInTheDocument()
  })

  it('un PIN (solo números) no avisa de mayúsculas pero sí tiene ojito', () => {
    render(<PasswordBox id="p" pin />)
    fireEvent.keyDown(document.getElementById('p')!, { key: '1', modifierCapsLock: true })
    expect(screen.queryByTestId('caps-warning')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mostrar contraseña' })).toBeInTheDocument()
  })

  it('conserva los manejadores del llamador', () => {
    let downs = 0
    render(<PasswordBox id="p" onKeyDown={() => downs++} />)
    fireEvent.keyDown(document.getElementById('p')!, { key: 'a' })
    expect(downs).toBe(1)
  })
})

function Wrapper({ children }: { children: React.ReactNode }) {
  const form = useForm({ defaultValues: { pwd: 'abc' } })
  return <Form form={form} onSubmit={() => undefined}>{children}</Form>
}

describe('TextInput type="password" del kit', () => {
  it('lleva el ojito sin que cada pantalla lo pida', () => {
    render(
      <Wrapper>
        <Field name="pwd" label="Clave">
          <TextInput type="password" />
        </Field>
      </Wrapper>,
    )
    expect(screen.getByRole('button', { name: 'Mostrar contraseña' })).toBeInTheDocument()
  })
})
