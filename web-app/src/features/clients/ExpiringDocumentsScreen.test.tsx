// Lote 248 — «Documentos por vencer»: filas con estado, buscador libre en el cliente, filtros de días/vencidos/faltantes que viajan al servidor y aviso de truncado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import ExpiringDocumentsScreen from './ExpiringDocumentsScreen'

const mock = vi.hoisted(() => ({ queries: [] as string[], truncated: false }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    if (url.pathname !== '/api/v1/client-documents/expiring') return new Response('{}', { status: 404 })
    mock.queries.push(url.search)
    return new Response(JSON.stringify({ warnDays: 30, withinDays: 30, truncated: mock.truncated, items: ITEMS }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
const ITEMS = [
  { rowKind: 'DOCUMENT', documentPublicId: 'd1', clientPublicId: 'c1', clientCode: 'C-1', clientName: 'Farmacia Uno', documentType: 'IVU_EXEMPTION_STATE', documentTypeLabel: 'Exención de IVU estatal', documentNumber: 'A-1', validTo: '2026-12-01', daysToExpiry: 5, expiryState: 'EXPIRING' },
  { rowKind: 'DOCUMENT', documentPublicId: 'd2', clientPublicId: 'c2', clientCode: 'C-2', clientName: 'Colmado Dos', documentType: 'LICENSE', documentTypeLabel: 'Licencia', documentNumber: 'L-2', validTo: '2026-11-01', daysToExpiry: -20, expiryState: 'EXPIRED' },
  { rowKind: 'EXEMPTION_WITHOUT_DOCUMENT', exemptionPublicId: 'e1', clientPublicId: 'c3', clientCode: 'C-3', clientName: 'Super Tres', documentTypeLabel: null, validTo: '2026-12-20', daysToExpiry: 24, expiryState: 'EXPIRING' },
]

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={new Set(['clients.read'])} modules={new Set(['CATALOG'])}>
        <MemoryRouter>
          <ExpiringDocumentsScreen />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

describe('Documentos por vencer', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.queries.length = 0
    mock.truncated = false
  })

  it('lista las filas con su estado y marca la exención sin documento', async () => {
    wrap()
    expect(await screen.findByText('Farmacia Uno')).toBeInTheDocument()
    expect(screen.getByText('Vencido')).toBeInTheDocument()
    expect(screen.getByText('Exención de IVU sin documento')).toBeInTheDocument()
    expect(screen.getByText('Falta el documento')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Farmacia Uno/ })).toHaveAttribute('href', '/catalog/clients?client=c1')
  })

  it('el buscador filtra lo cargado sin volver al servidor', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Farmacia Uno')
    const calls = mock.queries.length
    await user.type(screen.getByRole('searchbox'), 'colmado')
    await waitFor(() => expect(screen.queryByText('Farmacia Uno')).not.toBeInTheDocument())
    expect(screen.getByText('Colmado Dos')).toBeInTheDocument()
    expect(mock.queries.length).toBe(calls)
  })

  it('los filtros viajan al servidor y el truncado avisa', async () => {
    mock.truncated = true
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Farmacia Uno')
    expect(screen.getByRole('status')).toHaveTextContent('Hay más de 2000 documentos')
    await user.selectOptions(screen.getByLabelText('Vence dentro de'), '90')
    await user.click(screen.getByRole('switch', { name: 'Incluir vencidos' }))
    await waitFor(() => expect(mock.queries.at(-1)).toContain('withinDays=90'))
    expect(mock.queries.at(-1)).toContain('includeExpired=false')
  })
})
