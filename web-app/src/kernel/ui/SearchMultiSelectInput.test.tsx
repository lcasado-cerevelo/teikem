import { zodResolver } from '@hookform/resolvers/zod'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { setLang } from '../i18n/i18n'
import { Field, Form } from './Form'
import { SearchMultiSelectInput } from './SearchMultiSelectInput'

beforeAll(() => setLang('es'))

const ZONES = [
  { value: '1', label: 'R-01 · Norte' },
  { value: '2', label: 'R-02 · Sur' },
]
const schema = z.object({ zoneIds: z.array(z.string()).min(1, 'Indique al menos una zona.') })

function Demo({ onSubmit }: { onSubmit: (v: { zoneIds: string[] }) => void }) {
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { zoneIds: [] as string[] } })
  return (
    <Form form={form} onSubmit={onSubmit}>
      <Field name="zoneIds" label="Zonas" required>
        <SearchMultiSelectInput options={ZONES} placeholder="Todas" />
      </Field>
      <button type="submit">Enviar</button>
    </Form>
  )
}

describe('SearchMultiSelectInput', () => {
  it('guarda el arreglo elegido en el formulario y muestra el error del esquema bajo el campo', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(<Demo onSubmit={onSubmit} />)
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    expect(await screen.findByText('Indique al menos una zona.')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
    await user.click(screen.getByLabelText(/Zonas/))
    await user.click(screen.getByLabelText(/R-02/))
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    await vi.waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1))
    expect(onSubmit.mock.calls[0][0]).toEqual({ zoneIds: ['2'] })
  })
})
