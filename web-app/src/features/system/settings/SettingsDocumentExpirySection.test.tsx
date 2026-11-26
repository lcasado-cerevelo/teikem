// Lote 248 — Ajustes → Operación → Avisos de vencimiento: umbrales como chips (de mayor a menor), validación, interruptores y guardado por PUT /tenant/settings; sin admin.tenant es solo lectura.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../../kernel/access'
import { setLang } from '../../../kernel/i18n/i18n'
import { SettingsDocumentExpirySection } from './SettingsDocumentExpirySection'
import type { TenantSettingsDto } from '../tenantSettingsApi'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: Record<string, unknown> | undefined }[], fail: false }))

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: new URL(req.url).pathname, body: text ? JSON.parse(text) : undefined })
    if (mock.fail) return new Response(JSON.stringify({ title: 'Hay un umbral repetido.', status: 400 }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
    return new Response(JSON.stringify({}), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const SETTINGS = { docExpiryThresholdDays: [30, 15, 5], docExpiryDailyAfterExpiry: false, docExpiryNotifyClient: false } as TenantSettingsDto

function wrap(canEdit = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={new Set(['admin.tenant', 'notifications.manage'])} modules={new Set(['CATALOG'])}>
          <SettingsDocumentExpirySection settings={SETTINGS} canEdit={canEdit} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('Ajustes → Avisos de vencimiento', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.fail = false
  })

  it('muestra los umbrales de mayor a menor y el enlace a las reglas de aviso', () => {
    wrap()
    expect(screen.getByText('30 días antes')).toBeInTheDocument()
    expect(screen.getByText('5 días antes')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sistema → Reglas de aviso' })).toHaveAttribute('href', '/system/notifications')
  })

  it('agrega un umbral, rechaza el repetido y el que no es número, y guarda con los interruptores', async () => {
    const user = userEvent.setup()
    wrap()
    const add = screen.getByLabelText('Días para avisar')
    await user.type(add, '15')
    await user.click(screen.getByRole('button', { name: 'Agregar' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Hay un umbral repetido.')
    await user.clear(add)
    await user.type(add, 'abc')
    await user.click(screen.getByRole('button', { name: 'Agregar' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Cada umbral es un número entero de días entre -365 y 365')
    await user.clear(add)
    await user.type(add, '0')
    await user.click(screen.getByRole('button', { name: 'Agregar' }))
    expect(screen.getByText('El día que vence')).toBeInTheDocument()
    await user.click(screen.getByRole('switch', { name: 'Avisar al cliente' }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'PUT')).toHaveLength(1))
    expect(mock.requests.find((r) => r.method === 'PUT')?.body).toEqual({ docExpiryThresholdDays: [30, 15, 5, 0], docExpiryDailyAfterExpiry: false, docExpiryNotifyClient: true })
  })

  it('un error del servidor se muestra junto al botón', async () => {
    mock.fail = true
    const user = userEvent.setup()
    wrap()
    await user.click(screen.getByRole('switch', { name: 'Avisar a diario después de vencido' }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('Hay un umbral repetido.')).toBeInTheDocument()
  })

  it('sin permiso de edición no hay botones de guardar', () => {
    wrap(false)
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).not.toBeInTheDocument()
  })
})
