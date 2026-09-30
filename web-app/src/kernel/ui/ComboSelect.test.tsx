import { zodResolver } from '@hookform/resolvers/zod'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { setLang } from '../i18n/i18n'
import { ComboSelect, ComboSelectInput } from './ComboSelect'
import { exactComboMatch } from './comboMatch'
import { Field, Form } from './Form'

const OPTIONS = [
  { value: 'STORAGE', label: 'Almacenaje', hint: 'Racks' },
  { value: 'STAGING', label: 'Preparación' },
  { value: 'CROSSDOCK', label: 'Cruce de muelle' },
]

beforeAll(() => setLang('es'))

describe('ComboSelect', () => {
  it('filtra en el cliente sin acentos (etiqueta, valor o hint) y devuelve el valor elegido', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ComboSelect options={OPTIONS} value="" onChange={onChange} aria-label="Tipo" />)
    const input = screen.getByRole('combobox', { name: 'Tipo' })
    await user.click(input)
    expect(screen.getAllByRole('option')).toHaveLength(3)
    await user.type(input, 'preparacion')
    expect(screen.getAllByRole('option')).toHaveLength(1)
    await user.click(screen.getByRole('option', { name: 'Preparación' }))
    expect(onChange).toHaveBeenCalledWith('STAGING', OPTIONS[1])
    await user.clear(input)
    await user.type(input, 'racks')
    expect(screen.getByRole('option', { name: /Almacenaje/ })).toBeInTheDocument()
  })

  it('teclado: ↓ recorre, Enter elige; Enter con el código exacto gana a la resaltada; Escape cierra', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ComboSelect options={OPTIONS} value="" onChange={onChange} aria-label="Tipo" />)
    const input = screen.getByRole('combobox', { name: 'Tipo' })
    await user.click(input)
    await user.keyboard('{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenLastCalledWith('STAGING', OPTIONS[1])
    await user.click(input)
    await user.type(input, 'crossdock{Enter}')
    expect(onChange).toHaveBeenLastCalledWith('CROSSDOCK', OPTIONS[2])
    await user.click(input)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('muestra la etiqueta del valor y ✕ lo quita con ""', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<ComboSelect options={OPTIONS} value="STORAGE" onChange={onChange} aria-label="Tipo" />)
    expect(screen.getByRole('combobox', { name: 'Tipo' })).toHaveValue('Almacenaje')
    await user.click(screen.getByRole('button', { name: 'Quitar' }))
    expect(onChange).toHaveBeenCalledWith('', null)
  })

  it('sin coincidencias lo dice', async () => {
    const user = userEvent.setup()
    render(<ComboSelect options={OPTIONS} value="" onChange={vi.fn()} aria-label="Tipo" />)
    await user.type(screen.getByRole('combobox', { name: 'Tipo' }), 'zzz')
    expect(screen.getByText('Sin coincidencias')).toBeInTheDocument()
  })

  it('exactComboMatch compara valor y etiqueta sin mayúsculas ni acentos', () => {
    expect(exactComboMatch(OPTIONS, ' staging ')?.value).toBe('STAGING')
    expect(exactComboMatch(OPTIONS, 'preparacion')?.value).toBe('STAGING')
    expect(exactComboMatch(OPTIONS, 'prep')).toBeUndefined()
  })
})

describe('ComboSelectInput', () => {
  function Harness({ onSubmit }: { onSubmit: (v: { zoneType: string }) => void }) {
    const form = useForm({ resolver: zodResolver(z.object({ zoneType: z.string().min(1, 'Elija el tipo.') })), defaultValues: { zoneType: '' } })
    return (
      <Form form={form} onSubmit={onSubmit}>
        <Field name="zoneType" label="Tipo" required>
          <ComboSelectInput options={OPTIONS} />
        </Field>
        <button type="submit">Guardar</button>
      </Form>
    )
  }

  it('se registra en el formulario con la etiqueta del Field y muestra el error bajo el campo', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<Harness onSubmit={onSubmit} />)
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Elija el tipo.')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: /Tipo/ })).toHaveAttribute('aria-invalid', 'true')
    await user.click(screen.getByRole('combobox', { name: /Tipo/ }))
    await user.click(screen.getByRole('option', { name: 'Cruce de muelle' }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(onSubmit).toHaveBeenCalledWith({ zoneType: 'CROSSDOCK' })
  })
})
