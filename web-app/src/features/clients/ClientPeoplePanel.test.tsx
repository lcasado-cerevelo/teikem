// Lote 243 — Personas de contacto del cliente: la marca «Recibe avisos por correo» (casilla, chip en la lista y aviso si no hay correo).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientPeoplePanel } from './ClientPeoplePanel'
import type { ClientDetail } from './clientRules'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: Record<string, unknown> | undefined }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? (JSON.parse(text) as Record<string, unknown>) : undefined })
    return new Response(JSON.stringify({}), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const mail = (value: string) => ({ id: 1, contactType: 'EMAIL', value, isPrimary: true, isActive: true })
const client = {
  id: 7,
  publicId: '11111111-1111-1111-1111-111111111111',
  name: 'Cliente Demo',
  contacts: [
    { id: 1, fullName: 'Ana Marcada', isPrimary: true, isActive: true, receivesEmailNotifications: true, hasEmail: true, contactPoints: [mail('ana@x.com')] },
    { id: 2, fullName: 'Beto Sin Correo', isPrimary: false, isActive: true, receivesEmailNotifications: true, hasEmail: false, contactPoints: [] },
    { id: 3, fullName: 'Carla No Marcada', isPrimary: false, isActive: true, receivesEmailNotifications: false, hasEmail: true, contactPoints: [mail('carla@x.com')] },
  ],
} as unknown as ClientDetail

function renderPanel(perms = ['clients.update', 'contacts.manage']) {
  return render(
    <MemoryRouter>
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <AccessProvider permissions={perms} modules={['CATALOG']}>
          <ClientPeoplePanel client={client} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

async function expand(user: ReturnType<typeof userEvent.setup>) {
  const toggle = screen.queryByText('Ana Marcada') ? null : screen.getByRole('button', { name: /Personas de contacto/ })
  if (toggle) await user.click(toggle)
  return screen.findByRole('table')
}

describe('Personas de contacto — avisos por correo', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('la lista pone el chip «Avisos por correo» solo a las marcadas', async () => {
    const user = userEvent.setup()
    renderPanel()
    const table = await expand(user)
    const rowOf = (name: string) => within(table).getByText(name).closest('tr') as HTMLElement
    expect(within(rowOf('Ana Marcada')).getByText('Avisos por correo')).toBeInTheDocument()
    expect(within(rowOf('Beto Sin Correo')).getByText('Avisos por correo')).toBeInTheDocument()
    expect(within(rowOf('Carla No Marcada')).queryByText('Avisos por correo')).toBeNull()
  })

  it('una marcada sin correo avisa en la lista (marca de advertencia) y al editarla', async () => {
    const user = userEvent.setup()
    renderPanel()
    const table = await expand(user)
    const row = within(table).getByText('Beto Sin Correo').closest('tr') as HTMLElement
    expect(within(row).getByText('Avisos por correo')).toHaveAttribute('title', 'Marcado para avisos pero sin correo: agréguele un correo.')
    expect(within(table).getByText('Avisos por correo', { selector: 'span.chip.s-warn' })).toBeInTheDocument()
    await user.click(within(row).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('switch', { name: 'Recibe avisos por correo' })).toBeChecked()
    expect(within(dialog).getByText('Marcado para avisos pero sin correo: agréguele un correo.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Correo'), 'beto@x.com')
    expect(within(dialog).queryByText('Marcado para avisos pero sin correo: agréguele un correo.')).toBeNull()
  })

  it('el alta manda receivesEmailNotifications; sin marcar va en false y sin aviso', async () => {
    const user = userEvent.setup()
    renderPanel()
    await expand(user)
    await user.click(screen.getByRole('button', { name: 'Agregar contacto' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).queryByText('Marcado para avisos pero sin correo: agréguele un correo.')).toBeNull()
    await user.type(within(dialog).getByLabelText(/Nombre/), 'Dana Nueva')
    const box = within(dialog).getByRole('switch', { name: 'Recibe avisos por correo' })
    expect(box).not.toBeChecked()
    await user.click(box)
    expect(within(dialog).getByText('Marcado para avisos pero sin correo: agréguele un correo.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Correo'), 'dana@x.com')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path.endsWith('/contacts'))).toBe(true))
    const post = mock.requests.find((r) => r.method === 'POST' && r.path.endsWith('/contacts'))!
    expect(post.body).toMatchObject({ fullName: 'Dana Nueva', receivesEmailNotifications: true, contactPoints: [{ contactType: 'EMAIL', value: 'dana@x.com' }] })
  })

  it('editar y desmarcar manda false con el PATCH', async () => {
    const user = userEvent.setup()
    renderPanel()
    const table = await expand(user)
    const row = within(table).getByText('Ana Marcada').closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog')
    const box = within(dialog).getByRole('switch', { name: 'Recibe avisos por correo' })
    expect(box).toBeChecked()
    await user.click(box)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH' && r.path.endsWith('/contacts/1'))).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PATCH' && r.path.endsWith('/contacts/1'))!.body).toMatchObject({ receivesEmailNotifications: false })
  })
})
