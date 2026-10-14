// Panel «Consignatarios» de la ficha del cliente: tabla de 10 filas como máximo (contador y «Mostrando 10 de N»), buscador simple,
// Exportar con lo filtrado (todas las filas, no solo las 10 visibles), Importar y Nuevo abren las ventanas de Consignatarios
// con este cliente como dueño, y los permisos (sin locations.read no hay panel; sin locations.create no hay Importar ni Nuevo).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientConsigneesPanel } from './ClientConsigneesPanel'
import type { ClientDetail } from './clientRules'

const mock = vi.hoisted(() => ({
  urls: [] as URL[],
  exported: [] as { format: string; headers: string[]; names: string[] }[],
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.urls.push(url)
    const body = url.pathname === '/api/v1/locations' ? rows() : url.pathname === '/api/v1/clients' ? [{ id: 7, publicId: CLIENT_ID, name: 'Cliente Demo', isActive: true }] : []
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
// la descarga real no se prueba aquí: se captura lo que se manda a exportar
vi.mock('../../kernel/ui/exportTable', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/exportTable')>()
  return {
    ...actual,
    exportTable: async (format: string, columns: { header: string; cell: (r: unknown) => unknown }[], rows: { name: string }[]) => {
      mock.exported.push({ format, headers: columns.map((c) => c.header), names: rows.map((r) => r.name) })
    },
  }
})

const CLIENT_ID = '11111111-1111-1111-1111-111111111111'
const client = { id: 7, publicId: CLIENT_ID, name: 'Cliente Demo' } as unknown as ClientDetail

function rows() {
  return Array.from({ length: 14 }, (_, i) => {
    const n = String(i + 1).padStart(2, '0')
    return {
      id: 100 + i,
      publicId: `00000000-0000-0000-0000-0000000000${n}`,
      clientPublicId: CLIENT_ID,
      clientName: 'Cliente Demo',
      isShared: false,
      code: `C-${n}`,
      name: i === 12 ? 'Zeta Especial' : `Consignatario ${n}`,
      locationType: 'DELIVERY',
      line1: `Calle ${n}`,
      city: i === 12 ? 'Ponce' : 'San Juan',
      country: 'PR',
      isActive: true,
      contactName: null,
    }
  })
}

function renderPanel(perms: string[]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={perms} modules={['CATALOG']}>
          <ClientConsigneesPanel client={client} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.urls.length = 0
  mock.exported.length = 0
})

describe('Clientes — panel «Consignatarios» de la ficha', () => {
  it('pide solo los consignatarios de este cliente y pagina ahí mismo: 10 filas por página (14 en total) y la segunda página trae las demás', async () => {
    const user = userEvent.setup()
    renderPanel(['locations.read', 'locations.create'])
    expect(await screen.findByText('Consignatario 01')).toBeInTheDocument()
    const call = mock.urls.find((u) => u.pathname === '/api/v1/locations')!
    expect(call.searchParams.get('clientId')).toBe(CLIENT_ID)
    expect(call.searchParams.get('includeShared')).toBe('false')
    expect(screen.getByText('Consignatario 10')).toBeInTheDocument()
    expect(screen.queryByText('Consignatario 11')).toBeNull()
    // la paginación está en la propia tabla (sin salir de la ficha)
    await user.click(screen.getByRole('button', { name: /siguiente|next|›|»/i }))
    expect(await screen.findByText('Consignatario 11')).toBeInTheDocument()
    expect(screen.getByText('Zeta Especial')).toBeInTheDocument()
    expect(screen.queryByText('Consignatario 01')).toBeNull()
  })

  it('el buscador filtra todas las filas (no solo las 10 visibles) y quita el «Mostrando…» si caben', async () => {
    const user = userEvent.setup()
    renderPanel(['locations.read'])
    await screen.findByText('Consignatario 01')
    await user.type(screen.getByRole('searchbox'), 'Ponce')
    expect(await screen.findByText('Zeta Especial')).toBeInTheDocument()
    expect(screen.queryByText('Consignatario 01')).toBeNull()
    expect(screen.queryByText('Mostrando', { exact: false })).toBeNull()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 'zzzz')
    expect(await screen.findByText('Ningún consignatario de este cliente coincide con la búsqueda.')).toBeInTheDocument()
  })

  it('Exportar manda las 14 filas filtradas, no solo las 10 de la tabla; con búsqueda, solo las que coinciden', async () => {
    const user = userEvent.setup()
    renderPanel(['locations.read'])
    await screen.findByText('Consignatario 01')
    await user.click(screen.getByRole('button', { name: /Exportar/ }))
    await user.click(await screen.findByRole('menuitem', { name: /CSV/ }))
    await waitFor(() => expect(mock.exported).toHaveLength(1))
    expect(mock.exported[0].format).toBe('csv')
    expect(mock.exported[0].names).toHaveLength(14)
    expect(mock.exported[0].headers).toContain('Persona de contacto')

    await user.type(screen.getByRole('searchbox'), 'Ponce')
    await screen.findByText('Zeta Especial')
    await user.click(screen.getByRole('button', { name: /Exportar/ }))
    await user.click(await screen.findByRole('menuitem', { name: /Excel/ }))
    await waitFor(() => expect(mock.exported).toHaveLength(2))
    expect(mock.exported[1].names).toEqual(['Zeta Especial'])
  })

  it('«Nuevo consignatario» e «Importar» abren sus ventanas con este cliente; sin locations.create no están', async () => {
    const user = userEvent.setup()
    const { unmount } = renderPanel(['locations.read', 'locations.create'])
    await screen.findByText('Consignatario 01')
    await user.click(screen.getByRole('button', { name: /Nuevo consignatario/ }))
    const dialog = await screen.findByRole('dialog')
    // el dueño con el que arranca el alta es este cliente
    expect(await within(dialog).findByDisplayValue('Cliente Demo')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    unmount()

    renderPanel(['locations.read'])
    await screen.findByText('Consignatario 01')
    expect(screen.queryByRole('button', { name: /Nuevo consignatario/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Importar' })).toBeNull()
    expect(screen.getByRole('button', { name: /Exportar/ })).toBeInTheDocument()
  })

  it('sin locations.read no hay panel ni se pide la lista', async () => {
    renderPanel(['clients.read'])
    await new Promise((r) => setTimeout(r, 50))
    expect(screen.queryByText('Consignatarios')).toBeNull()
    expect(mock.urls.some((u) => u.pathname === '/api/v1/locations')).toBe(false)
  })
})
