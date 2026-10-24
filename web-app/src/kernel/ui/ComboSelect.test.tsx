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
  { value: 'CROSSDOCK', label: 'Cruce de plataforma' },
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

describe('ComboSelect — opción de crear (Lote 213)', () => {
  const label = (text: string) => `+ Nuevo «${text}»`

  it('está apagada por omisión: sin onCreate no hay opción de crear', async () => {
    const user = userEvent.setup()
    render(<ComboSelect options={OPTIONS} value="" onChange={vi.fn()} aria-label="Tipo" />)
    await user.type(screen.getByRole('combobox', { name: 'Tipo' }), 'zzz')
    expect(screen.queryByRole('option')).toBeNull()
    expect(screen.getByText('Sin coincidencias')).toBeInTheDocument()
  })

  it('aparece solo con texto escrito, al final de la lista', async () => {
    const user = userEvent.setup()
    render(<ComboSelect options={OPTIONS} value="" onChange={vi.fn()} aria-label="Tipo" createLabel={label} onCreate={vi.fn()} />)
    const input = screen.getByRole('combobox', { name: 'Tipo' })
    await user.click(input)
    expect(screen.getAllByRole('option')).toHaveLength(3)
    await user.type(input, 'a')
    const opts = screen.getAllByRole('option')
    expect(opts[opts.length - 1]).toHaveTextContent('+ Nuevo «a»')
  })

  it('con cero coincidencias queda resaltada y Enter la elige con el texto escrito', async () => {
    const user = userEvent.setup()
    const onCreate = vi.fn()
    const onChange = vi.fn()
    render(<ComboSelect options={OPTIONS} value="" onChange={onChange} aria-label="Tipo" createLabel={label} onCreate={onCreate} />)
    const input = screen.getByRole('combobox', { name: 'Tipo' })
    await user.type(input, '  Zeta nueva ')
    const create = screen.getByRole('option', { name: /Nuevo/ })
    expect(create).toHaveClass('on')
    expect(screen.queryByText('Sin coincidencias')).toBeNull()
    await user.keyboard('{Enter}')
    expect(onCreate).toHaveBeenCalledWith('Zeta nueva')
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('con coincidencias, ↓ llega a la opción de crear y Enter la elige; sin bajar, Enter elige la coincidencia', async () => {
    const user = userEvent.setup()
    const onCreate = vi.fn()
    const onChange = vi.fn()
    render(<ComboSelect options={OPTIONS} value="" onChange={onChange} aria-label="Tipo" createLabel={label} onCreate={onCreate} />)
    const input = screen.getByRole('combobox', { name: 'Tipo' })
    await user.type(input, 'almac')
    await user.keyboard('{Enter}')
    expect(onChange).toHaveBeenLastCalledWith('STORAGE', OPTIONS[0])
    expect(onCreate).not.toHaveBeenCalled()
    await user.click(input)
    await user.type(input, 'almac')
    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(screen.getByRole('option', { name: /Nuevo/ })).toHaveClass('on')
    await user.keyboard('{Enter}')
    expect(onCreate).toHaveBeenCalledWith('almac')
  })

  it('el clic en la opción de crear también la elige', async () => {
    const user = userEvent.setup()
    const onCreate = vi.fn()
    render(<ComboSelect options={OPTIONS} value="" onChange={vi.fn()} aria-label="Tipo" createLabel={label} onCreate={onCreate} />)
    await user.type(screen.getByRole('combobox', { name: 'Tipo' }), 'qq')
    await user.click(screen.getByRole('option', { name: /Nuevo/ }))
    expect(onCreate).toHaveBeenCalledWith('qq')
  })

  it('Enter con el código exacto de una opción gana a la de crear; seedText reabre con el texto', async () => {
    const user = userEvent.setup()
    const onCreate = vi.fn()
    const onChange = vi.fn()
    const { rerender } = render(<ComboSelect options={OPTIONS} value="" onChange={onChange} aria-label="Tipo" createLabel={label} onCreate={onCreate} />)
    await user.type(screen.getByRole('combobox', { name: 'Tipo' }), 'crossdock{Enter}')
    expect(onChange).toHaveBeenLastCalledWith('CROSSDOCK', OPTIONS[2])
    expect(onCreate).not.toHaveBeenCalled()
    rerender(<ComboSelect options={OPTIONS} value="" onChange={onChange} aria-label="Tipo" createLabel={label} onCreate={onCreate} seedText="Zeta" />)
    await user.tab()
    await user.tab({ shift: true })
    expect(screen.getByRole('combobox', { name: 'Tipo' })).toHaveValue('Zeta')
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
    await user.click(screen.getByRole('option', { name: 'Cruce de plataforma' }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(onSubmit).toHaveBeenCalledWith({ zoneType: 'CROSSDOCK' })
  })
})
