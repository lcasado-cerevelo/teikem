// Teléfono con la máscara de la compañía (Región y formatos). Las funciones puras se prueban a fondo en
// `kernel/format/phone.test.ts`; aquí, la reexportación del kit y el control dentro de un formulario.
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useForm } from 'react-hook-form'
import { afterEach, describe, expect, it } from 'vitest'
import { PR_FORMAT, resetFormatSettings, setFormatSettings } from '../format'
import { Field, Form } from './Form'
import { CallingCodeInput, PhoneRow } from './CallingCodeInput'
import { PhoneInput } from './PhoneInput'
import { formatPhone, formatPhoneInput, isValidPhone, normalizePhone, normalizeStoredPhone } from './phone'

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

afterEach(() => resetFormatSettings())

describe('teléfono', () => {
  it('formatPhoneInput aplica la máscara de Puerto Rico mientras se escribe y limita a 10 dígitos', () => {
    expect(formatPhoneInput('')).toBe('')
    expect(formatPhoneInput('78')).toBe('(78')
    expect(formatPhoneInput('787555')).toBe('(787) 555')
    expect(formatPhoneInput('7875551234')).toBe('(787) 555-1234')
    expect(formatPhoneInput('787555123499')).toBe('(787) 555-1234')
  })
  it('formatPhone (normalizeStoredPhone) muestra con máscara solo con los dígitos exactos; lo demás tal cual', () => {
    expect(formatPhone('787-555-1234')).toBe('(787) 555-1234')
    expect(normalizeStoredPhone('(787)555-1234')).toBe('(787) 555-1234')
    expect(formatPhone('555-1234')).toBe('555-1234')
    expect(formatPhone(null)).toBe('')
  })
  it('isValidPhone: vacío o 10 dígitos; normalizePhone guarda solo dígitos', () => {
    expect(isValidPhone('')).toBe(true)
    expect(isValidPhone('(787) 555-1234')).toBe(true)
    expect(isValidPhone('(787)555')).toBe(false)
    expect(normalizePhone('(787) 555-1234')).toBe('7875551234')
  })
  it('PhoneInput da formato mientras se escribe y sigue la máscara de la compañía', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const input = screen.getByLabelText('Teléfono')
    expect(input).toHaveAttribute('placeholder', '(000) 000-0000')
    await user.type(input, '7875551234')
    expect(input).toHaveValue('(787) 555-1234')
    act(() => setFormatSettings({ ...PR_FORMAT, phoneMask: '###.###.####' }))
    expect(input).toHaveAttribute('placeholder', '000.000.0000')
    await user.clear(input)
    await user.type(input, '7875551234')
    expect(input).toHaveValue('787.555.1234')
  })
})

function HostWithPrefix({ prefix = '+1' }: { prefix?: string }) {
  const form = useForm({ values: { countryCallingCode: prefix, phone: '' } })
  return (
    <Form form={form} onSubmit={async () => {}}>
      <PhoneRow>
        <Field name="countryCallingCode" label="Prefijo">
          <CallingCodeInput />
        </Field>
        <Field name="phone" label="Teléfono">
          <PhoneInput />
        </Field>
      </PhoneRow>
    </Form>
  )
}

describe('teléfono con prefijo', () => {
  it('solo entran números: letras, signos y espacios se descartan', async () => {
    const user = userEvent.setup()
    render(<HostWithPrefix />)
    const input = screen.getByLabelText('Teléfono')
    await user.type(input, 'a7-8.7x 5#5@5!1,2;3/4')
    expect(input).toHaveValue('(787) 555-1234')
  })

  it('con el prefijo de la compañía se pinta con la máscara; con otro prefijo quedan los dígitos y se repinta al cambiar el prefijo', async () => {
    const user = userEvent.setup()
    render(<HostWithPrefix />)
    const cc = screen.getByLabelText('Prefijo')
    const input = screen.getByLabelText('Teléfono')
    await user.type(input, '7875551234')
    expect(input).toHaveValue('(787) 555-1234')

    await user.clear(cc)
    await user.type(cc, '593')
    expect(input).toHaveValue('7875551234')
    expect(input).toHaveAttribute('placeholder', '')

    await user.clear(input)
    await user.type(input, '991234567899999')
    expect(input).toHaveValue('991234567899')   // 15 dígitos como máximo contando el prefijo (+593 deja 12)

    await user.clear(cc)
    await user.type(cc, '1')
    expect(input).toHaveValue('(991) 234-5678')   // con el prefijo +1 vuelve la máscara y se corta a 10 dígitos
  })

  it('el prefijo solo deja números (hasta 4) y pone el «+»', async () => {
    const user = userEvent.setup()
    render(<HostWithPrefix prefix="" />)
    const cc = screen.getByLabelText('Prefijo')
    await user.type(cc, 'ab5-9 3x1')
    expect(cc).toHaveValue('+5931')
  })
})
