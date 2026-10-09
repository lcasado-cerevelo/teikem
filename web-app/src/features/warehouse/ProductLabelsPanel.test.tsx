// Etiquetas de producto (2026-10-09): el botón abre la ventana con los tres tamaños y la orientación, y «Generar PDF» baja las etiquetas del filtro.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { downloadBinLabelsPdf } from '../../kernel/ui/binLabelPdf'
import { ProductLabelsButton } from './ProductLabelsPanel'
import { EMPTY_PRODUCT_FILTERS } from './productFilters'

const mock = vi.hoisted(() => ({ calls: [] as URL[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.calls.push(url)
    const items = [
      { id: 1, sku: 'SKU-2', name: 'Tuerca', categoryName: 'Ferretería', isActive: true },
      { id: 2, sku: 'SKU-1', name: 'Tornillo', categoryName: null, isActive: true },
    ]
    return new Response(JSON.stringify({ total: items.length, skip: 0, take: 200, items }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
vi.mock('../../kernel/ui/binLabelPdf', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/binLabelPdf')>()
  return { ...actual, downloadBinLabelsPdf: vi.fn(async (spec: Parameters<typeof actual.downloadBinLabelsPdf>[0]) => actual.planBinLabels(spec.bins, spec.size, spec.orientation)) }
})

const FILTERS = EMPTY_PRODUCT_FILTERS

function show(permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          <ProductLabelsButton filters={FILTERS} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  vi.mocked(downloadBinLabelsPdf).mockClear()
  try {
    localStorage.clear()
  } catch {
    // sin almacenamiento
  }
})

describe('Etiquetas de producto', () => {
  it('sin inventory.view no hay botón', () => {
    show(['warehouse.receive'])
    expect(screen.queryByRole('button', { name: 'Etiquetas de producto' })).toBeNull()
  })

  it('el botón abre la ventana con los tres tamaños (4 × 2 por defecto) y la orientación', async () => {
    const user = userEvent.setup()
    show(['inventory.view'])
    await user.click(screen.getByRole('button', { name: 'Etiquetas de producto' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('4 × 2 pulgadas (10 × 5 cm)')
    expect(dialog).toHaveTextContent('4 × 4 pulgadas (10 × 10 cm)')
    expect(dialog).toHaveTextContent('4 × 6 pulgadas (10 × 15 cm)')
    expect(screen.getByRole('radio', { name: /4 × 2 pulgadas/ })).toBeChecked()
    expect(screen.getByRole('radio', { name: /Automática/ })).toBeChecked()
  })

  it('Generar PDF baja una etiqueta por producto en el tamaño elegido y recuerda la elección', async () => {
    const user = userEvent.setup()
    show(['inventory.view'])
    await user.click(screen.getByRole('button', { name: 'Etiquetas de producto' }))
    await user.click(await screen.findByRole('radio', { name: /4 × 6 pulgadas/ }))
    await user.click(screen.getByRole('button', { name: 'Generar PDF' }))
    await waitFor(() => expect(downloadBinLabelsPdf).toHaveBeenCalledTimes(1))
    const spec = vi.mocked(downloadBinLabelsPdf).mock.calls[0][0]
    expect(spec.size).toBe('4x6')
    expect(spec.bins.map((b) => b.code)).toEqual(['SKU-1', 'SKU-2'])
    expect(localStorage.getItem('teikem.productLabels.size')).toBe('4x6')
  })
})
