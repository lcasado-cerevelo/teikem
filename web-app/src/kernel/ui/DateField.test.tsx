// Campo de fecha único (DateField / DateTimeField): calendario que abre solo, solo números, formato de la compañía, pegado, fecha y hora.
import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PR_FORMAT, resetFormatSettings, setFormatSettings } from '../format'
import { DateField } from './DateField'
import { DateTimeField } from './DateTimeField'

function Host({ initial = '', onValue, min, max, readOnly }: { initial?: string; onValue?: (v: string) => void; min?: string; max?: string; readOnly?: boolean }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <DateField
        aria-label="Fecha"
        value={value}
        min={min}
        max={max}
        readOnly={readOnly}
        onChange={(v) => {
          setValue(v)
          onValue?.(v)
        }}
      />
      <output data-testid="saved">{value}</output>
    </>
  )
}

afterEach(() => resetFormatSettings())

const day = (iso: string) => document.querySelector<HTMLButtonElement>(`button[data-iso="${iso}"]`)

describe('DateField', () => {
  it('al entrar se abre el calendario solo, y se puede escribir sin cerrarlo', async () => {
    const user = userEvent.setup()
    render(<Host />)
    expect(day('2026-10-21')).toBeNull()
    await user.click(screen.getByLabelText('Fecha'))
    expect(screen.getByRole('group', { name: 'Calendar' })).toBeInTheDocument()
    await user.type(screen.getByLabelText('Fecha'), '10212026')
    expect(screen.getByLabelText('Fecha')).toHaveValue('10/21/2026')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21')
    expect(screen.getByRole('group', { name: 'Calendar' })).toBeInTheDocument()   // sigue abierto
    expect(day('2026-10-21')).toHaveAttribute('aria-pressed', 'true')              // y se movió a esa fecha
  })

  it('el foco con el teclado también lo abre; Esc lo cierra', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.tab()
    expect(screen.getByLabelText('Fecha')).toHaveFocus()
    expect(screen.getByRole('group', { name: 'Calendar' })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('group', { name: 'Calendar' })).toBeNull()
  })

  it('solo entran números y los separadores aparecen solos; incompleta = vacía', async () => {
    const user = userEvent.setup()
    const onValue = vi.fn()
    render(<Host onValue={onValue} />)
    const input = screen.getByLabelText('Fecha')
    expect(input).toHaveAttribute('placeholder', 'MM/DD/YYYY')
    await user.type(input, '1a0-2/1')
    expect(input).toHaveValue('10/21')
    expect(screen.getByTestId('saved')).toHaveTextContent('')
    await user.type(input, '2026')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21')
    await user.type(input, '9999')   // no pasa de 8 dígitos
    expect(input).toHaveValue('10/21/2026')
    await user.type(input, '{Backspace}')
    expect(input).toHaveValue('10/21/202')
    expect(screen.getByTestId('saved')).toHaveTextContent('')   // al faltar un dígito la pantalla vuelve a tenerla vacía
  })

  it('un día que no existe no se acepta (se marca inválido y no sale)', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const input = screen.getByLabelText('Fecha')
    await user.type(input, '02302026')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByTestId('saved')).toHaveTextContent('')
  })

  it('con otro orden y separador (DMY con punto) se pinta y se lee con ese formato', async () => {
    act(() => setFormatSettings({ ...PR_FORMAT, dateOrder: 'DMY', dateSeparator: '.', weekStartDay: 1 }))
    const user = userEvent.setup()
    render(<Host />)
    const input = screen.getByLabelText('Fecha')
    expect(input).toHaveAttribute('placeholder', 'DD.MM.YYYY')
    await user.type(input, '21102026')
    expect(input).toHaveValue('21.10.2026')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21')
  })

  it('un valor que llega de afuera se pinta con el formato de la compañía', () => {
    render(<Host initial="2026-03-04" />)
    expect(screen.getByLabelText('Fecha')).toHaveValue('03/04/2026')
  })

  it('elegir un día del calendario la pone y lo cierra; «Hoy» y «Limpiar»', async () => {
    const user = userEvent.setup()
    render(<Host initial="2026-10-05" />)
    await user.click(screen.getByLabelText('Fecha'))
    await user.click(day('2026-10-14')!)
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-14')
    expect(screen.getByLabelText('Fecha')).toHaveValue('10/14/2026')
    expect(screen.queryByRole('group', { name: 'Calendar' })).toBeNull()
    await user.click(screen.getByLabelText('Fecha'))
    await user.click(screen.getByRole('button', { name: 'Clear' }))
    expect(screen.getByTestId('saved')).toHaveTextContent('')
    expect(screen.getByLabelText('Fecha')).toHaveValue('')
  })

  it('el calendario cambia de mes y no deja elegir fuera de min y max; una fecha tecleada fuera de rango sale igual, marcada', async () => {
    const user = userEvent.setup()
    render(<Host initial="2026-10-15" min="2026-10-10" max="2026-10-20" />)
    await user.click(screen.getByLabelText('Fecha'))
    expect(day('2026-10-09')).toBeDisabled()
    expect(day('2026-10-10')).toBeEnabled()
    expect(day('2026-10-21')).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Next month' }))
    expect(day('2026-11-01')).toBeDisabled()
    const input = screen.getByLabelText('Fecha')
    await user.clear(input)
    await user.type(input, '12312099')
    expect(screen.getByTestId('saved')).toHaveTextContent('2099-12-31')   // el formulario pone su propio mensaje («no puede ser futura»)
    expect(input).toHaveAttribute('aria-invalid', 'true')
  })

  it('una fecha pegada se cambia al formato de la compañía; la que no se entiende se rechaza', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const input = screen.getByLabelText('Fecha')
    await user.click(input)
    await user.paste('2026-10-21')   // ISO: se entiende siempre
    expect(input).toHaveValue('10/21/2026')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21')

    await user.click(input)
    await user.paste('Oct 21, 2026')
    expect(screen.getByRole('alert')).toHaveTextContent('MM/DD/YYYY')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveValue('10/21/2026')   // no cambió
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21')

    await user.click(input)
    await user.paste('3/4/2027')
    expect(input).toHaveValue('03/04/2027')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('solo lectura: se ve la fecha, no se abre el calendario y no se escribe', async () => {
    const user = userEvent.setup()
    render(<Host initial="2026-10-05" readOnly />)
    await user.click(screen.getByLabelText('Fecha'))
    expect(screen.queryByRole('group', { name: 'Calendar' })).toBeNull()
    expect(screen.getByLabelText('Fecha')).toHaveAttribute('readonly')
  })

  it('un ISO completo que llega como evento (autocompletado) se acepta', () => {
    const onValue = vi.fn()
    render(<Host onValue={onValue} />)
    fireEvent.change(screen.getByLabelText('Fecha'), { target: { value: '2026-10-12' } })
    expect(onValue).toHaveBeenLastCalledWith('2026-10-12')
    expect(screen.getByLabelText('Fecha')).toHaveValue('10/12/2026')
  })
})

function HostDt({ initial = '', onValue }: { initial?: string; onValue?: (v: string) => void }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <DateTimeField
        id="dt"
        aria-label="Cita"
        value={value}
        onChange={(v) => {
          setValue(v)
          onValue?.(v)
        }}
      />
      <output data-testid="saved">{value}</output>
    </>
  )
}

describe('DateTimeField', () => {
  it('al completar la fecha el foco pasa a la hora; solo números; con 12 horas lleva a. m./p. m.', async () => {
    const user = userEvent.setup()
    render(<HostDt />)
    await user.type(screen.getByLabelText('Cita'), '10212026')
    expect(screen.getByLabelText('Time')).toHaveFocus()
    await user.type(screen.getByLabelText('Time'), '9x3-0')
    expect(screen.getByLabelText('Time')).toHaveValue('09:30')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21T09:30')
    await user.selectOptions(screen.getByLabelText('AM or PM'), 'PM')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21T21:30')
  })

  it('con hora de 24: sin selector y con rango 00–23', async () => {
    act(() => setFormatSettings({ ...PR_FORMAT, timeFormat: 24 }))
    const user = userEvent.setup()
    render(<HostDt initial="2026-10-21T21:05" />)
    expect(screen.getByLabelText('Time')).toHaveValue('21:05')
    expect(screen.queryByLabelText('AM or PM')).toBeNull()
    await user.clear(screen.getByLabelText('Time'))
    await user.type(screen.getByLabelText('Time'), '2500')
    expect(screen.getByLabelText('Time')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByTestId('saved')).toHaveTextContent('')
  })

  it('una hora pegada se cambia al formato; la que no se entiende se rechaza', async () => {
    const user = userEvent.setup()
    render(<HostDt initial="2026-10-21T08:00" />)
    const time = screen.getByLabelText('Time')
    await user.click(time)
    await user.paste('9:30 pm')
    expect(time).toHaveValue('09:30')
    expect(screen.getByLabelText('AM or PM')).toHaveValue('PM')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21T21:30')
    await user.click(time)
    await user.paste('mediodía')
    expect(time).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByTestId('saved')).toHaveTextContent('2026-10-21T21:30')
  })

  it('un valor de afuera se reparte en fecha y hora con el formato de la compañía', () => {
    render(<HostDt initial="2026-09-30T00:00" />)
    expect(screen.getByLabelText('Cita')).toHaveValue('09/30/2026')
    expect(screen.getByLabelText('Time')).toHaveValue('12:00')
    expect(screen.getByLabelText('AM or PM')).toHaveValue('AM')
  })
})
