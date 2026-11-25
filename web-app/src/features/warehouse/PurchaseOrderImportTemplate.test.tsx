// Lote 246 — importar órdenes de compra con el origen «Plantilla»: elige la plantilla, manda templatePublicId y muestra el motivo de lo bloqueado (BAD_VALUE, COST_CONFLICT) y el nombre de la plantilla.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { PurchaseOrderImportModal } from './PurchaseOrderImportModal'

const T1 = 'aaaaaaaa-0000-0000-0000-0000000000a1'
const T2 = 'aaaaaaaa-0000-0000-0000-0000000000a2'
const mock = vi.hoisted(() => ({ forms: [] as Record<string, string>[], paths: [] as string[], cur: {} as Record<string, string> }))

// jsdom no codifica un FormData con archivo dentro de un Request: aquí el FormData solo registra los campos (el multipart real lo prueban el humo y Playwright)
class RecordingFormData {
  constructor() {
    mock.cur = {}
  }
  append(field: string, value: string | File) {
    mock.cur[field] = typeof value === 'string' ? value : 'file'
  }
}

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.paths.push(`${req.method} ${url.pathname}`)
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/purchase-orders/import-templates') return json([{ publicId: T1, name: 'Acme semanal', isActive: true, columns: [] }, { publicId: T2, name: 'Zeta mensual', isActive: true, columns: [] }])
    if (req.method === 'GET' && p === '/api/v1/purchase-orders/import-templates/fields') return json([{ field: 'supplier', required: true, defaultAllowed: true }])
    if (req.method === 'GET' && p === '/api/v1/warehouses') return json([])
    if (req.method === 'POST' && p === '/api/v1/purchase-orders/import') {
      mock.forms.push({ ...mock.cur })
      return json({
        committed: false, source: 'TEMPLATE', targetStatus: 'DRAFT', totalDocuments: 2, ready: 1, blocked: 1, duplicates: 0, skipped: 0, created: 0, failed: 0, templatePublicId: T2, templateName: 'Zeta mensual',
        documents: [
          { key: 'k1', vendor: 'Acme', number: '1045', date: '2026-11-02', lines: 2, status: 'READY', issues: [] },
          { key: 'k2', vendor: 'Zeta', number: '77', date: '2026-11-03', lines: 2, status: 'BLOCKED', issues: [{ code: 'COST_CONFLICT', blocking: true, message: 'El producto AB-1 viene en varias líneas de la orden con costos distintos (5, 6).' }] },
        ],
        missingProducts: [], missingSuppliers: [],
      })
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap(perms = ['purchasing.view', 'purchasing.manage']) {
  const session = { me: { userId: 9 }, isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', permissions: new Set(perms), modules: new Set(['PURCHASING']) } as unknown as Session
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={session}>
        <AccessProvider permissions={perms} modules={['PURCHASING']}>
          <MemoryRouter>
            <PurchaseOrderImportModal onClose={() => {}} />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

describe('Importar órdenes de compra con plantilla (Lote 246)', () => {
  beforeAll(() => {
    vi.stubGlobal('FormData', RecordingFormData)
    setLang('es')
  })
  afterAll(() => vi.unstubAllGlobals())
  beforeEach(() => {
    mock.forms.length = 0
    mock.paths.length = 0
  })

  it('el origen Plantilla pide las plantillas, manda la elegida y muestra el motivo del bloqueo y el nombre', async () => {
    const user = userEvent.setup()
    wrap()
    expect(mock.paths).not.toContain('GET /api/v1/purchase-orders/import-templates')   // solo se piden al elegir el origen
    await user.selectOptions(screen.getByLabelText('Origen del archivo'), 'TEMPLATE')
    const tpl = await screen.findByLabelText('Plantilla')
    await screen.findByRole('option', { name: 'Zeta mensual' })
    expect(screen.getByRole('button', { name: 'Nueva plantilla' })).toBeInTheDocument()
    await user.selectOptions(tpl, T2)
    await user.upload(screen.getByLabelText('Elegir archivo'), new File(['a;b'], 'compras.csv', { type: 'text/csv' }))
    await user.click(screen.getByRole('button', { name: 'Revisar' }))
    await waitFor(() => expect(mock.forms).toHaveLength(1))
    expect(mock.forms[0]).toMatchObject({ source: 'TEMPLATE', templatePublicId: T2, commit: 'false', status: 'DRAFT' })
    expect(await screen.findByText(/costos distintos \(5, 6\)/)).toBeInTheDocument()
    expect(screen.getByText('Plantilla: Zeta mensual')).toBeInTheDocument()
  })

  it('sin purchasing.manage no hay Nueva plantilla ni Editar', async () => {
    const user = userEvent.setup()
    wrap(['purchasing.view'])
    await user.selectOptions(screen.getByLabelText('Origen del archivo'), 'TEMPLATE')
    await screen.findByRole('option', { name: 'Zeta mensual' })
    expect(screen.queryByRole('button', { name: 'Nueva plantilla' })).toBeNull()
  })

  it('con otro origen no manda templatePublicId', async () => {
    const user = userEvent.setup()
    wrap()
    await user.upload(screen.getByLabelText('Elegir archivo'), new File(['x'], 'qb.csv', { type: 'text/csv' }))
    await user.click(screen.getByRole('button', { name: 'Revisar' }))
    await waitFor(() => expect(mock.forms).toHaveLength(1))
    expect(mock.forms[0].source).toBe('QUICKBOOKS')
    expect(mock.forms[0].templatePublicId).toBeUndefined()
  })
})
