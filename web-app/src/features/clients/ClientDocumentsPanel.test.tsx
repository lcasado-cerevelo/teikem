// Lote 248 — documentos del cliente con vigencia: columnas nuevas, «hasta» obligatorio según el tipo, subir con número y fechas, la pregunta «¿Aplicar la exención de IVU?»
// y el 409 al quitar un documento que respalda una exención (el mensaje del servidor se ve en el diálogo).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientDocumentsPanel } from './ClientDocumentsPanel'
import type { ClientDetail } from './clientRules'

const CLIENT = '11111111-0000-0000-0000-000000000001'
const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[], removeStatus: 200, fields: [] as [string, string][] }))

// jsdom + el Request de Node no se entienden con un FormData con archivos: aquí el FormData solo registra los campos (la codificación real la prueban el humo y Playwright)
class RecordingFormData {
  append(field: string, value: string | File) {
    mock.fields.push([field, typeof value === 'string' ? value : value.name])
  }
}

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push({ method: req.method, path: url.pathname, body: undefined })
    const p = url.pathname
    if (req.method === 'GET' && p === `/api/v1/clients/${CLIENT}/documents`) return json(DOCS)
    if (req.method === 'GET' && p === '/api/v1/catalogs/ClientDocumentType') return json(TYPES)
    if (req.method === 'GET' && p === '/api/v1/tax/rates') return json([])
    if (req.method === 'GET' && p === '/api/v1/tax/settings') return json({})
    if (req.method === 'POST' && p === `/api/v1/clients/${CLIENT}/documents`) return json({ publicId: 'd9', documentType: 'IVU_EXEMPTION_STATE', documentTypeLabel: 'Exención de IVU estatal', isTax: true, suggestExemption: true })
    if (req.method === 'DELETE') return mock.removeStatus === 409 ? json({ title: 'Este documento respalda una exención de IVU vigente: dé de baja la exención en Impuestos antes de quitarlo.', status: 409 }, 409) : new Response(null, { status: 204 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })
}
const TYPES = [
  { code: 'OTHER', label: 'Otro', sortOrder: 90, isEnabled: true, extraJson: null },
  { code: 'IVU_EXEMPTION_STATE', label: 'Exención de IVU estatal', sortOrder: 10, isEnabled: true, extraJson: '{"requiresValidTo":true,"isTax":true}' },
]
const DOCS = [
  { publicId: 'd1', documentType: 'IVU_EXEMPTION_STATE', documentTypeLabel: 'Exención de IVU estatal', filePublicId: 'f1', fileName: 'cert.pdf', createdAtUtc: '2026-10-01T10:00:00', documentNumber: 'A-77', validFrom: '2026-01-01', validTo: '2026-12-31', daysToExpiry: 12, expiryState: 'EXPIRING', isTax: true, requiresValidTo: true, backsExemption: true, rowVersion: 'rv' },
  { publicId: 'd2', documentType: 'OTHER', documentTypeLabel: 'Otro', filePublicId: 'f2', fileName: 'nota.pdf', createdAtUtc: '2026-10-02T10:00:00', expiryState: 'NO_EXPIRY' },
]

function wrap(permissions = ['clients.read', 'clients.update', 'tax.manage'], modules = ['CATALOG', 'TAX']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={new Set(permissions)} modules={new Set(modules)}>
        <MemoryRouter>
          <ClientDocumentsPanel client={{ publicId: CLIENT, name: 'Acme', isActive: true } as unknown as ClientDetail} />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

async function open(user: ReturnType<typeof userEvent.setup>) {
  await user.click((await screen.findAllByRole('button', { expanded: false }))[0])
}

describe('Documentos del cliente con vigencia', () => {
  beforeAll(() => {
    vi.stubGlobal('FormData', RecordingFormData)
    setLang('es')
  })
  afterAll(() => vi.unstubAllGlobals())
  beforeEach(() => {
    mock.fields.length = 0
    mock.requests.length = 0
    mock.removeStatus = 200
  })

  it('muestra número, fechas, estado de vigencia y la marca «Respalda exención»', async () => {
    const user = userEvent.setup()
    wrap()
    await open(user)
    expect((await screen.findAllByText('A-77')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Por vencer').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Respalda exención').length).toBeGreaterThan(0)
    expect(screen.getAllByText('en 12 días').length).toBeGreaterThan(0)
  })

  it('un tipo con vigencia exige «hasta» antes de subir y no llama al servidor', async () => {
    const user = userEvent.setup()
    wrap()
    await open(user)
    await screen.findAllByText('A-77')
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'IVU_EXEMPTION_STATE')
    await user.upload(screen.getByLabelText('Archivo'), new File(['x'], 'c.pdf', { type: 'application/pdf' }))
    await user.click(screen.getByRole('button', { name: 'Subir documento' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Este tipo de documento lleva vigencia: escriba la fecha «hasta».')
    expect(mock.requests.filter((r) => r.method === 'POST')).toHaveLength(0)
  })

  it('al subir un certificado de impuestos pregunta si aplica la exención y abre la captura con ese documento', async () => {
    const user = userEvent.setup()
    wrap()
    await open(user)
    await screen.findAllByText('A-77')
    await user.selectOptions(screen.getByLabelText('Tipo de documento'), 'IVU_EXEMPTION_STATE')
    await user.upload(screen.getByLabelText('Archivo'), new File(['x'], 'c.pdf', { type: 'application/pdf' }))
    await user.type(screen.getByLabelText('Número'), 'B-1')
    await user.type(screen.getByLabelText('Hasta (obligatorio)'), '12/31/2027')
    await user.click(screen.getByRole('button', { name: 'Subir documento' }))
    expect(await screen.findByText('¿Aplicar la exención de IVU?')).toBeInTheDocument()
    expect(mock.fields).toEqual(expect.arrayContaining([['documentType', 'IVU_EXEMPTION_STATE'], ['documentNumber', 'B-1'], ['validTo', '2027-12-31']]))
    await user.click(screen.getByRole('button', { name: 'Sí, aplicar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Aplicar la exención de IVU' })
    expect(within(dialog).getByRole('group', { name: 'Documento que respalda la exención' })).toBeInTheDocument()
  })

  it('sin tax.manage no pregunta por la exención', async () => {
    const user = userEvent.setup()
    wrap(['clients.read', 'clients.update'])
    await open(user)
    await screen.findAllByText('A-77')
    expect(screen.queryByRole('button', { name: 'Aplicar exención de IVU' })).not.toBeInTheDocument()
  })

  it('quitar un documento que respalda una exención muestra el 409 en el diálogo', async () => {
    mock.removeStatus = 409
    const user = userEvent.setup()
    wrap()
    await open(user)
    await screen.findAllByText('A-77')
    await user.click(screen.getAllByRole('button', { name: 'Quitar' })[0])
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(screen.getByText(/respalda una exención de IVU vigente/)).toBeInTheDocument())
  })
})
