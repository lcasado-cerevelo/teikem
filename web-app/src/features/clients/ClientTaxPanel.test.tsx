// Lote 248 — «Impuestos» de la ficha del cliente: exenciones con el chip «Falta el documento» y «Renovar» abre la captura con el alcance de la exención.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientTaxPanel } from './ClientTaxPanel'
import type { ClientDetail } from './clientRules'

const CLIENT = '11111111-0000-0000-0000-000000000001'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const p = new URL(req.url).pathname
    if (p === `/api/v1/clients/${CLIENT}/tax-exemptions`) return json(EXEMPTIONS)
    if (p === '/api/v1/tax/rates') return json([{ code: 'IVU', status: 'ACTIVE', components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] }])
    if (p === '/api/v1/tax/settings') return json({ defaultRateCode: 'IVU' })
    if (p === '/api/v1/catalogs/ClientDocumentType') return json([{ code: 'IVU_EXEMPTION_CITY', label: 'Exención de IVU municipal', sortOrder: 1, isEnabled: true, extraJson: '{"requiresValidTo":true,"isTax":true}' }])
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
const EXEMPTIONS = [
  { id: 1, publicId: 'e1', exemptionType: 'Exención municipal', certificateNumber: 'M-1', validFrom: '2026-01-01', validTo: '2026-12-31', status: 'VALID', scope: 'COMPONENTS', componentCodes: ['CITY'], missingDocument: false },
  { id: 2, publicId: 'e2', exemptionType: 'Exención total', certificateNumber: 'T-9', validFrom: '2020-01-01', validTo: null, status: 'VALID', scope: 'ALL', componentCodes: [], missingDocument: true },
]

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={new Set(['tax.manage', 'clients.read'])} modules={new Set(['TAX', 'CATALOG'])}>
        <MemoryRouter>
          <ClientTaxPanel client={{ publicId: CLIENT, name: 'Acme', isActive: true } as unknown as ClientDetail} />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

describe('Impuestos de la ficha del cliente', () => {
  beforeAll(() => setLang('es'))

  it('lista las exenciones y marca la que no tiene documento', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('2 exención(es) vigente(s)')).toBeInTheDocument()
    await user.click(screen.getAllByRole('button', { expanded: false })[0])
    expect((await screen.findAllByText('Falta el documento')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Exención municipal').length).toBeGreaterThan(0)
  })

  it('«Renovar» abre la captura con el alcance por componentes de la exención anterior', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click((await screen.findAllByRole('button', { expanded: false }))[0])
    await user.click((await screen.findAllByRole('button', { name: 'Renovar' }))[0])
    const dialog = await screen.findByRole('dialog', { name: 'Renovar exención de IVU' })
    expect(dialog).toBeInTheDocument()
    expect(await screen.findByRole('checkbox', { name: /Municipal/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /Estatal/ })).not.toBeChecked()
  })
})
