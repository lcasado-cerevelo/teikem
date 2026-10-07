// Contraseña temporal desde administración de usuarios (2026-10-07): se explica la regla de los 10 minutos, se puede dejar que el sistema la genere o
// escribir una, se muestra una sola vez con su vencimiento, y un error del API (p. ej. contraseña corta) queda dentro de la ventana.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { TemporaryPasswordDialog } from './TemporaryPasswordDialog'

const mock = vi.hoisted(() => ({ reply: null as unknown, bodies: [] as unknown[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    mock.bodies.push(JSON.parse((await req.text()) || 'null'))
    return mock.reply as Response
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const ok = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

function renderDialog(onClose = vi.fn()) {
  const qc = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  render(
    <QueryClientProvider client={qc}>
      <TemporaryPasswordDialog user={{ id: 7, fullName: 'Ana Ruiz' }} onClose={onClose} />
    </QueryClientProvider>,
  )
  return onClose
}

describe('TemporaryPasswordDialog', () => {
  beforeEach(() => {
    setLang('es')
    mock.bodies = []
  })
  afterEach(() => vi.restoreAllMocks())

  it('explica la regla, genera la contraseña (cuerpo vacío) y la muestra una vez con su vencimiento', async () => {
    mock.reply = ok({ password: 'Tq7mPz2RkWnV4xYb', expiresAtUtc: '2026-10-07T20:10:00Z', validMinutes: 10 })
    renderDialog()
    const user = userEvent.setup()
    expect(screen.getByText(/Vale 10 minutos/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Poner contraseña temporal' }))
    expect(await screen.findByTestId('temp-password-value')).toHaveTextContent('Tq7mPz2RkWnV4xYb')
    expect(screen.getByText(/Dele esta contraseña a Ana Ruiz ahora: vale 10 minutos/)).toBeInTheDocument()
    expect(mock.bodies[0]).toEqual({ password: null })
  })

  it('manda la contraseña que escribió el administrador', async () => {
    mock.reply = ok({ password: 'Clave-Que-Escribi-77', expiresAtUtc: '2026-10-07T20:10:00Z', validMinutes: 10 })
    renderDialog()
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Contraseña (opcional)'), 'Clave-Que-Escribi-77')
    await user.click(screen.getByRole('button', { name: 'Poner contraseña temporal' }))
    expect(await screen.findByTestId('temp-password-value')).toHaveTextContent('Clave-Que-Escribi-77')
    expect(mock.bodies[0]).toEqual({ password: 'Clave-Que-Escribi-77' })
  })

  it('un error del API queda dentro de la ventana y no muestra contraseña', async () => {
    mock.reply = new Response(JSON.stringify({ title: 'Datos inválidos.', status: 400, code: 'validation', errors: { password: ['Passwords must be at least 12 characters.'] } }), {
      status: 400,
      headers: { 'Content-Type': 'application/problem+json' },
    })
    renderDialog()
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Contraseña (opcional)'), 'corta')
    await user.click(screen.getByRole('button', { name: 'Poner contraseña temporal' }))
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.queryByTestId('temp-password-value')).toBeNull()
  })
})
