// Lote 249 — bandeja «Por aprobar»: paginada por el servidor (skip/take/q) y con la decisión de otra persona (no la de quien registró).
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { PendingApprovalsTab } from './PendingApprovalsTab'
import { mock, wrap } from './testKit'

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fetchFor } = await import('./testKit')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fetchFor() }) }
})

const ITEM = {
  publicId: 'ch-1', orderPublicId: 'o-1', orderNumber: 'OV-00001', clientName: 'Cliente Uno', changeNo: 2, kind: 'MODIFY', status: 'PENDING_APPROVAL', statusLabel: 'Por aprobar', isPending: true,
  registeredBy: 5, registeredByName: 'Ana', registeredAtUtc: '2026-11-26T14:00:00', authorizedByName: 'Marta', channel: 'EMAIL', channelLabel: 'Por correo', reason: 'x', totalBefore: 100, totalAfter: 150, totalDelta: 50, requiresApproval: true,
}

describe('Bandeja «Por aprobar»', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.routes = { 'GET /api/v1/sales-order-changes/pending': () => ({ json: { items: [ITEM], total: 1, skip: 0, take: 25 } }) }
  })

  it('pide la página al servidor y el buscador libre va al servidor', async () => {
    const user = userEvent.setup()
    wrap(<PendingApprovalsTab />)
    expect(await screen.findByText('OV-00001')).toBeInTheDocument()
    expect(screen.getByText('Cliente Uno')).toBeInTheDocument()
    const first = mock.requests.find((r) => r.path === '/api/v1/sales-order-changes/pending')
    expect(first).toBeDefined()
    await user.type(screen.getByRole('searchbox'), 'uno')
    await waitFor(() => expect(mock.requests.filter((r) => r.path === '/api/v1/sales-order-changes/pending').length).toBeGreaterThan(1))
  })

  it('sin pendientes lo dice', async () => {
    mock.routes['GET /api/v1/sales-order-changes/pending'] = () => ({ json: { items: [], total: 0, skip: 0, take: 25 } })
    wrap(<PendingApprovalsTab />)
    expect(await screen.findByText('No hay modificaciones esperando aprobación.')).toBeInTheDocument()
  })
})
