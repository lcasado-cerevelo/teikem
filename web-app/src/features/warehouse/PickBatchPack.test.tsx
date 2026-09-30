// Empacar una recolección (PackModal): Tipo de servicio y Tipo de paquete con el predeterminado de la compañía (decisión del
// 2026-09-30). Con predeterminado, la opción vacía dice "Predeterminado de la compañía (etiqueta)" y el campo es opcional;
// sin él, esa opción no aparece y el campo es obligatorio. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { PackModal } from './PackModal'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  settings: { defaultServiceType: null as string | null, defaultPackageType: null as string | null },
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.calls.push({ method: req.method, url })
    const body = route(url)
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/catalogs/ServiceType')
    return [
      { code: 'STANDARD', label: 'Estándar', sortOrder: 1 },
      { code: 'EXPRESS', label: 'Expreso', sortOrder: 2 },
    ]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', sortOrder: 1 }]
  if (p === '/api/v1/tenant/settings') return { id: 1, name: 'Advance Logistics', ...mock.settings }
  return []
}

const BATCH = { publicId: 'bbbbbbbb-0000-0000-0000-000000000001', number: 'PB-00001', clientName: 'Cliente Uno', rowVersion: 'AA==' }

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['warehouse.pick', 'orders.create']} modules={['WMS_LOTSERIAL']}>
          <PackModal batch={BATCH} onClose={() => {}} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Textos de las opciones de un <select>. */
const optionTexts = (select: HTMLElement) => Array.from((select as HTMLSelectElement).options).map((o) => o.text)
const settingsRead = () => mock.calls.some((c) => c.url.pathname === '/api/v1/tenant/settings')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.settings = { defaultServiceType: null, defaultPackageType: null }
})

describe('PackModal: predeterminados de la compañía', () => {
  it('con predeterminados: "Predeterminado de la compañía (Estándar/Caja)" y los campos no son obligatorios', async () => {
    const user = userEvent.setup()
    mock.settings = { defaultServiceType: 'STANDARD', defaultPackageType: 'BOX' }
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Empacar PB-00001' })
    const service = within(dialog).getByLabelText(/^Tipo de servicio/)
    await waitFor(() => expect(optionTexts(service)[0]).toBe('Predeterminado de la compañía (Estándar)'))
    expect(optionTexts(service)).toEqual(['Predeterminado de la compañía (Estándar)', 'Estándar', 'Expreso'])
    const pkg = within(dialog).getByLabelText(/^Tipo de paquete/)
    await waitFor(() => expect(optionTexts(pkg)[0]).toBe('Predeterminado de la compañía (Caja)'))
    expect(within(dialog).getByText('Tipo de servicio').closest('label')).not.toHaveTextContent('*')

    await user.click(within(dialog).getByRole('button', { name: 'Empacar y crear orden' }))
    expect(await within(dialog).findByText('Indique el cliente de la orden.')).toBeInTheDocument()
    expect(within(dialog).queryByText('Elija el tipo de servicio.')).toBeNull()
    expect(within(dialog).queryByText('Elija el tipo de paquete.')).toBeNull()
  })

  it('sin predeterminados: no se ofrece "Predeterminado de la compañía" y los campos son obligatorios', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Empacar PB-00001' })
    await waitFor(() => expect(settingsRead()).toBe(true))
    const service = within(dialog).getByLabelText(/^Tipo de servicio/)
    await waitFor(() => expect(optionTexts(service)).toEqual(['Elija el tipo de servicio', 'Estándar', 'Expreso']))
    expect(optionTexts(within(dialog).getByLabelText(/^Tipo de paquete/))).toEqual(['Elija el tipo de paquete', 'Caja'])
    expect(within(dialog).queryByText(/Predeterminado de la compañía/)).toBeNull()
    expect(within(dialog).getByText('Tipo de servicio').closest('label')).toHaveTextContent('*')

    await user.click(within(dialog).getByRole('button', { name: 'Empacar y crear orden' }))
    expect(await within(dialog).findByText('Elija el tipo de servicio.')).toBeInTheDocument()
    expect(within(dialog).getByText('Elija el tipo de paquete.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)

    // elegir ambos quita los errores
    await user.selectOptions(service, 'STANDARD')
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de paquete/), 'BOX')
    await user.click(within(dialog).getByRole('button', { name: 'Empacar y crear orden' }))
    await waitFor(() => expect(within(dialog).queryByText('Elija el tipo de servicio.')).toBeNull())
    expect(within(dialog).queryByText('Elija el tipo de paquete.')).toBeNull()
  })

  it('predeterminado que no está en el catálogo: se muestra su código', async () => {
    mock.settings = { defaultServiceType: 'OLD_SVC', defaultPackageType: null }
    wrap()
    const dialog = await screen.findByRole('dialog', { name: 'Empacar PB-00001' })
    const service = within(dialog).getByLabelText(/^Tipo de servicio/)
    await waitFor(() => expect(optionTexts(service)[0]).toBe('Predeterminado de la compañía (OLD_SVC)'))
    expect(optionTexts(within(dialog).getByLabelText(/^Tipo de paquete/))[0]).toBe('Elija el tipo de paquete')
  })
})
