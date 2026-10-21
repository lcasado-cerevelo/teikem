// Pruebas de «Importar productos» sobre un fetch simulado: sin plantillas ofrece el formato estándar, «Revisar» manda commit=false y muestra por fila qué pasa,
// «Importar N» manda commit=true y el resumen dice cuántos se crearon.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { EntityImportModal } from './EntityImportModal'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; form: Record<string, string> }[], templates: [] as unknown[], uploads: [] as { field: string; value: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    // jsdom no sabe leer un multipart: el FormData está sustituido por uno que registra los campos (como en la prueba de importar envíos)
    const form: Record<string, string> = {}
    if (url.pathname === '/api/v1/products/import') for (const u of mock.uploads) form[u.field] = typeof u.value === 'string' ? u.value : 'file'
    mock.requests.push({ method: req.method, path: url.pathname, form })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/products/import-templates') return json(mock.templates)
    if (req.method === 'GET' && p === '/api/v1/products/import-templates/fields') return json([{ field: 'sku', required: true, defaultAllowed: false }, { field: 'name', required: false, defaultAllowed: true }])
    if (req.method === 'POST' && p === '/api/v1/products/import-templates') {
      const created = { publicId: 't-new', name: 'Formato estándar', kind: 'PRODUCT', columns: [], isActive: true }
      mock.templates = [created]
      return json(created)
    }
    if (req.method === 'POST' && p === '/api/v1/products/import') {
      const commit = form.commit === 'true'
      return json({
        committed: commit,
        total: 2,
        valid: 1,
        rejected: 1,
        created: commit ? 1 : 0,
        rows: [
          { row: 1, line: 2, key: 'A-1', label: 'Uno', status: commit ? 'CREATED' : 'OK', errors: {} },
          { row: 2, line: 3, key: 'A-2', label: 'Dos', status: 'ERROR', errors: { sku: 'Ya existe un producto con ese SKU para ese dueño.' } },
        ],
      })
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const SESSION = {
  me: { tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['inventory.manage']),
  modules: new Set(['WMS_LOT_SERIAL']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={['inventory.manage']} modules={['WMS_LOT_SERIAL']}>
          <EntityImportModal entity="PRODUCT" onClose={() => {}} />
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

class RecordingFormData {
  append(field: string, value: unknown) {
    mock.uploads.push({ field, value })
  }
}

describe('EntityImportModal', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    vi.stubGlobal('FormData', RecordingFormData)
    mock.requests.length = 0
    mock.uploads.length = 0
    mock.templates = [{ publicId: 't-1', name: 'Mi plantilla', kind: 'PRODUCT', columns: [], isActive: true }]
  })

  it('sin plantillas ofrece crear el formato estándar y lo deja elegido', async () => {
    mock.templates = []
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Crear formato estándar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/products/import-templates')).toBe(true))
    expect(await screen.findByRole('option', { name: 'Formato estándar' })).toBeInTheDocument()
  })

  it('«Revisar» no guarda (commit=false) y muestra por fila el estado y el motivo; «Importar» manda commit=true', async () => {
    const user = userEvent.setup()
    wrap()
    const input = (await screen.findByLabelText(/Archivo/)) as HTMLInputElement
    await waitFor(() => expect(input).toBeEnabled())
    await user.upload(input, new File(['sku,name\nA-1,Uno\nA-2,Dos'], 'p.csv', { type: 'text/csv' }))
    await user.click(screen.getByRole('button', { name: 'Revisar' }))

    expect(await screen.findByTestId('pi-summary')).toHaveTextContent('2 filas · 1 válidas · 1 con errores')
    const bad = screen.getByText('A-2').closest('tr') as HTMLElement
    expect(within(bad).getByText('Rechazada')).toBeInTheDocument()
    expect(within(bad).getByText(/Ya existe un producto con ese SKU/)).toBeInTheDocument()
    const review = mock.requests.find((r) => r.path === '/api/v1/products/import')!
    expect(review.form).toMatchObject({ templatePublicId: 't-1', commit: 'false', file: 'file' })

    await user.click(screen.getByRole('button', { name: 'Importar 1' }))
    expect(await screen.findByText('Se crearon 1 registros · 1 rechazados')).toBeInTheDocument()
    expect(mock.requests.filter((r) => r.path === '/api/v1/products/import').at(-1)!.form.commit).toBe('true')
  })

  it('«Importar» no se puede sin revisar antes', async () => {
    wrap()
    const confirm = await screen.findByRole('button', { name: 'Importar 0' })
    expect(confirm).toBeDisabled()
  })
})

afterAll(() => vi.unstubAllGlobals())
