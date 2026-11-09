// Lote 230 — HelpTip: el «?» abre y cierra la ayuda; Escape la cierra sin cerrar el modal que la contiene; tocar fuera la cierra;
// en un <Field tip> el botón queda junto a la etiqueta sin cambiar el nombre accesible del control.
import { zodResolver } from '@hookform/resolvers/zod'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { setLang } from '../i18n/i18n'
import { Field, Form, TextInput } from './Form'
import { HelpTip } from './HelpTip'
import { Modal } from './Modal'

beforeAll(() => setLang('es'))

function TipForm() {
  const form = useForm({ resolver: zodResolver(z.object({ rotation: z.string() })), defaultValues: { rotation: '' } })
  return (
    <Form form={form} onSubmit={() => undefined}>
      <Field name="rotation" label="Rotación" tip="De qué saldo sale primero la mercancía.">
        <TextInput />
      </Field>
    </Form>
  )
}

describe('HelpTip', () => {
  it('abre y cierra con el botón; el botón dice de qué es la ayuda', async () => {
    const user = userEvent.setup()
    render(<HelpTip label="Ayuda: Empaque">Caja de 12 o barril de 50.</HelpTip>)
    const btn = screen.getByRole('button', { name: 'Ayuda: Empaque' })
    expect(btn).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('note')).toBeNull()
    await user.click(btn)
    expect(btn).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('note')).toHaveTextContent('Caja de 12 o barril de 50.')
    expect(btn).toHaveAttribute('aria-controls', screen.getByRole('note').id)
    await user.click(btn)
    expect(screen.queryByRole('note')).toBeNull()
  })

  it('Escape cierra la ayuda y no el modal; tocar fuera también la cierra', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(
      <Modal open title="Producto" onClose={onClose}>
        <p>Fuera</p>
        <HelpTip label="Ayuda: Rotación">Texto largo</HelpTip>
      </Modal>,
    )
    await user.click(screen.getByRole('button', { name: 'Ayuda: Rotación' }))
    expect(screen.getByRole('note')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('note')).toBeNull()
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Ayuda: Rotación' })).toHaveFocus()

    await user.click(screen.getByRole('button', { name: 'Ayuda: Rotación' }))
    await user.click(screen.getByText('Fuera'))
    expect(screen.queryByRole('note')).toBeNull()
  })

  it('Field con tip: el control conserva su etiqueta y el «?» lleva el nombre del campo', async () => {
    const user = userEvent.setup()
    render(<TipForm />)
    expect(screen.getByLabelText('Rotación')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Ayuda: Rotación' }))
    expect(screen.getByRole('note')).toHaveTextContent('De qué saldo sale primero la mercancía.')
  })
})
