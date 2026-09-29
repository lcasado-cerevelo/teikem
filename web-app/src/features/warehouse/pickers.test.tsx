import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { Field, Form } from '../../kernel/ui'
import {
  BinPicker,
  BinPickerInput,
  ProductMultiFilter,
  ProductPicker,
  WarehousePicker,
  WarehousePickerInput,
  type BinPickerProps,
  type ProductFilterItem,
} from './pickers'

// El cliente de la app se sustituye por uno con la misma política sobre un fetch simulado.
const mock = vi.hoisted(() => ({ requests: [] as URL[], handler: (_url: URL): unknown => [] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const body = mock.handler(url)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WAREHOUSES = [
  { id: 1, publicId: '11111111-1111-1111-1111-111111111111', code: 'ALM-01', name: 'Almacén principal', isActive: true },
  { id: 2, publicId: '22222222-2222-2222-2222-222222222222', code: 'ALM-02', name: 'Almacén norte', isActive: true },
]
const OLD_WAREHOUSE = { id: 3, publicId: '33333333-3333-3333-3333-333333333333', code: 'ALM-99', name: 'Almacén cerrado', isActive: false }

const PRODUCTS = [
  { id: 1, publicId: 'aaaaaaaa-0000-0000-0000-000000000001', sku: 'TORN-01', name: 'Tornillo', isOwn: true, isActive: true, trackingTypeCode: 'NONE' },
  { id: 2, publicId: 'aaaaaaaa-0000-0000-0000-000000000002', sku: 'TUER-01', name: 'Tuerca', isOwn: false, ownerName: 'Acme', isActive: true, trackingTypeCode: 'LOT' },
  { id: 3, publicId: 'aaaaaaaa-0000-0000-0000-000000000003', sku: 'CLAV-01', name: 'Clavo', isOwn: true, isActive: true, trackingTypeCode: 'SERIAL' },
]

function route(url: URL): unknown {
  if (url.pathname === '/api/v1/warehouses') return WAREHOUSES
  if (url.pathname === `/api/v1/warehouses/${OLD_WAREHOUSE.publicId}`) return { warehouse: OLD_WAREHOUSE, zones: [], docks: [] }
  if (url.pathname === '/api/v1/products') {
    const search = (url.searchParams.get('search') ?? '').toLowerCase()
    const items = PRODUCTS.filter((p) => `${p.sku} ${p.name}`.toLowerCase().includes(search))
    return { total: items.length, skip: 0, take: 50, items }
  }
  const detail = PRODUCTS.find((p) => url.pathname === `/api/v1/products/${p.publicId}`)
  if (detail) return { product: detail, hasMovements: false }
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.handler = route
})

/** WarehousePicker controlado (el valor elegido vuelve como `value`, como en una pantalla real). */
function WarehouseHarness({ onChange, placeholder }: { onChange: (v: string | null) => void; placeholder?: string | null }) {
  const [value, setValue] = useState<string | null>(null)
  return (
    <WarehousePicker
      value={value}
      placeholder={placeholder}
      aria-label="Almacén"
      onChange={(v) => {
        setValue(v)
        onChange(v)
      }}
    />
  )
}

describe('WarehousePicker', () => {
  it('lista almacenes activos (includeInactive=false) como "Code · Name", devuelve el publicId y se puede quitar', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<WarehouseHarness onChange={onChange} />)
    const box = screen.getByRole('combobox', { name: 'Almacén' })
    await waitFor(() => expect(box).toHaveAttribute('placeholder', 'Seleccione un almacén…'))
    expect(mock.requests.find((u) => u.pathname === '/api/v1/warehouses')?.searchParams.get('includeInactive')).toBe('false')
    await user.click(box)
    expect(screen.getAllByRole('option')).toHaveLength(2)
    await user.click(screen.getByRole('option', { name: 'ALM-02 · Almacén norte' }))
    expect(onChange).toHaveBeenCalledWith(WAREHOUSES[1].publicId)
    expect(box).toHaveValue('ALM-02 · Almacén norte')
    await user.click(screen.getByRole('button', { name: 'Quitar almacén' }))
    expect(onChange).toHaveBeenLastCalledWith(null)
  })

  it('filtra en el cliente por código o nombre (sin mayúsculas ni acentos) sin volver a consultar el API', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseHarness onChange={vi.fn()} />)
    const box = screen.getByRole('combobox', { name: 'Almacén' })
    await waitFor(() => expect(box).toHaveAttribute('placeholder', 'Seleccione un almacén…'))
    const before = mock.requests.length
    await user.type(box, 'NORTE')
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['ALM-02 · Almacén norte'])
    await user.clear(box)
    await user.type(box, 'almacen')
    expect(screen.getAllByRole('option')).toHaveLength(2)
    await user.type(box, ' zzz')
    expect(screen.queryByRole('option')).toBeNull()
    expect(screen.getByText('Ningún almacén coincide.')).toBeInTheDocument()
    expect(mock.requests.length).toBe(before)
  })

  it('lector de código de barras: código exacto + Enter elige ese almacén (aunque otro esté resaltado)', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    const LONGER = { id: 4, publicId: '44444444-4444-4444-4444-444444444444', code: 'ALM-010', name: 'Anexo', isActive: true }
    mock.handler = (url) => (url.pathname === '/api/v1/warehouses' ? [LONGER, ...WAREHOUSES] : route(url))
    wrap(<WarehouseHarness onChange={onChange} />)
    const box = screen.getByRole('combobox', { name: 'Almacén' })
    await waitFor(() => expect(box).toHaveAttribute('placeholder', 'Seleccione un almacén…'))
    // "alm-01" coincide con ALM-01 y ALM-010: la exacta va primero; aunque la flecha resalte ALM-010, Enter elige ALM-01
    await user.type(box, 'alm-01')
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['ALM-01 · Almacén principal', 'ALM-010 · Anexo'])
    await user.keyboard('{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenLastCalledWith(WAREHOUSES[0].publicId)
    expect(box).toHaveValue('ALM-01 · Almacén principal')
    expect(box).toHaveAttribute('aria-expanded', 'false')
  })

  it('teclado: ↓ y Enter eligen la opción resaltada; Escape cierra', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<WarehouseHarness onChange={onChange} />)
    const box = screen.getByRole('combobox', { name: 'Almacén' })
    await waitFor(() => expect(box).toHaveAttribute('placeholder', 'Seleccione un almacén…'))
    await user.click(box)
    await user.keyboard('{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenLastCalledWith(WAREHOUSES[1].publicId)
    await user.click(box)
    expect(box).toHaveAttribute('aria-expanded', 'true')
    await user.keyboard('{Escape}')
    expect(box).toHaveAttribute('aria-expanded', 'false')
  })

  it('placeholder null: sin botón para quitar el valor', async () => {
    const user = userEvent.setup()
    wrap(<WarehouseHarness onChange={vi.fn()} placeholder={null} />)
    const box = screen.getByRole('combobox', { name: 'Almacén' })
    await user.click(box)
    await user.click(await screen.findByRole('option', { name: 'ALM-01 · Almacén principal' }))
    expect(screen.queryByRole('button', { name: 'Quitar almacén' })).toBeNull()
  })

  it('un valor que ya no está activo se conserva con su etiqueta (ficha del almacén)', async () => {
    wrap(<WarehousePicker value={OLD_WAREHOUSE.publicId} onChange={vi.fn()} aria-label="Almacén" />)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Almacén' })).toHaveValue('ALM-99 · Almacén cerrado'))
    expect(mock.requests.some((u) => u.pathname === `/api/v1/warehouses/${OLD_WAREHOUSE.publicId}`)).toBe(true)
  })

  it('sin acceso al módulo: aviso en el control y en la lista, sin sacar de la pantalla', async () => {
    const user = userEvent.setup()
    mock.handler = () =>
      new Response(JSON.stringify({ status: 403, code: 'module_disabled', title: 'Módulo apagado' }), {
        status: 403,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    wrap(<WarehousePicker value={null} onChange={vi.fn()} aria-label="Almacén" />)
    const box = screen.getByRole('combobox', { name: 'Almacén' })
    await waitFor(() => expect(box).toHaveAttribute('placeholder', 'Su usuario no puede consultar almacenes.'))
    await user.click(box)
    expect(within(screen.getByRole('listbox')).getByText('Su usuario no puede consultar almacenes.')).toBeInTheDocument()
  })

  it('WarehousePickerInput: el valor del formulario es el publicId', async () => {
    const user = userEvent.setup()
    let submitted: unknown = undefined
    function FormHarness() {
      const form = useForm<{ warehousePublicId: string | null }>({ defaultValues: { warehousePublicId: null } })
      return (
        <Form form={form} onSubmit={async (v) => void (submitted = v)} id="f">
          <Field name="warehousePublicId" label="Almacén">
            <WarehousePickerInput />
          </Field>
          <button type="submit">Enviar</button>
        </Form>
      )
    }
    wrap(<FormHarness />)
    const box = screen.getByRole('combobox', { name: 'Almacén' })
    await waitFor(() => expect(box).toHaveAttribute('placeholder', 'Seleccione un almacén…'))
    await user.type(box, 'ALM-02{Enter}')
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    await waitFor(() => expect(submitted).toEqual({ warehousePublicId: WAREHOUSES[1].publicId }))
  })
})

// ---------------------------------------------------------------------------------------------------------------------
// BinPicker
// ---------------------------------------------------------------------------------------------------------------------
const WH = WAREHOUSES[0].publicId
const BINS = [
  { id: 10, zoneId: 1, zoneCode: 'STG', zoneTypeCode: 'STAGING', code: 'REC-01', isActive: true },
  { id: 11, zoneId: 2, zoneCode: 'ALM', zoneTypeCode: 'STORAGE', code: 'A-01-01', isActive: true },
  { id: 12, zoneId: 2, zoneCode: 'ALM', zoneTypeCode: 'STORAGE', code: 'A-01-02', isActive: true },
  { id: 13, zoneId: 2, zoneCode: 'ALM', zoneTypeCode: 'STORAGE', code: 'A-01-020', isActive: true },
]
const OLD_BIN = { id: 99, zoneId: 2, zoneCode: 'ALM', zoneTypeCode: 'STORAGE', code: 'Z-99', isActive: false }

function binRoute(url: URL): unknown {
  if (url.pathname === `/api/v1/warehouses/${WH}/bins`) {
    const search = (url.searchParams.get('search') ?? '').toLowerCase()
    const all = url.searchParams.get('includeInactive') === 'true' ? [...BINS, OLD_BIN] : BINS
    return all.filter((b) => `${b.code} ${b.zoneCode}`.toLowerCase().includes(search))
  }
  return route(url)
}

function BinHarness(props: { onChange: (v: number | null) => void; warehousePublicId?: string | null; initial?: number | null } & Partial<BinPickerProps>) {
  const { onChange, warehousePublicId = WH, initial = null, ...rest } = props
  const [value, setValue] = useState<number | null>(initial)
  return (
    <BinPicker
      {...rest}
      warehousePublicId={warehousePublicId}
      value={value}
      aria-label="Posición"
      onChange={(v) => {
        setValue(v)
        onChange(v)
      }}
    />
  )
}

const binRequests = () => mock.requests.filter((u) => u.pathname === `/api/v1/warehouses/${WH}/bins`)

describe('BinPicker', () => {
  beforeEach(() => {
    mock.handler = binRoute
  })

  it('sin almacén: deshabilitado con "Elija primero un almacén" y sin consultar', () => {
    wrap(<BinHarness onChange={vi.fn()} warehousePublicId={null} />)
    const box = screen.getByRole('combobox', { name: 'Posición' })
    expect(box).toBeDisabled()
    expect(box).toHaveAttribute('placeholder', 'Elija primero un almacén')
    expect(mock.requests).toHaveLength(0)
  })

  it('busca en el API con search= e includeInactive=false, muestra "Código · Zona" y devuelve el id', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<BinHarness onChange={onChange} />)
    const box = screen.getByRole('combobox', { name: 'Posición' })
    await user.type(box, 'a-01')
    await waitFor(() => expect(binRequests().some((u) => u.searchParams.get('search') === 'a-01')).toBe(true))
    expect(binRequests().every((u) => u.searchParams.get('includeInactive') === 'false')).toBe(true)
    await waitFor(() => expect(screen.queryByRole('option', { name: /REC-01/ })).toBeNull())
    await user.click(screen.getByRole('option', { name: 'A-01-02 · ALM' }))
    expect(onChange).toHaveBeenLastCalledWith(12)
    expect(box).toHaveValue('A-01-02 · ALM')
    await user.click(screen.getByRole('button', { name: 'Quitar posición' }))
    expect(onChange).toHaveBeenLastCalledWith(null)
  })

  it('lector de código de barras: código completo + Enter inmediato (antes de la pausa) elige la posición exacta', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<BinHarness onChange={onChange} />)
    const box = screen.getByRole('combobox', { name: 'Posición' })
    // "A-01-02" también es subcadena de "A-01-020": gana la coincidencia exacta
    await user.type(box, 'a-01-02{Enter}')
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(12))
    expect(box).toHaveValue('A-01-02 · ALM')
    expect(box).toHaveAttribute('aria-expanded', 'false')
  })

  it('zoneTypeCodes filtra en el cliente (p. ej. solo staging) y onlyWithStock se envía al API', async () => {
    const user = userEvent.setup()
    wrap(<BinHarness onChange={vi.fn()} zoneTypeCodes={['STAGING']} onlyWithStock />)
    await user.click(screen.getByRole('combobox', { name: 'Posición' }))
    await screen.findByRole('option', { name: 'REC-01 · STG' })
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(binRequests().at(-1)?.searchParams.get('onlyWithStock')).toBe('true')
  })

  it('las sugeridas van primero con la marca "Sugerida"', async () => {
    const user = userEvent.setup()
    wrap(<BinHarness onChange={vi.fn()} suggestedBinIds={[12, null]} />)
    await user.click(screen.getByRole('combobox', { name: 'Posición' }))
    await screen.findByRole('option', { name: /A-01-01/ })
    const options = screen.getAllByRole('option')
    expect(options[0]).toHaveTextContent('A-01-02 · ALM')
    expect(options[0]).toHaveTextContent('Sugerida')
    expect(options[1]).not.toHaveTextContent('Sugerida')
  })

  it('un valor inactivo (registro histórico) se muestra con su código buscando con includeInactive=true', async () => {
    wrap(<BinHarness onChange={vi.fn()} initial={OLD_BIN.id} />)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Posición' })).toHaveValue('Z-99 · ALM'))
    expect(binRequests().some((u) => u.searchParams.get('includeInactive') === 'true')).toBe(true)
  })

  it('al cambiar de almacén quita la posición elegida', async () => {
    const onChange = vi.fn()
    function Switcher() {
      const [wh, setWh] = useState<string | null>(WH)
      return (
        <>
          <BinHarness onChange={onChange} warehousePublicId={wh} initial={11} />
          <button type="button" onClick={() => setWh(WAREHOUSES[1].publicId)}>
            Cambiar
          </button>
        </>
      )
    }
    const user = userEvent.setup()
    wrap(<Switcher />)
    await user.click(screen.getByRole('button', { name: 'Cambiar' }))
    expect(onChange).toHaveBeenLastCalledWith(null)
    expect(screen.getByRole('combobox', { name: 'Posición' })).toHaveValue('')
  })

  it('sin acceso: aviso en la lista, sin sacar de la pantalla', async () => {
    const user = userEvent.setup()
    mock.handler = () =>
      new Response(JSON.stringify({ status: 403, code: 'forbidden', title: 'Prohibido' }), { status: 403, headers: { 'Content-Type': 'application/problem+json' } })
    wrap(<BinHarness onChange={vi.fn()} />)
    await user.click(screen.getByRole('combobox', { name: 'Posición' }))
    expect(await screen.findByText('Su usuario no puede consultar posiciones.')).toBeInTheDocument()
  })

  it("BinPickerInput: el valor del formulario es el id como texto ('' = ninguna)", async () => {
    const user = userEvent.setup()
    let submitted: unknown = undefined
    function FormHarness() {
      const form = useForm<{ binId: string }>({ defaultValues: { binId: '' } })
      return (
        <Form form={form} onSubmit={async (v) => void (submitted = v)} id="f">
          <Field name="binId" label="Posición">
            <BinPickerInput warehousePublicId={WH} />
          </Field>
          <button type="submit">Enviar</button>
        </Form>
      )
    }
    wrap(<FormHarness />)
    await user.type(screen.getByRole('combobox', { name: 'Posición' }), 'REC-01{Enter}')
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Posición' })).toHaveValue('REC-01 · STG'))
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    await waitFor(() => expect(submitted).toEqual({ binId: '10' }))
  })
})

describe('ProductPicker', () => {
  it('busca con search= y activeOnly=true, muestra "SKU · Nombre" y devuelve el publicId con la fila', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<ProductPicker value={null} onChange={onChange} aria-label="Producto" />)
    await user.type(screen.getByRole('combobox', { name: 'Producto' }), 'tu')
    await waitFor(() => expect(mock.requests.some((u) => u.searchParams.get('search') === 'tu')).toBe(true))
    await waitFor(() => expect(screen.queryByText('Tornillo')).toBeNull())
    const last = mock.requests.filter((u) => u.pathname === '/api/v1/products').at(-1)
    expect(last?.searchParams.get('activeOnly')).toBe('true')
    const option = screen.getByRole('option', { name: /TUER-01 · Tuerca/ })
    expect(option).toHaveTextContent('Acme') // dueño cliente visible
    await user.click(option)
    expect(onChange).toHaveBeenCalledWith(PRODUCTS[1].publicId, expect.objectContaining({ trackingTypeCode: 'LOT' }))
  })

  it('ownOnly se envía al API', async () => {
    const user = userEvent.setup()
    wrap(<ProductPicker value={null} onChange={vi.fn()} ownOnly aria-label="Producto" />)
    await user.click(screen.getByRole('combobox', { name: 'Producto' }))
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === '/api/v1/products' && u.searchParams.get('ownOnly') === 'true')).toBe(true))
  })

  it('con un valor inicial pide la ficha para mostrar "SKU · Nombre"', async () => {
    wrap(<ProductPicker value={PRODUCTS[2].publicId} onChange={vi.fn()} aria-label="Producto" />)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Producto' })).toHaveValue('CLAV-01 · Clavo'))
  })
})

describe('ProductMultiFilter', () => {
  function Harness({ onChange }: { onChange: (v: ProductFilterItem[]) => void }) {
    const [value, setValue] = useState<ProductFilterItem[]>([])
    return (
      <ProductMultiFilter
        label="Producto"
        value={value}
        onChange={(v) => {
          setValue(v)
          onChange(v)
        }}
      />
    )
  }

  it('busca en el API por SKU o nombre (no en una página fija), agrega una píldora por producto y la quita', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<Harness onChange={onChange} />)
    await user.type(screen.getByRole('combobox', { name: 'Producto' }), 'clavo')
    await waitFor(() => expect(mock.requests.some((u) => u.pathname === '/api/v1/products' && u.searchParams.get('search') === 'clavo')).toBe(true))
    await user.click(await screen.findByRole('option', { name: /CLAV-01 · Clavo/ }))
    expect(onChange).toHaveBeenLastCalledWith([{ publicId: PRODUCTS[2].publicId, sku: 'CLAV-01', label: 'CLAV-01 · Clavo' }])
    // el buscador queda vacío para agregar otro
    expect(screen.getByRole('combobox', { name: 'Producto' })).toHaveValue('')
    await user.click(screen.getByRole('button', { name: 'Quitar CLAV-01' }))
    expect(onChange).toHaveBeenLastCalledWith([])
  })

  it('por defecto busca solo activos; con includeInactive (Kárdex) omite activeOnly y marca los dados de baja', async () => {
    const user = userEvent.setup()
    const OLD = { id: 9, publicId: 'aaaaaaaa-0000-0000-0000-000000000009', sku: 'VIEJ-01', name: 'Viejo', isOwn: true, isActive: false, trackingTypeCode: 'NONE' }
    mock.handler = (url) => {
      if (url.pathname !== '/api/v1/products') return route(url)
      const items = url.searchParams.get('activeOnly') === 'true' ? PRODUCTS : [...PRODUCTS, OLD]
      return { total: items.length, skip: 0, take: 50, items }
    }
    const { unmount } = wrap(<ProductMultiFilter label="Producto" value={[]} onChange={() => {}} />)
    await user.click(screen.getByRole('combobox', { name: 'Producto' }))
    await screen.findByRole('option', { name: /TORN-01/ })
    expect(mock.requests.filter((u) => u.pathname === '/api/v1/products').every((u) => u.searchParams.get('activeOnly') === 'true')).toBe(true)
    expect(screen.queryByRole('option', { name: /VIEJ-01/ })).toBeNull()
    unmount()

    mock.requests = []
    wrap(<ProductMultiFilter label="Producto" value={[]} onChange={() => {}} includeInactive />)
    await user.click(screen.getByRole('combobox', { name: 'Producto' }))
    const option = await screen.findByRole('option', { name: /VIEJ-01/ })
    expect(option).toHaveTextContent('Inactivo')
    expect(mock.requests.filter((u) => u.pathname === '/api/v1/products').every((u) => u.searchParams.get('activeOnly') !== 'true')).toBe(true)
  })

  it('la píldora recorta un SKU largo con elipsis (no desborda a 360 px)', () => {
    const sku = 'X'.repeat(60)
    wrap(<ProductMultiFilter label="Producto" value={[{ publicId: 'p1', sku, label: `${sku} · Largo` }]} onChange={() => {}} />)
    const text = screen.getByText(sku)
    expect(text).toHaveClass('pfilter-text')
    expect(text.closest('.chip')?.parentElement).toHaveClass('pfilter-chips')
  })
})
