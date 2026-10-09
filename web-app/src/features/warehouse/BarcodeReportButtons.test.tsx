// Lote F14 — botones "Códigos de barras": permiso (inventory.view), "Generando…" y deshabilitado mientras corre, toast si
// falla, menú de columnas (Automático / 2 columnas, 2026-10-05) y lo que reciben los reportes (lo filtrado, agrupado, con los
// "Filtros aplicados" de la barra de la pantalla). Sobre un fetch simulado; la descarga del PDF es un espía.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useRef, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { FilterScope, toast, useRegisterFilter } from '../../kernel/ui'
import { downloadBarcodeReportPdf, type BarcodeReportSpec } from '../../kernel/ui/barcodeReportPdf'
import { BinBarcodeReportButton, ProductBarcodeReportButton } from './BarcodeReportButtons'
import { EMPTY_PRODUCT_FILTERS } from './productFilters'

const mock = vi.hoisted(() => ({ requests: [] as URL[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    return new Response(JSON.stringify(route(url)), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
vi.mock('../../kernel/ui/barcodeReportPdf', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/barcodeReportPdf')>()
  return { ...actual, downloadBarcodeReportPdf: vi.fn(async () => {}) }
})

const WH = '11111111-1111-1111-1111-111111111111'

function route(url: URL): unknown {
  const p = url.pathname
  if (p === '/api/v1/products')
    return {
      total: 3,
      skip: 0,
      take: 200,
      items: [
        { id: 1, sku: 'SKU-10', name: 'Diez', categoryId: 3, categoryName: 'Médico', isActive: true },
        { id: 2, sku: 'SKU-2', name: 'Dos', categoryId: 3, categoryName: 'Médico', isActive: true },
        { id: 3, sku: 'X-1', name: 'Sin cat', isActive: false },
      ],
    }
  if (p === `/api/v1/warehouses/${WH}/bins`)
    return { total: 3, skip: 0, take: 200, items: [{ id: 1, zoneId: 7, zoneCode: 'RSV', code: 'A10-1' }, { id: 2, zoneId: 7, zoneCode: 'RSV', code: 'A2-1' }, { id: 3, zoneId: 7, zoneCode: 'RSV', code: 'GENERAL' }] }
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Principal', isActive: true }]
  if (p === '/api/v1/product-categories') return [{ id: 3, name: 'Médico', path: 'Salud / Médico', isActive: true }]
  return []
}

function wrap(node: ReactNode, permissions = ['inventory.view']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          {node}
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Un filtro de la barra de la pantalla (como TextFilter): se anota en el ámbito. */
function FakeFilter({ label, value }: { label: string; value: string | null }) {
  const ref = useRef<HTMLSpanElement>(null)
  useRegisterFilter(label, value, ref)
  return <span ref={ref} />
}

const lastSpec = () => vi.mocked(downloadBarcodeReportPdf).mock.calls.at(-1)?.[0] as BarcodeReportSpec

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  vi.mocked(downloadBarcodeReportPdf).mockReset()
  vi.mocked(downloadBarcodeReportPdf).mockImplementation(async () => {})
})

describe('ProductBarcodeReportButton', () => {
  it('sin inventory.view no se pinta', () => {
    wrap(<ProductBarcodeReportButton filters={EMPTY_PRODUCT_FILTERS} />, [])
    expect(screen.queryByRole('button', { name: 'Códigos de barras' })).toBeNull()
  })

  it('lee todo lo filtrado y arma el reporte corrido, sin agrupar, en el mismo orden en que el filtro devuelve los productos', async () => {
    const user = userEvent.setup()
    wrap(<ProductBarcodeReportButton filters={{ ...EMPTY_PRODUCT_FILTERS, name: 'sku' }} />)
    await user.click(screen.getByRole('button', { name: 'Códigos de barras' }))
    await user.click(await screen.findByRole('menuitem', { name: 'Automático' }))
    await waitFor(() => expect(downloadBarcodeReportPdf).toHaveBeenCalledTimes(1))
    const read = mock.requests.find((u) => u.pathname === '/api/v1/products')!
    expect(read.searchParams.get('name')).toBe('sku')
    expect(read.searchParams.get('take')).toBe('200')
    const spec = lastSpec()
    expect(spec.columns).toBe('auto')
    expect(spec.groups.map((g) => [g.title, g.rows.map((r) => r.value)])).toEqual([['', ['SKU-10', 'SKU-2', 'X-1']]])
    expect(spec.filters).toEqual([{ label: 'Nombre', value: 'contiene «sku»' }])
  })

  it('el botón es un menú: sin elegir no se genera nada; "2 columnas" genera el PDF a 2 columnas y no hay selector aparte', async () => {
    const user = userEvent.setup()
    wrap(<ProductBarcodeReportButton filters={EMPTY_PRODUCT_FILTERS} />)
    expect(screen.queryByRole('combobox')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Códigos de barras' }))
    expect(screen.getByRole('menu', { name: 'Columnas del reporte de códigos de barras' })).toBeInTheDocument()
    expect(downloadBarcodeReportPdf).not.toHaveBeenCalled()
    await user.click(screen.getByRole('menuitem', { name: '2 columnas' }))
    await waitFor(() => expect(downloadBarcodeReportPdf).toHaveBeenCalledTimes(1))
    expect(lastSpec().columns).toBe(2)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('"Generando…" y deshabilitado mientras corre; si falla, toast de error y el botón vuelve', async () => {
    const user = userEvent.setup()
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '')
    let fail: (e: Error) => void = () => {}
    vi.mocked(downloadBarcodeReportPdf).mockImplementation(() => new Promise<void>((_, reject) => (fail = reject)))
    wrap(<ProductBarcodeReportButton filters={EMPTY_PRODUCT_FILTERS} />)
    await user.click(screen.getByRole('button', { name: 'Códigos de barras' }))
    await user.click(await screen.findByRole('menuitem', { name: 'Automático' }))
    const busy = await screen.findByRole('button', { name: 'Generando…' })
    expect(busy).toBeDisabled()
    expect(busy).toHaveAttribute('aria-busy', 'true')
    fail(new Error('pdf'))
    await waitFor(() => expect(error).toHaveBeenCalledWith('No se pudo generar el reporte. Intente de nuevo.'))
    expect(await screen.findByRole('button', { name: 'Códigos de barras' })).toBeEnabled()
    error.mockRestore()
  })
})

describe('BinBarcodeReportButton', () => {
  const zones = [{ id: 7, code: 'RSV', name: 'Reserva' }]

  it('posiciones de lo filtrado agrupadas por el primer número (2 antes que 10, GENERAL al final) y con los filtros de la barra', async () => {
    const user = userEvent.setup()
    wrap(
      <FilterScope>
        <FakeFilter label="Posición" value='"A"' />
        <FakeFilter label="Estatus" value={null} />
        <BinBarcodeReportButton
          warehousePublicId={WH}
          warehouse={{ code: 'ALM-01', name: 'Principal' }}
          zones={zones}
          query={{ includeInactive: false, search: 'A' }}
          extraFilters={[{ label: 'Almacén', value: 'ALM-01 (Principal)' }]}
        />
      </FilterScope>,
    )
    await user.click(screen.getByRole('button', { name: 'Códigos de barras' }))
    await user.click(await screen.findByRole('menuitem', { name: 'Automático' }))
    await waitFor(() => expect(downloadBarcodeReportPdf).toHaveBeenCalledTimes(1))
    const read = mock.requests.find((u) => u.pathname.endsWith('/bins'))!
    expect(read.searchParams.get('search')).toBe('A')
    expect(read.searchParams.get('includeInactive')).toBe('false')
    const spec = lastSpec()
    expect(spec.title).toBe('Códigos de barras de posiciones')
    expect(spec.groups.map((g) => [g.title, g.rows.map((r) => r.value)])).toEqual([
      ['Grupo 2 (1)', ['A2-1']],
      ['Grupo 10 (1)', ['A10-1']],
      ['Otras posiciones (1)', ['GENERAL']],
    ])
    expect(spec.groups[0].rows[0].meta).toBe('Zona RSV (Reserva) · Almacén ALM-01')
    expect(spec.filters).toEqual([
      { label: 'Almacén', value: 'ALM-01 (Principal)' },
      { label: 'Posición', value: '"A"' },
    ])
  })

  it('combinación de filtros imposible (query null): no consulta y el reporte sale vacío', async () => {
    const user = userEvent.setup()
    wrap(<BinBarcodeReportButton warehousePublicId={WH} warehouse={{ code: 'ALM-01' }} zones={zones} query={null} />)
    await user.click(screen.getByRole('button', { name: 'Códigos de barras' }))
    await user.click(await screen.findByRole('menuitem', { name: 'Automático' }))
    await waitFor(() => expect(downloadBarcodeReportPdf).toHaveBeenCalledTimes(1))
    expect(mock.requests.some((u) => u.pathname.endsWith('/bins'))).toBe(false)
    expect(lastSpec().groups).toEqual([])
  })

  it('sin inventory.view no se pinta', () => {
    wrap(<BinBarcodeReportButton warehousePublicId={WH} warehouse={{ code: 'ALM-01' }} zones={zones} query={{}} />, ['warehouse.manage'])
    expect(screen.queryByRole('button', { name: 'Códigos de barras' })).toBeNull()
  })
})
