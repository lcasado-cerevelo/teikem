// Modal único de producto (Fase 5, maqueta `renderProductModalHtml`; Lote 12): orden de campos (con Marca y Modelo),
// desplegables con buscador, alta sin bloque de ajuste, edición con SKU bloqueado, interruptor "Producto activo" bloqueado
// con saldo, bloque de ajuste oculto tras "Añadir ajuste" (nota obligatoria, se oculta y refresca el Total al aplicar, el
// 409 del servidor queda dentro del bloque) y acceso a Lotes/Series. Sobre un fetch simulado.
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
const mock = vi.hoisted(() => ({ calls: [] as Call[], onHand: 12, adjustFails: false }))
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
    { code: 'DAMAGE', label: 'Daño', sortOrder: 3 },
  ],
}

const BINS = [{ id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'STORAGE', isActive: true }]

function route(method: string, url: URL): unknown {
  const p = url.pathname
  if (p.startsWith('/api/v1/catalogs/')) return LOOKUPS[p.slice('/api/v1/catalogs/'.length)] ?? []
  if (p === '/api/v1/product-categories') return [{ id: 3, name: 'Médico', path: 'Médico', isActive: true, productCount: 1 }]
  if (p === '/api/v1/warehouses') return [{ id: 1, publicId: WH, code: 'ALM-01', name: 'Almacén principal', isActive: true }]
  if (p === `/api/v1/warehouses/${WH}/bins`) {
    // listado paginado (Lote 1); el modal pide la posición por defecto por id (binIds)
    const ids = url.searchParams.getAll('binIds').map(Number)
    const items = BINS.filter((x) => ids.length === 0 || ids.includes(x.id))
    return { total: items.length, skip: 0, take: 100, items }
  }
  if (p === '/api/v1/products/brands') return ['Abbott', 'Roche']
  if (p === `/api/v1/products/${PID}` && method === 'GET') return { ...detail({ trackingTypeCode: 'NONE' }), product: { ...detail().product, trackingTypeCode: 'NONE', qtyOnHand: mock.onHand } }
  if (p === '/api/v1/products' && method === 'POST') return { product: { id: 99, publicId: PID } }
  if (p === '/api/v1/inventory/adjustments' && method === 'POST') {
    if (mock.adjustFails)
      return new Response(
        JSON.stringify({ title: 'Inventario insuficiente de GLU-100 en A-01: disponible 12, solicitado 20.', status: 409, code: 'insufficient_stock' }),
        { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
      )
    mock.onHand -= 2
    return { transactions: [], balances: [] }
  }
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
  mock.onHand = 12
  mock.adjustFails = false
})

/** Abre el bloque de ajuste, elige "Bajar" (Lote 14, D11) y llena Cantidad (positiva) y Motivo (Daño, con el buscador). */
async function fillAdjust(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, qty: string) {
  await user.click(await within(dialog).findByRole('button', { name: /Añadir ajuste/ }))
  await user.click(await within(dialog).findByRole('radio', { name: /Bajar/ }))
  await user.type(await within(dialog).findByLabelText(/^Cantidad/), qty)
  await user.type(within(dialog).getByRole('combobox', { name: /^Motivo/ }), 'dañ')
  await user.click(await within(dialog).findByRole('option', { name: 'Daño' }))
}

describe('ProductEditorModal', () => {
  it('alta: campos en el orden de la maqueta, Rastreo "Ninguno" y Unidad por defecto, sin interruptor ni ajuste', async () => {
    wrap(<ProductEditorModal open product={null} onClose={() => {}} />, ['inventory.view', 'inventory.manage', 'inventory.adjust'])
    const dialog = await findDialog('Nuevo producto')
    await within(dialog).findByLabelText(/^SKU/)
    expect(mainLabels(dialog)).toEqual([
      'SKU',
      'Unidad',
      'Nombre',
      'Marca',
      'Modelo',
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
    // desplegables con buscador: muestran la etiqueta del catálogo
    expect(within(dialog).getByRole('combobox', { name: 'Rastreo' })).toHaveValue('Ninguno')
    expect(within(dialog).getByRole('combobox', { name: 'Unidad' })).toHaveValue('Unidad')
    expect(within(dialog).getByRole('combobox', { name: 'Categoría' })).toHaveValue('')
    expect(within(dialog).queryByRole('switch')).toBeNull()
    expect(within(dialog).queryByRole('button', { name: /Aplicar ajuste/ })).toBeNull()
    expect(within(dialog).queryByRole('button', { name: /Añadir ajuste/ })).toBeNull()
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
    mock.onHand = 0
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

  it('ajuste oculto tras "Añadir ajuste"; Subir/Bajar con motivos según la dirección, sin los reservados; nota obligatoria sin llamar al API', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={detail({ trackingTypeCode: 'NONE' })} onClose={() => {}} />, [
      'inventory.view',
      'inventory.manage',
      'inventory.adjust',
    ])
    const dialog = await findDialog('Editar producto')
    expect(await within(dialog).findByRole('button', { name: /Añadir ajuste/ })).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/^Cantidad/)).toBeNull()
    expect(within(dialog).queryByRole('button', { name: /Aplicar ajuste/ })).toBeNull()

    await user.click(within(dialog).getByRole('button', { name: /Añadir ajuste/ }))
    // los motivos reservados al sistema no se ofrecen
    await user.type(within(dialog).getByRole('combobox', { name: /^Motivo/ }), 'diferencia')
    expect(await within(dialog).findByText('Sin coincidencias')).toBeInTheDocument()
    await user.clear(within(dialog).getByRole('combobox', { name: /^Motivo/ }))
    // al bajar no se ofrece Encontrado (D11)
    await user.click(within(dialog).getByRole('radio', { name: /Bajar/ }))
    await user.type(within(dialog).getByRole('combobox', { name: /^Motivo/ }), 'encon')
    expect(await within(dialog).findByText('Sin coincidencias')).toBeInTheDocument()
    await user.clear(within(dialog).getByRole('combobox', { name: /^Motivo/ }))
    await user.click(within(dialog).getByRole('radio', { name: /Subir/ }))
    await user.type(within(dialog).getByRole('combobox', { name: /^Motivo/ }), 'encon')
    await user.click(await within(dialog).findByRole('option', { name: 'Encontrado' }))
    await user.type(within(dialog).getByLabelText(/^Cantidad/), '2')
    await user.click(within(dialog).getByRole('button', { name: /Aplicar ajuste/ }))
    expect(await within(dialog).findByText('Escriba una nota que explique el ajuste.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'POST')).toBe(false)

    // Cancelar ajuste lo cierra; al volver a abrirlo está limpio
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar ajuste' }))
    await user.click(await within(dialog).findByRole('button', { name: /Añadir ajuste/ }))
    expect(within(dialog).getByLabelText(/^Cantidad/)).toHaveValue(null)
    expect(within(dialog).getByRole('radio', { name: /Subir/ })).toHaveAttribute('aria-checked', 'false')
  })

  it('Aplicar ajuste: POST con la nota, el producto fijo y la posición por defecto; refresca el Total y oculta el bloque', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={detail({ trackingTypeCode: 'NONE' })} onClose={() => {}} />, [
      'inventory.view',
      'inventory.manage',
      'inventory.adjust',
    ])
    const dialog = await findDialog('Editar producto')
    await fillAdjust(user, dialog, '2')
    await user.type(within(dialog).getByLabelText(/^Nota/), 'Caja dañada en muelle')
    await user.click(within(dialog).getByRole('button', { name: /Aplicar ajuste/ }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST')).toBe(true))
    const post = mock.calls.find((c) => c.method === 'POST')!
    expect(post.url.pathname).toBe('/api/v1/inventory/adjustments')
    expect(post.body).toEqual({ productPublicId: PID, warehousePublicId: WH, binId: 10, quantity: -2, reason: 'DAMAGE', notes: 'Caja dañada en muelle' })
    // el Total del modal pasa a lo que dice la ficha refrescada y el bloque vuelve a quedar oculto
    await waitFor(() => expect(within(dialog).getByLabelText('Total (usa Ajustar abajo)')).toHaveValue('10'))
    expect(await within(dialog).findByRole('button', { name: /Añadir ajuste/ })).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/^Nota/)).toBeNull()
    expect(screen.getByRole('dialog', { name: 'Editar producto' })).toBeInTheDocument()
  })

  it('409 insufficient_stock: el mensaje del servidor queda dentro del bloque y el bloque sigue abierto', async () => {
    const user = userEvent.setup()
    mock.adjustFails = true
    wrap(<ProductEditorModal open product={detail({ trackingTypeCode: 'NONE' })} onClose={() => {}} />, [
      'inventory.view',
      'inventory.manage',
      'inventory.adjust',
    ])
    const dialog = await findDialog('Editar producto')
    await fillAdjust(user, dialog, '20')
    await user.type(within(dialog).getByLabelText(/^Nota/), 'Merma')
    await user.click(within(dialog).getByRole('button', { name: /Aplicar ajuste/ }))
    const block = dialog.querySelector('form.pe-adjust') as HTMLElement
    expect(await within(block).findByRole('alert')).toHaveTextContent('Inventario insuficiente de GLU-100 en A-01: disponible 12, solicitado 20.')
    expect(within(dialog).getByLabelText(/^Nota/)).toHaveValue('Merma')
    expect(within(dialog).getByLabelText('Total (usa Ajustar abajo)')).toHaveValue('12')
  })

  it('alta con Marca y Modelo; edición: quitar la marca manda "" (PATCH) y el modelo sin cambio va null', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap(<ProductEditorModal open product={null} onClose={() => {}} />, ['inventory.view', 'inventory.manage'])
    let dialog = await findDialog('Nuevo producto')
    await user.type(await within(dialog).findByLabelText(/^SKU/), 'NEW-1')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Producto nuevo')
    // sugerencias de marcas existentes (datalist), pero se puede escribir una nueva
    const brand = within(dialog).getByLabelText('Marca')
    await waitFor(() => expect(document.getElementById(brand.getAttribute('list')!)?.querySelectorAll('option')).toHaveLength(2))
    await user.type(brand, 'Marca Nueva')
    await user.type(within(dialog).getByLabelText('Modelo'), 'X-200')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'POST')).toBe(true))
    expect(mock.calls.find((c) => c.method === 'POST')!.body).toMatchObject({ sku: 'NEW-1', brand: 'Marca Nueva', model: 'X-200' })
    unmount()

    mock.calls = []
    wrap(<ProductEditorModal open product={detail({ brand: 'Abbott', model: 'Lite' })} onClose={() => {}} />, ['inventory.view', 'inventory.manage'])
    dialog = await findDialog('Editar producto')
    expect(await within(dialog).findByLabelText('Marca')).toHaveValue('Abbott')
    await user.clear(within(dialog).getByLabelText('Marca'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(true))
    const patch = mock.calls.find((c) => c.method === 'PATCH')!.body as Record<string, unknown>
    expect(patch.brand).toBe('')
    expect(patch.model).toBeNull()
  })

  it('mínimo de picking con la posición por defecto fuera de una zona PICKING: aviso bajo Posición y sin guardar', async () => {
    const user = userEvent.setup()
    wrap(<ProductEditorModal open product={{ ...detail({ trackingTypeCode: 'NONE' }), minPickQty: 2 }} onClose={() => {}} />, ['inventory.view', 'inventory.manage'])
    const dialog = await findDialog('Editar producto')
    await within(dialog).findByLabelText(/^Nombre/)
    // la posición por defecto se pide por id (el listado paginado ya no trae todas)
    await waitFor(() =>
      expect(mock.calls.some((c) => c.url.pathname.endsWith('/bins') && c.url.searchParams.getAll('binIds').join() === '10')).toBe(true),
    )
    await user.type(within(dialog).getByLabelText(/^Nombre/), ' X')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El mínimo de picking requiere una posición preferida en una zona PICKING.')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'PATCH')).toBe(false)
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
