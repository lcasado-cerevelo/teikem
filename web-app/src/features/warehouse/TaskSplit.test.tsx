// Reparto por posición en la web (Completar una tarea de acomodo): "Repartir en varias posiciones" pide la cantidad por posición, se eligen las
// posiciones una por una, la última recibe el resto con la alerta fija (cerrable), cada renglón se quita con su ✕, una posición con cupo que no alcanza
// avisa sin bloquear y Guardar manda una sola llamada POST /warehouse-tasks/{id}/distribute.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { CompleteTaskModal } from './taskDialogs'

type Req = { method: string; url: URL; body: unknown }
const mock = vi.hoisted(() => ({ requests: [] as Req[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = req.method === 'GET' ? '' : await req.text()
    mock.requests.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const json = (data: unknown) => new Response(JSON.stringify(data), { status: 200, headers: { 'Content-Type': 'application/json' } })
    if (url.pathname === '/api/v1/warehouse-tasks/putaway-suggestions') return json([])
    if (url.pathname === `/api/v1/warehouses/${WH}/zones`) return json([])
    if (url.pathname === `/api/v1/warehouses/${WH}/bins`) {
      const items = BINS.filter((b) => b.code.toLowerCase().includes((url.searchParams.get('search') ?? '').toLowerCase()))
      return json({ total: items.length, skip: 0, take: 50, items })
    }
    if (req.method === 'POST' && url.pathname === '/api/v1/warehouse-tasks/1/distribute') return json({ id: 1, statusCode: 'DONE' })
    return json({})
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const WH = '11111111-1111-1111-1111-111111111111'
const BINS = [
  { id: 10, code: 'A-01', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'RESERVE', qtyOnHand: 10, maxCapacityQty: 25, productCount: 1, occupancy: 'PARTIAL', isActive: true },
  { id: 11, code: 'A-02', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'RESERVE', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
  { id: 12, code: 'A-03', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'RESERVE', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
  { id: 13, code: 'A-04', zoneId: 1, zoneCode: 'A', zoneTypeCode: 'RESERVE', qtyOnHand: 0, productCount: 0, occupancy: 'EMPTY', isActive: true },
]
const TASK = { id: 1, typeCode: 'PUTAWAY', statusCode: 'PENDING', warehousePublicId: WH, quantity: 45, sku: 'SKU-1', completableFromQueue: true }

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
})

function open(task = TASK) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={['inventory.view', 'warehouse.receive']} modules={['WMS_LOTSERIAL']}>
        <CompleteTaskModal task={task} open onClose={onClose} />
      </AccessProvider>
    </QueryClientProvider>,
  )
  return { onClose }
}

async function addBin(user: ReturnType<typeof userEvent.setup>, code: string) {
  const box = screen.getByRole('combobox', { name: 'Agregar posición' })
  await user.clear(box)
  await user.type(box, code)
  await user.click(await screen.findByRole('option', { name: new RegExp(code) }))
}

describe('Completar acomodo — repartir en varias posiciones', () => {
  it('45 de 20: 20 + 20 + 5 con alerta fija cerrable, quitar un renglón recalcula, el cupo avisa sin bloquear y Guardar manda un solo distribute', async () => {
    const user = userEvent.setup()
    const { onClose } = open()
    // el interruptor aparece con una tarea de acomodo con cantidad y sin serie
    await user.click(await screen.findByRole('switch', { name: 'Repartir en varias posiciones' }))
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled()
    await user.type(screen.getByLabelText('Cantidad por posición'), '20')

    await addBin(user, 'A-01')
    expect(await screen.findByText('A-01 · 20')).toBeInTheDocument()
    // A-01 tiene cupo de 25 con 10 en existencia (15 libres): avisa, sin bloquear
    expect(screen.getByText('Cupo para 15: recibirá 20. Se puede confirmar igual.')).toBeInTheDocument()
    await addBin(user, 'A-02')
    expect(await screen.findByText('A-02 · 20')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).toBeNull()

    // la tercera recibe lo que quedaba (5) y sale la alerta fija, que se cierra
    await addBin(user, 'A-03')
    expect(await screen.findByText('A-03 · 5')).toBeInTheDocument()
    expect(screen.getByText('A-03 recibe solo 5 (lo que quedaba), no 20.')).toBeInTheDocument()
    expect(screen.getByText('Repartido: 45 · quedan 0 sin repartir')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cerrar aviso' }))
    expect(screen.queryByText('A-03 recibe solo 5 (lo que quedaba), no 20.')).toBeNull()

    // una cuarta ya no cabe
    await addBin(user, 'A-04')
    expect(await screen.findByText('Ya no hay unidades por repartir: las 45 están repartidas.')).toBeInTheDocument()
    expect(screen.queryByText('A-04 · 20')).toBeNull()

    // quitar A-01: A-02 y A-03 pasan a 20 y 20 (ya no hay posición de resto)
    await user.click(screen.getByRole('button', { name: 'Quitar A-01' }))
    expect(await screen.findByText('A-03 · 20')).toBeInTheDocument()
    expect(screen.getByText('Repartido: 40 · quedan 5 sin repartir')).toBeInTheDocument()
    await addBin(user, 'A-01')
    expect(await screen.findByText('A-01 · 5')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.url.pathname.endsWith('/distribute'))).toBe(true))
    expect(mock.requests.find((r) => r.url.pathname.endsWith('/distribute'))?.body).toEqual({ quantityPerBin: 20, toBinIds: [11, 12, 10] })
    expect(mock.requests.some((r) => r.url.pathname.endsWith('/complete'))).toBe(false)
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('sin cantidad (o tarea de serie) no ofrece repartir', async () => {
    open({ ...TASK, quantity: 0 })
    await screen.findByText('Completar tarea')
    expect(screen.queryByRole('switch', { name: 'Repartir en varias posiciones' })).toBeNull()
  })
})
