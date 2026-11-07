// Lote 222 — Ajustes → Operación → Ventas: se lee el cargo de envío, el mínimo y el IVU; se guarda con admin.tenant (vacío = 0) y sin permiso es solo lectura.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { SettingsSalesSection } from './SettingsSalesSection'

const mock = vi.hoisted(() => ({ settings: { charge: 5 as number | null, freeFrom: 100 as number | null, taxable: true, measuredAfterTax: true }, requests: [] as { method: string; path: string; body: unknown }[], fail: false }))

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    if (url.pathname !== '/api/v1/sales-orders/settings') return new Response('{}', { status: 404 })
    if (req.method === 'PATCH') {
      if (mock.fail) return new Response(JSON.stringify({ title: 'El cargo de envío debe ser un monto mayor que cero con hasta 2 decimales.', status: 400 }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
      const b = body as { charge: number; freeFrom: number; taxable: boolean }
      mock.settings = { ...mock.settings, charge: b.charge === 0 ? null : b.charge, freeFrom: b.freeFrom === 0 ? null : b.freeFrom, taxable: b.taxable }
    }
    return new Response(JSON.stringify(mock.settings), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function wrap(canEdit = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SettingsSalesSection canEdit={canEdit} />
    </QueryClientProvider>,
  )
}

const patches = () => mock.requests.filter((r) => r.method === 'PATCH')

describe('Ajustes → Operación → Ventas', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.settings = { charge: 5, freeFrom: 100, taxable: true, measuredAfterTax: true }
    mock.requests.length = 0
    mock.fail = false
  })

  it('muestra el cargo, el mínimo y el IVU de la compañía y guarda los cambios', async () => {
    const user = userEvent.setup()
    wrap()
    const charge = await screen.findByLabelText('Cargo de envío')
    expect(charge).toHaveValue('5')
    expect(screen.getByLabelText('Envío gratis desde')).toHaveValue('100')
    expect(screen.getByRole('switch', { name: 'El envío lleva IVU' })).toBeChecked()
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()
    await user.clear(charge)
    await user.type(charge, '7,5')
    await user.click(screen.getByRole('switch', { name: 'El envío lleva IVU' }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(patches()).toHaveLength(1))
    expect(patches()[0].body).toEqual({ charge: 7.5, freeFrom: 100, taxable: false })
  })

  it('vaciar el cargo y el mínimo manda 0 («sin cargo», «sin mínimo»)', async () => {
    const user = userEvent.setup()
    wrap()
    await user.clear(await screen.findByLabelText('Cargo de envío'))
    await user.clear(screen.getByLabelText('Envío gratis desde'))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(patches()).toHaveLength(1))
    expect(patches()[0].body).toEqual({ charge: 0, freeFrom: 0, taxable: true })
  })

  it('un cargo inválido no se envía y dice por qué; el error del servidor se muestra', async () => {
    const user = userEvent.setup()
    wrap()
    const charge = await screen.findByLabelText('Cargo de envío')
    await user.clear(charge)
    await user.type(charge, '1.234')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('El cargo debe ser mayor que cero y con hasta 2 decimales.')).toBeInTheDocument()
    expect(patches()).toHaveLength(0)
    mock.fail = true
    await user.clear(charge)
    await user.type(charge, '3')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('El cargo de envío debe ser un monto mayor que cero con hasta 2 decimales.')).toBeInTheDocument()
  })

  it('sin admin.tenant es solo lectura', async () => {
    wrap(false)
    expect(await screen.findByLabelText('Cargo de envío')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).not.toBeInTheDocument()
  })
})
