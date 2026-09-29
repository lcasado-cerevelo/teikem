// Pruebas de "Aparatos móviles" (Lote F8a P5) sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import DevicesPage from './DevicesPage'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; url: URL; body: unknown }[], enrollSeq: 0 }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, url, body: text ? JSON.parse(text) : undefined })
    const p = url.pathname

    if (req.method === 'GET' && p === '/api/v1/devices') {
      return json(DEVICES)
    }
    if (req.method === 'GET' && p === '/api/v1/warehouses') {
      return json([{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }])
    }
    if (req.method === 'POST' && p === '/api/v1/devices') {
      return json({ device: { ...DEVICES[0], publicId: 'new-device' }, enrollCode: 'CREATE01' })
    }
    if (req.method === 'POST' && p.endsWith('/deactivate')) {
      return json({ ...DEVICES[0], isActive: false })
    }
    if (req.method === 'POST' && p.endsWith('/reactivate')) {
      return json({ ...DEVICES[1], isActive: true })
    }
    if (req.method === 'POST' && p.endsWith('/enroll-code')) {
      mock.enrollSeq += 1
      return json({ device: DEVICES[0], enrollCode: `NEWCODE${mock.enrollSeq}` })
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const WH = '11111111-1111-1111-1111-111111111111'

const DEVICES = [
  {
    publicId: 'device-1',
    code: 'MC33-014',
    name: 'Zebra MC3300 — Recibo 1',
    isActive: true,
    isEnrolled: true,
    lastSeenUtc: '2026-09-28T11:57:00',
    appVersion: '1.0.0',
    defaultWarehousePublicId: WH,
    defaultWarehouseCode: 'ALM-01',
  },
  {
    publicId: 'device-2',
    code: 'TAB-007',
    name: 'Tablet conteo cíclico',
    isActive: false,
    isEnrolled: true,
    lastSeenUtc: '2026-09-22T12:00:00',
    appVersion: '0.9.4',
    defaultWarehousePublicId: null,
    defaultWarehouseCode: null,
  },
]

function wrap(ui: ReactNode, permissions: string[] = ['devices.manage'], modules: string[] = ['WMS_LOTSERIAL']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={modules}>
        {ui}
      </AccessProvider>
    </QueryClientProvider>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.enrollSeq = 0
})

describe('DevicesPage', () => {
  it('lista los aparatos y la tabla se ordena por columna', async () => {
    const user = userEvent.setup()
    wrap(<DevicesPage />)
    await screen.findByText('MC33-014')

    // TAB-007 está inactivo: con "Incluir inactivos" aparecen ambos
    await user.click(screen.getByLabelText('Mostrar'))
    await user.selectOptions(screen.getByLabelText('Mostrar'), 'all')
    await screen.findByText('TAB-007')

    // Orden por defecto ascendente por código: MC33-014 antes que TAB-007
    let rows = screen.getAllByRole('row').slice(1)
    expect(within(rows[0]).getByText('MC33-014')).toBeInTheDocument()

    // clic en el encabezado "Código" invierte el orden (el botón de orden vive dentro del <th>)
    await user.click(screen.getByRole('button', { name: /Código/ }))
    rows = screen.getAllByRole('row').slice(1)
    expect(within(rows[0]).getByText('TAB-007')).toBeInTheDocument()
  })

  it('"Nuevo aparato" abre el modal del código de registro tras crear', async () => {
    const user = userEvent.setup()
    wrap(<DevicesPage />)
    await screen.findByText('MC33-014')

    await user.click(screen.getByRole('button', { name: 'Nuevo aparato' }))
    await user.type(screen.getByLabelText(/^Nombre/), 'Tablet nueva')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))

    expect(await screen.findByText('CREATE01')).toBeInTheDocument()
    expect(screen.getByText(/Escríbelo en la pantalla Registrar/)).toBeInTheDocument()

    // la pantalla no pide ni manda código: lo genera el servidor (Lote F8a, P5)
    const createReq = mock.requests.find((r) => r.method === 'POST' && r.url.pathname === '/api/v1/devices')
    expect(createReq?.body).toEqual({ name: 'Tablet nueva', defaultWarehousePublicId: null })
  })

  it('"Nuevo código de registro" pide confirmación y el código es distinto del anterior', async () => {
    const user = userEvent.setup()
    wrap(<DevicesPage />)
    await screen.findByText('MC33-014')

    await user.click(screen.getAllByRole('button', { name: 'Nuevo código de registro' })[0])
    // ConfirmDialog: el título/acción aparece dos veces (botón de fila ya cerrado + confirmar); se confirma
    const confirmButtons = screen.getAllByRole('button', { name: 'Nuevo código de registro' })
    await user.click(confirmButtons[confirmButtons.length - 1])

    const firstCode = await screen.findByText(/^NEWCODE1$/)
    expect(firstCode).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Listo' }))

    await user.click(screen.getAllByRole('button', { name: 'Nuevo código de registro' })[0])
    const confirmButtons2 = screen.getAllByRole('button', { name: 'Nuevo código de registro' })
    await user.click(confirmButtons2[confirmButtons2.length - 1])
    const secondCode = await screen.findByText(/^NEWCODE2$/)
    expect(secondCode).toBeInTheDocument()
    expect(secondCode.textContent).not.toBe(firstCode.textContent)
  })

  it('Desactivar llama al endpoint correcto tras confirmar', async () => {
    const user = userEvent.setup()
    wrap(<DevicesPage />)
    await screen.findByText('MC33-014')

    await user.click(screen.getAllByRole('button', { name: 'Desactivar' })[0])
    const confirmButtons = screen.getAllByRole('button', { name: 'Desactivar' })
    await user.click(confirmButtons[confirmButtons.length - 1])

    await waitFor(() =>
      expect(mock.requests.some((r) => r.method === 'POST' && r.url.pathname === '/api/v1/devices/device-1/deactivate')).toBe(true),
    )
  })

  it('Reactivar llama al endpoint correcto tras confirmar', async () => {
    const user = userEvent.setup()
    wrap(<DevicesPage />)
    await screen.findByText('TAB-007')

    await user.click(screen.getByRole('button', { name: 'Reactivar' }))
    const confirmButtons = screen.getAllByRole('button', { name: 'Reactivar' })
    await user.click(confirmButtons[confirmButtons.length - 1])

    await waitFor(() =>
      expect(mock.requests.some((r) => r.method === 'POST' && r.url.pathname === '/api/v1/devices/device-2/reactivate')).toBe(true),
    )
  })

  it('sin devices.manage no se ofrece "Nuevo aparato" ni acciones de fila', async () => {
    wrap(<DevicesPage />, [], ['WMS_LOTSERIAL'])
    await screen.findByText('MC33-014')
    expect(screen.queryByRole('button', { name: 'Nuevo aparato' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Desactivar' })).toBeNull()
  })
})
