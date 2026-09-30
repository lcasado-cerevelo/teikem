// Lote 2 — Proveedores: filtros de texto en el cliente (Nombre, Contacto, Teléfono por dígitos, Correo), tabla sin buscador,
// clic en la fila abre el modal (solo con purchasing.manage), baja como ícono con tooltip, máscara de teléfono y su validación,
// correo inválido y término de pago con buscador. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import SupplierListScreen from './SupplierListScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const SUPPLIERS = [
  { id: 1, name: 'Acme Corp', contactName: 'Ana Pérez', phone: '7875551234', email: 'ventas@acme.com', paymentTermCode: 'NET30', isActive: true, rowVersion: 'AA==' },
  { id: 2, name: 'Bolt SA', contactName: 'Luis Ríos', phone: '9395550000', email: 'bolt@bolt.com', isActive: true, rowVersion: 'AA==' },
]

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/suppliers') return SUPPLIERS
  if (p === '/api/v1/catalogs/PaymentTerm')
    return [
      { code: 'NET30', label: 'Neto 30' },
      { code: 'NET60', label: 'Neto 60' },
    ]
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  if (method !== 'GET') return SUPPLIERS[0]
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function wrap(permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['PURCHASING']}>
          <SupplierListScreen />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const MANAGE = ['purchasing.view', 'purchasing.manage']

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('Proveedores', () => {
  it('sin buscador en la tabla y filtros Nombre/Contacto/Teléfono/Correo en el cliente', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await screen.findAllByText('Acme Corp')
    expect(screen.queryByRole('searchbox', { name: /Buscar/ })).toBeNull()
    const table = screen.getByRole('table')

    await user.type(screen.getByLabelText('Nombre'), 'bolt')
    expect(within(table).queryByText('Acme Corp')).toBeNull()
    expect(within(table).getByText('Bolt SA')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Nombre'))

    await user.type(screen.getByLabelText('Contacto'), 'perez')
    expect(within(table).getByText('Acme Corp')).toBeInTheDocument()
    expect(within(table).queryByText('Bolt SA')).toBeNull()
    await user.clear(screen.getByLabelText('Contacto'))

    // el teléfono coincide por dígitos, sin importar el formato
    await user.type(screen.getByLabelText('Teléfono'), '939-555')
    expect(within(table).getByText('Bolt SA')).toBeInTheDocument()
    expect(within(table).queryByText('Acme Corp')).toBeNull()
    await user.clear(screen.getByLabelText('Teléfono'))

    await user.type(screen.getByLabelText('Correo electrónico'), 'acme.com')
    expect(within(table).getByText('Acme Corp')).toBeInTheDocument()
    expect(within(table).queryByText('Bolt SA')).toBeNull()
  })

  it('clic en la fila abre el modal con purchasing.manage; sin él no hace nada', async () => {
    const user = userEvent.setup()
    const view = wrap(MANAGE)
    await user.click((await screen.findAllByText('Acme Corp'))[0])
    const dialog = await screen.findByRole('dialog', { name: 'Editar' })
    expect(within(dialog).getByLabelText(/Nombre/)).toHaveValue('Acme Corp')
    expect(within(dialog).getByLabelText('Teléfono')).toHaveValue('(787)555-1234')
    view.unmount()

    wrap(['purchasing.view'])
    await user.click((await screen.findAllByText('Acme Corp'))[0])
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('la baja es un ícono con tooltip y pide confirmación', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await screen.findAllByText('Acme Corp')
    const icon = screen.getAllByRole('button', { name: 'Dar de baja' })[0]
    expect(icon).toHaveAttribute('title', 'Dar de baja')
    expect(icon).not.toHaveTextContent('Dar de baja')
    await user.click(icon)
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST' && c.url.pathname.endsWith('/deactivate'))).toBe(true))
  })

  it('máscara de teléfono, validación de 10 dígitos y correo inválido', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Nuevo proveedor' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo proveedor' })
    const phone = within(dialog).getByLabelText('Teléfono')
    await user.type(phone, '78755')
    expect(phone).toHaveValue('(787)55')
    await user.type(within(dialog).getByLabelText(/Nombre/), 'Nuevo')
    await user.type(within(dialog).getByLabelText('Correo electrónico'), 'no-es-correo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El teléfono debe tener 10 dígitos: (xxx)xxx-xxxx.')).toBeInTheDocument()
    expect(within(dialog).getByText('El correo electrónico no es válido.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)
    await user.type(phone, '51234999')
    // la máscara corta en 10 dígitos
    expect(phone).toHaveValue('(787)555-1234')
  })

  it('término de pago con buscador', async () => {
    const user = userEvent.setup()
    wrap(MANAGE)
    await user.click(await screen.findByRole('button', { name: 'Nuevo proveedor' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo proveedor' })
    const term = within(dialog).getByRole('combobox', { name: /Término de pago/ })
    await user.type(term, '60')
    expect(await screen.findByText('Neto 60')).toBeInTheDocument()
    expect(screen.queryByText('Neto 30')).toBeNull()
  })
})
