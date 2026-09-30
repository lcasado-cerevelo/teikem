import { zodResolver } from '@hookform/resolvers/zod'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { ApiError } from '../api/problem'
import { setLang } from '../i18n/i18n'
import { Field, Form, NumberInput, Select, TextInput, Toggle } from './Form'

const schema = z.object({
  code: z.string(),
  name: z.string().min(1, 'Escriba el nombre.'),
  creditLimit: z.number().nullable(),
  currency: z.string(),
  active: z.boolean(),
})

function ClientForm({ onSubmit }: { onSubmit: (v: z.infer<typeof schema>) => Promise<unknown> }) {
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { code: '', name: '', creditLimit: null, currency: '', active: true },
  })
  return (
    <Form form={form} onSubmit={onSubmit}>
      <Field name="code" label="Código">
        <TextInput />
      </Field>
      <Field name="name" label="Nombre" required help="Como aparece en facturas.">
        <TextInput />
      </Field>
      <Field name="creditLimit" label="Límite">
        <NumberInput />
      </Field>
      <Field name="currency" label="Moneda">
        <Select placeholder="Seleccione…" options={[{ value: 'USD', label: 'Dólar' }]} />
      </Field>
      <Field name="active" label="Activo">
        <Toggle />
      </Field>
      <button type="submit">Guardar</button>
    </Form>
  )
}

beforeAll(() => setLang('es'))

describe('Form + Field', () => {
  it('muestra bajo el campo el error de validación del esquema', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn(async () => undefined)
    render(<ClientForm onSubmit={onSubmit} />)
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Escriba el nombre.')).toBeInTheDocument()
    expect(screen.getByLabelText(/Nombre/)).toHaveAttribute('aria-invalid', 'true')
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('pone el error del servidor bajo su campo (applyProblemDetails) y los errores sin campo arriba', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn(async () => {
      throw new ApiError(400, {
        title: 'Hay errores de validación.',
        code: 'validation',
        errors: { '$.Code': ['El código ya existe.'], 'contract.title': ['El título no puede exceder 200 caracteres.'] },
      })
    })
    render(<ClientForm onSubmit={onSubmit} />)
    await user.type(screen.getByLabelText('Código'), 'ACME')
    await user.type(screen.getByLabelText(/Nombre/), 'Acme Corp')
    await user.type(screen.getByLabelText('Límite'), '1500.5')
    await user.selectOptions(screen.getByLabelText('Moneda'), 'USD')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))

    expect(onSubmit).toHaveBeenCalledWith(
{ code: 'ACME', name: 'Acme Corp', creditLimit: 1500.5, currency: 'USD', active: true })
    const err = await screen.findByText('El código ya existe.')
    expect(err).toHaveClass('ferr')
    const code = screen.getByLabelText('Código')
    expect(code).toHaveAttribute('aria-invalid', 'true')
    expect(code.getAttribute('aria-describedby')).toBe(err.id)
    // el error de un campo que el formulario no pinta va al aviso superior, con el título
    const alert = screen.getByText('Hay errores de validación.')
    expect(alert).toHaveTextContent('El título no puede exceder 200 caracteres.')
  })

  it('error sin campos (409, 422): solo el título arriba', async () => {
    const user = userEvent.setup()
    render(
      <ClientForm
        onSubmit={async () => {
          throw new ApiError(409, { title: 'Otro usuario modificó el registro.', code: 'conflict' })
        }}
      />,
    )
    await user.type(screen.getByLabelText(/Nombre/), 'X')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Otro usuario modificó el registro.')
  })

  it('NumberInput vacío envía null', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn(async () => undefined)
    render(<ClientForm onSubmit={onSubmit} />)
    await user.type(screen.getByLabelText(/Nombre/), 'X')
    await user.click(screen.getByRole('switch'))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ creditLimit: null, active: false }))
  })

  it('hideLabel: la etiqueta sigue nombrando el control (.sr-only) y el error se ve bajo el campo', async () => {
    const qtySchema = z.object({ qty: z.number({ error: 'Indique la cantidad.' }) })
    function GridCell() {
      const form = useForm({ resolver: zodResolver(qtySchema), defaultValues: { qty: null as unknown as number } })
      return (
        <Form form={form} onSubmit={() => undefined}>
          <Field name="qty" label="Cantidad de la línea 1" required hideLabel>
            <NumberInput />
          </Field>
          <button type="submit">Guardar</button>
        </Form>
      )
    }
    const user = userEvent.setup()
    render(<GridCell />)
    const input = screen.getByRole('spinbutton', { name: /Cantidad de la línea 1/ })
    const label = document.querySelector(`label[for="${input.id}"]`)
    expect(label).toHaveClass('sr-only')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Indique la cantidad.')).toBeInTheDocument()
    expect(input).toHaveAttribute('aria-invalid', 'true')
  })

  it('sin hideLabel la etiqueta se ve (sin .sr-only)', () => {
    render(<ClientForm onSubmit={async () => undefined} />)
    const input = screen.getByLabelText('Código')
    expect(document.querySelector(`label[for="${input.id}"]`)).not.toHaveClass('sr-only')
  })
})
