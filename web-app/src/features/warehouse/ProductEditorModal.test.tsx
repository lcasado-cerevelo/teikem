// Modal único de producto (Fase 5, maqueta `renderProductModalHtml`): orden de campos, alta sin bloque de ajuste,
// edición con SKU bloqueado, interruptor "Producto activo" bloqueado con saldo, "Aplicar ajuste" con el producto fijo y
// acceso a Lotes/Series. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import type { ProductDetailDto } from './api'
import { ProductEditorModal } from './ProductEditorModal'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.text()
    mock.calls.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const body = route(req.method, url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const PID = 'aaaaaaaa-0000-0000-0000-000000000001'

const LOOKUPS: Record<string, { code: string; label: string; sortOrder: number }[]> = {
  TrackingType: [
    { code: 'NONE', label: 'Ninguno', sortOrder: 1 },
    { code: 'LOT', label: 'Lote', sortOrder: 2 },
    { code: 'SERIAL', label: 'Serie', sortOrder: 3 },
  ],
  UnitOfMeasure: [
    { code: 'UN', label: 'Unidad', sortOrder: 1 },
    { code: 'BOX', label: 'Caja', sortOrder: 2 },
  ],
  AdjustmentReason: [
    { code: 'FOUND', label: 'Encontrado', sortOrder: 1 },
    { code: 'COUNT_VARIANCE', label: 'Diferencia de conteo', sortOrder: 2 },
  ],
}

function route(method: string, url: URL): unknown {
  const p = url.pathname
  if (p.startsWith('/api/v1/catalogs/')) return LOOKUPS[p.slice('/api/v1/catalogs/'.length)] ?? []
  if (p === '/api/v1/product-categories') return [{ id: 3, name: 'Médico', path: 'Médico', isActive: true, productCount: 1 }]
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) return [{ id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true }]
  if (p === '/api/v1/inventory/adjustments' && method === 'POST') return { transactions: [], balances: [] }
  return new Response(JSON.stringify({ title: 'Sin acceso', code: 'forbidden' }), { status: 403 })
}

function detail(over: Partial<NonNullable<ProductDetailDto['product']>> = {}): ProductDetailDto {
  return {
    product: {
      id: 7,
      publicId: PID,
      sku: 'GLU-100',
      name: 'Medidor de glucosa',
      categoryId: 3,
      baseUomCode: 'UN',
      trackingTypeCode: 'LOT',
      qtyOnHand: 12,
      qtyReserved: 0,
      qtyAvailable: 12,
      minQty: 5,
      isActive: true,
      isOwn: true,
      ...over,
    },
    preferredWarehousePublicId: WH,
    preferredBinId: 10,
    hasMovements: true,
    rowVersion: 'AAAAAAAAB9E=',
  }
}

function wrap(ui: ReactNode, permissions: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['WMS_LOTSERIAL']}>
          {ui}
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** Etiquetas de los campos del formulario principal, en orden de aparición (sin "Más datos"). */
function mainLabels(dialog: HTMLElement): string[] {
  const fieldset = dialog.querySelector('fieldset.pe-fields')!
  return Array.from(fieldset.querySelectorAll(':scope > .r2 > .f > label, :scope > .f > label')).map((l) =>
    (l.textContent ?? '').replace('*', '').trim(),
  )
}

/** El diálogo ya con su formulario (mientras cargan los catálogos es otro diálogo, con el indicador de carga). */
function findDialog(name: string): Promise<HTMLElement> {
  return waitFor(() => {
    const dialog = screen.getByRole('dialog', { name })
    if (!dialog.querySelector('form')) throw new Error('cargando')
    return dialog
  })
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('ProductEditorModal', () => {
  it('alta: campos en el orden de la maqueta, Rastreo "Ninguno" y Unidad por defecto, sin interruptor ni ajuste', async () => {
    wrap(<ProductEditorModal open product={null} onClose={() => {}} />, ['inventory.view', 'inventory.manage', 'inventory.adjust'])
    const dialog = await findDialog('Nuevo producto')
    await within(dialog).findByLabelText(/^SKU/)
    expect(mainLabels(dialog)).toEqual([
      'SKU',
      'Unidad',
      'Nombre',
      'Categoría',
      'Rastreo',
      'Dueño del inventario',
      'Costo de compra',
      'Precio de venta',
      'Almacén por defecto',
      'Posición por defecto',
      'Total (se ajusta al editar)',
      'Punto de reorden',
    ])
    expect(within(dialog).getByLabelText('Rastreo')).toHaveValue('NONE')
    expect(within(dialog).getByLabelText('Unidad')).toHaveValue('UN')
    expect(within(dialog).queryByRole('switch')).toBeNull()
    expect(within(dialog).queryByRole('button', { name: /Aplicar ajuste/ })).toBeNull()
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Guardar' })).toBeInTheDocument()
  })

  it('alta: sin SKU no llama al API y muestra el mensaje del manual', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={null} onClose={() => {}} />, ['inventory.view', 'inventory.manage'])
    const dialog = await findDialog('Nuevo producto')
    await user.type(await within(dialog).findByLabelText(/^Nombre/), 'Producto')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El SKU es obligatorio.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('edición: SKU bloqueado, Total con el saldo y "usa Ajustar abajo", interruptor bloqueado con saldo y su nota', async () => {
    wrap(<ProductEditorModal open product={detail()} onClose={() => {}} />, ['inventory.view', 'inventory.manage'])
    const dialog = await findDialog('Editar producto')
    const sku = await within(dialog).findByLabelText('SKU')
    expect(sku).toHaveValue('GLU-100')
    expect(sku).toBeDisabled()
    expect(within(dialog).getByLabelText('Total (usa Ajustar abajo)')).toHaveValue('12')
    const active = within(dialog).getByRole('switch', { name: 'Producto activo (aparece al crear órdenes)' })
    expect(active).toBeChecked()
    expect(active).toBeDisabled()
    expect(within(dialog).getByText('No se puede desactivar mientras tenga inventario disponible.')).toBeInTheDocument()
    // sin inventory.adjust no hay bloque de ajuste
    expect(within(dialog).queryByRole('button', { name: /Aplicar ajuste/ })).toBeNull()
    // rastreo por lote: acceso a los lotes desde el modal, sin series
    expect(within(dialog).getByRole('link', { name: 'Ver lotes' })).toHaveAttribute('href', `/warehouse/products/${PID}?tab=lots`)
    expect(within(dialog).queryByRole('link', { name: 'Ver series' })).toBeNull()
  })

  it('edición sin saldo: el interruptor se puede apagar; serie: Ver lotes y Ver series', async () => {
    wrap(<ProductEditorModal open product={detail({ qtyOnHand: 0, trackingTypeCode: 'SERIAL' })} onClose={() => {}} />, [
      'inventory.view',
      'inventory.manage',
    ])
    const dialog = await findDialog('Editar producto')
    const active = await within(dialog).findByRole('switch')
    expect(active).toBeEnabled()
    expect(within(dialog).queryByText('No se puede desactivar mientras tenga inventario disponible.')).toBeNull()
    expect(within(dialog).getByRole('link', { name: 'Ver series' })).toHaveAttribute('href', `/warehouse/products/${PID}?tab=serials`)
  })

  it('Aplicar ajuste: POST /inventory/adjustments con el producto fijo y el almacén y la posición por defecto', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={detail({ trackingTypeCode: 'NONE' })} onClose={() => {}} />, [
      'inventory.view',
      'inventory.manage',
      'inventory.adjust',
    ])
    const dialog = await findDialog('Editar producto')
    const qty = await within(dialog).findByLabelText(/^Cantidad \(\+\/-\)/)
    await user.type(qty, '-2')
    const reason = within(dialog).getByLabelText(/^Motivo/)
    // los motivos reservados al sistema no se ofrecen
    await waitFor(() => expect(within(reason).getByRole('option', { name: 'Encontrado' })).toBeInTheDocument())
    expect(within(reason).queryByRole('option', { name: 'Diferencia de conteo' })).toBeNull()
    await user.selectOptions(reason, 'FOUND')
    await user.click(within(dialog).getByRole('button', { name: /Aplicar ajuste/ }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST')).toBe(true))
    const post = mock.calls.find((c) => c.method === 'POST')!
    expect(post.url.pathname).toBe('/api/v1/inventory/adjustments')
    expect(post.body).toEqual({ productPublicId: PID, warehousePublicId: WH, binId: 10, quantity: -2, reason: 'FOUND' })
    // el modal sigue abierto y la cantidad vuelve a vacío
    await waitFor(() => expect(qty).toHaveValue(null))
    expect(screen.getByRole('dialog', { name: 'Editar producto' })).toBeInTheDocument()
  })

  it('sin inventory.manage: solo lectura con "Cerrar"', async () => {
    wrap(<ProductEditorModal open product={detail()} onClose={() => {}} />, ['inventory.view'])
    const dialog = await findDialog('Ver datos del producto')
    expect(await within(dialog).findByLabelText(/^Nombre/)).toBeDisabled()
    expect(within(dialog).queryByRole('button', { name: 'Guardar' })).toBeNull()
    // la ✕ de la cabecera y el botón de texto del pie
    expect(within(dialog).getAllByRole('button', { name: 'Cerrar' })).toHaveLength(2)
  })
})
