import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { describe, expect, it } from 'vitest'
import { Field, Form } from './Form'
import { PhoneInput } from './PhoneInput'
import { formatPhone, isValidPhone, normalizeStoredPhone } from './phone'

function Host({ initial = '' }: { initial?: string }) {
  const form = useForm({ values: { phone: initial } })
  return (
    <Form form={form} onSubmit={async () => {}}>
      <Field name="phone" label="Teléfono">
        <PhoneInput />
      </Field>
    </Form>
  )
}

describe('teléfono', () => {
  it('formatPhone aplica la máscara y limita a 10 dígitos', () => {
    expect(formatPhone('')).toBe('')
    expect(formatPhone('78')).toBe('(78')
    expect(formatPhone('787555')).toBe('(787)555')
    expect(formatPhone('7875551234')).toBe('(787)555-1234')
    expect(formatPhone('787555123499')).toBe('(787)555-1234')
  })
  it('normalizeStoredPhone normaliza 10 dígitos y respeta lo demás', () => {
    expect(normalizeStoredPhone('787-555-1234')).toBe('(787)555-1234')
    expect(normalizeStoredPhone('555-1234')).toBe('555-1234')
    expect(normalizeStoredPhone(null)).toBe('')
  })
  it('isValidPhone: vacío o 10 dígitos', () => {
    expect(isValidPhone('')).toBe(true)
    expect(isValidPhone('(787)555-1234')).toBe(true)
    expect(isValidPhone('(787)555')).toBe(false)
  })
  it('PhoneInput da formato mientras se escribe', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const input = screen.getByLabelText('Teléfono')
    await user.type(input, '7875551234')
    expect(input).toHaveValue('(787)555-1234')
  })
})
