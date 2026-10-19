// Lote 126 — Contrato → «Cotejo de factura»: encender el cobro pide el monto y manda enabled y amount; apagado manda enabled:false; solo lectura sin contracts.update.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import type { ContractDetail } from './contractRules'
import { InvoiceCheckFeeSection } from './InvoiceCheckFeeSection'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    if (req.method === 'PATCH' && url.pathname === '/api/v1/contracts/k-1/invoice-check-fee') return json({ ...CONTRACT, invoiceCheckFee: body })
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const CONTRACT = { publicId: 'k-1', id: 1, canEdit: true, invoiceCheckFee: { enabled: false, amount: null } } as unknown as ContractDetail

function wrap(contract: ContractDetail = CONTRACT, canWrite = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <InvoiceCheckFeeSection contract={contract} clientPublicId="c-1" canWrite={canWrite} />
    </QueryClientProvider>,
  )
}

describe('Cotejo de factura (contrato)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('encendido pide el monto; con monto manda enabled y amount', async () => {
    const user = userEvent.setup()
    wrap()
    expect(screen.queryByLabelText('Monto por factura cotejada')).toBeNull()
    await user.click(screen.getByLabelText('Cobrar el cotejo de la factura del cliente'))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Indique el monto por factura cotejada.')
    expect(mock.requests).toHaveLength(0)
    await user.type(screen.getByLabelText('Monto por factura cotejada'), '-1')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('no puede ser negativo')
    await user.clear(screen.getByLabelText('Monto por factura cotejada'))
    await user.type(screen.getByLabelText('Monto por factura cotejada'), '3,5')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests).toHaveLength(1))
    expect(mock.requests[0]).toMatchObject({ method: 'PATCH', path: '/api/v1/contracts/k-1/invoice-check-fee', body: { enabled: true, amount: 3.5 } })
  })

  it('apagar manda enabled:false sin monto; sin permiso todo es solo lectura', async () => {
    const user = userEvent.setup()
    const on = { ...CONTRACT, invoiceCheckFee: { enabled: true, amount: 2 } } as unknown as ContractDetail
    const first = wrap(on)
    expect(screen.getByLabelText('Monto por factura cotejada')).toHaveValue('2')
    await user.click(screen.getByLabelText('Cobrar el cotejo de la factura del cliente'))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests).toHaveLength(1))
    expect(mock.requests[0].body).toEqual({ enabled: false, amount: null })
    first.unmount()
    wrap(on, false)
    expect(screen.getByLabelText('Cobrar el cotejo de la factura del cliente')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar' })).toBeNull()
  })
})
