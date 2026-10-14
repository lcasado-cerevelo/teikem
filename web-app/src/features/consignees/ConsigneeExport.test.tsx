// Exportar en la pantalla Consignatarios: el botón de la cabecera exporta EXACTAMENTE lo que la lista tiene a la vista
// (filtro de cliente, «Mostrar inactivos» y buscador), y `?client=` abre la lista ya filtrada por ese cliente (lo usa «Ver todos»
// del panel Consignatarios de la ficha del cliente). Más las columnas del archivo.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { translate } from '../../kernel/i18n/i18n'
import { consigneeExportColumns } from './consigneeExport'
import ConsigneeListScreen from './ConsigneeListScreen'
import type { Consignee } from './consigneeRules'

const CLIENT_A = '11111111-1111-1111-1111-111111111111'
const mock = vi.hoisted(() => ({
  urls: [] as URL[],
  exported: [] as { format: string; names: string[]; headers: string[] }[],
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.urls.push(url)
    let body: unknown = []
    if (url.pathname === '/api/v1/clients') body = [{ id: 1, publicId: CLIENT_A, name: 'Cliente A', isActive: true }]
    else if (url.pathname === '/api/v1/locations') {
      const all = [
        { id: 1, publicId: 'a0000000-0000-0000-0000-000000000001', clientPublicId: CLIENT_A, clientName: 'Cliente A', isShared: false, code: 'A-1', name: 'Alfa Piezas', city: 'Ponce', isActive: true },
        { id: 2, publicId: 'a0000000-0000-0000-0000-000000000002', clientPublicId: CLIENT_A, clientName: 'Cliente A', isShared: false, code: 'A-2', name: 'Beta Auto', city: 'Ponce', isActive: true },
        { id: 3, publicId: 'a0000000-0000-0000-0000-000000000003', clientPublicId: null, clientName: null, isShared: true, code: 'S-1', name: 'Gamma Compartido', city: 'Caguas', isActive: true },
      ]
      const cid = url.searchParams.get('clientId')
      body = (cid ? all.filter((c) => c.clientPublicId === cid) : all).map((c) => ({ locationType: 'DELIVERY', country: 'PR', defaultServiceMinutes: 0, ...c }))
    }
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
vi.mock('../../kernel/ui/exportTable', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/exportTable')>()
  return {
    ...actual,
    exportTable: async (format: string, columns: { header: string }[], rows: { name: string }[]) => {
      mock.exported.push({ format, headers: columns.map((c) => c.header), names: rows.map((r) => r.name) })
    },
  }
})

function renderScreen(path = '/catalog/consignees') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={['locations.read']} modules={['CATALOG']}>
          <ConsigneeListScreen />
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

describe('Consignatarios — exportar', () => {
  it('exporta lo que la lista muestra, con el buscador aplicado, y el botón está en la cabecera, antes de Importar y Nuevo', async () => {
    const user = userEvent.setup()
    renderScreen()
    await screen.findByText('Alfa Piezas')
    // orden de la cabecera: Cliente, Exportar, Importar, Nuevo (solo con permiso de crear salen los dos últimos)
    await user.click(screen.getByRole('button', { name: /Exportar/ }))
    await user.click(await screen.findByRole('menuitem', { name: /CSV/ }))
    await waitFor(() => expect(mock.exported).toHaveLength(1))
    expect(mock.exported[0].names).toEqual(['Alfa Piezas', 'Beta Auto', 'Gamma Compartido'])

    await user.type(screen.getByRole('searchbox'), 'Caguas')
    await waitFor(() => expect(screen.queryByText('Alfa Piezas')).toBeNull())
    await user.click(screen.getByRole('button', { name: /Exportar/ }))
    await user.click(await screen.findByRole('menuitem', { name: /Excel/ }))
    await waitFor(() => expect(mock.exported).toHaveLength(2))
    expect(mock.exported[1].names).toEqual(['Gamma Compartido'])
  })

  it('`?client=` abre la lista filtrada por ese cliente y lo exportado es solo de ese cliente', async () => {
    const user = userEvent.setup()
    renderScreen(`/catalog/consignees?client=${CLIENT_A}`)
    await screen.findByText('Alfa Piezas')
    expect(screen.queryByText('Gamma Compartido')).toBeNull()
    expect(screen.getByRole('combobox', { name: /Cliente/ })).toHaveValue(CLIENT_A)
    expect(mock.urls.some((u) => u.pathname === '/api/v1/locations' && u.searchParams.get('clientId') === CLIENT_A)).toBe(true)
    await user.click(screen.getByRole('button', { name: /Exportar/ }))
    await user.click(await screen.findByRole('menuitem', { name: /CSV/ }))
    await waitFor(() => expect(mock.exported).toHaveLength(1))
    expect(mock.exported[0].names).toEqual(['Alfa Piezas', 'Beta Auto'])
  })

  it('las columnas del archivo: número, nombre, cliente, direcciones, pueblo, estado, código postal, país, contacto y estado del registro', () => {
    const t = (key: string, vars?: Record<string, string | number>) => translate('es', key, vars)
    const cols = consigneeExportColumns(t)
    expect(cols.map((c) => c.header)).toEqual([
      'Número', 'Nombre', 'Cliente', 'Dirección 1', 'Dirección 2', 'Pueblo', 'Estado', 'Código postal', 'País', 'Persona de contacto', 'Estado del registro',
    ])
    const row = { code: 'A-1', name: 'Alfa', isShared: true, line1: 'Calle 1', city: 'Ponce', countryLabel: 'Puerto Rico', isActive: false, contactName: 'Ana' } as Consignee
    expect(cols.map((c) => c.cell(row))).toEqual(['A-1', 'Alfa', 'Compartido (todos los clientes)', 'Calle 1', '', 'Ponce', '', '', 'Puerto Rico', 'Ana', 'Inactivo'])
  })
})
