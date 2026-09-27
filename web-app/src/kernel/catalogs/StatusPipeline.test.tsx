import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { StatusChip } from './StatusChip'
import { StatusHistory } from './StatusHistory'
import { StatusPipeline } from './StatusPipeline'

// El cliente de la app se sustituye por uno con la misma política sobre un fetch simulado.
const mock = vi.hoisted(() => ({ handler: (_path: string, _req: Request): unknown => [] }))
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  const fetch = async (req: Request) => {
    const body = mock.handler(new URL(req.url).pathname, req)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const STATUSES = [
  { code: 'CAPTURED', label: 'Capturada', colorHex: '#64748B', sortOrder: 10, stageKind: 'PIPELINE', isInitial: true, isEnabled: true },
  { code: 'CONFIRMED', label: 'Confirmada', colorHex: '#3B82F6', sortOrder: 20, stageKind: 'PIPELINE', isInitial: false, isEnabled: true },
  { code: 'IN_ROUTE', label: 'En ruta', colorHex: '#0EA5E9', sortOrder: 30, stageKind: 'PIPELINE', isInitial: false, isEnabled: true },
  { code: 'DELIVERED', label: 'Entregada', colorHex: '#22C55E', sortOrder: 40, stageKind: 'TERMINAL', isInitial: false, isEnabled: true },
  { code: 'ON_HOLD', label: 'En espera', colorHex: 'no-es-color', sortOrder: 50, stageKind: 'LATERAL', isInitial: false, isEnabled: true },
  { code: 'CANCELLED', label: 'Cancelada', colorHex: '#EF4444', sortOrder: 60, stageKind: 'TERMINAL', isInitial: false, isEnabled: true },
]

const HISTORY = [
  { id: 1, fromCode: null, toCode: 'CAPTURED', toLabel: 'Capturada', changedAtUtc: '2026-07-20T10:00:00Z', changedByName: 'Ana' },
  { id: 2, fromCode: 'CAPTURED', fromLabel: 'Capturada', toCode: 'CONFIRMED', toLabel: 'Confirmada', comment: 'Cliente confirmó', changedAtUtc: '2026-07-20T11:00:00Z', changedByName: 'Luis' },
]

function route(path: string): unknown {
  if (path === '/api/v1/status/OrderStatus') return STATUSES
  if (path === '/api/v1/status/OrderStatus/validate') return { isValid: true, errors: [], warnings: [] }
  if (path === '/api/v1/status/lateral-entries/TRANSPORT_ORDER')
    return [{ lateralStatusCode: 'CANCELLED', fromStatusCode: 'CAPTURED', isAllowed: true, isTenantRule: false }]
  if (path === '/api/v1/status/history/TRANSPORT_ORDER/7') return HISTORY
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404 })
}

beforeAll(() => setLang('es'))

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

describe('StatusPipeline', () => {
  it('dibuja etapas y ofrece solo las transiciones válidas; confirma con comentario', async () => {
    mock.handler = route
    const onTransition = vi.fn(async () => undefined)
    const user = userEvent.setup()
    wrap(<StatusPipeline domain="OrderStatus" entityType="TRANSPORT_ORDER" entityId={7} currentCode="CONFIRMED" onTransition={onTransition} />)

    const advance = await screen.findByRole('button', { name: 'Avanzar a En ruta' })
    expect(screen.getByText('Confirmada').closest('li')).toHaveAttribute('aria-current', 'step')
    // Sin reglas → permitido; con regla solo desde CAPTURED → no se ofrece
    expect(screen.getByRole('button', { name: 'Pasar a En espera' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pasar a Cancelada' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Capturada/ })).toBeNull()

    await user.click(advance)
    await user.type(screen.getByLabelText('Comentario (opcional)'), 'Sale el camión')
    await user.click(screen.getByRole('button', { name: 'Cambiar estatus' }))
    await waitFor(() => expect(onTransition).toHaveBeenCalledWith('IN_ROUTE', 'Sale el camión'))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('si el servidor rechaza, el diálogo queda abierto con el mensaje', async () => {
    mock.handler = route
    const { ApiError } = await import('../api/problem')
    const onTransition = vi.fn(async () => {
      throw new ApiError(422, { title: "Salto ilegal: de 'CONFIRMED' solo se puede avanzar a 'IN_ROUTE'.", code: 'status_rule' })
    })
    const user = userEvent.setup()
    wrap(<StatusPipeline domain="OrderStatus" entityType="TRANSPORT_ORDER" entityId={7} currentCode="CONFIRMED" onTransition={onTransition} />)
    await user.click(await screen.findByRole('button', { name: 'Avanzar a En ruta' }))
    await user.click(screen.getByRole('button', { name: 'Cambiar estatus' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Salto ilegal')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('con manualTargets solo ofrece esos códigos (el resto lo dispara el sistema)', async () => {
    mock.handler = route
    const onTransition = vi.fn(async () => undefined)
    wrap(
      <StatusPipeline
        domain="OrderStatus"
        entityType="TRANSPORT_ORDER"
        entityId={7}
        currentCode="CONFIRMED"
        onTransition={onTransition}
        manualTargets={['ON_HOLD']}
      />,
    )
    expect(await screen.findByRole('button', { name: 'Pasar a En espera' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Avanzar a En ruta' })).toBeNull()
  })

  it('sin onTransition es solo lectura', async () => {
    mock.handler = route
    wrap(<StatusPipeline domain="OrderStatus" entityType="TRANSPORT_ORDER" currentCode="CONFIRMED" />)
    await screen.findByText('En ruta')
    expect(screen.queryByRole('button')).toBeNull()
  })
})

describe('StatusChip y StatusHistory', () => {
  it('StatusChip usa la etiqueta y el color del tenant; color inválido → neutro', async () => {
    mock.handler = route
    wrap(
      <>
        <StatusChip domain="OrderStatus" code="in_route" />
        <StatusChip domain="OrderStatus" code="ON_HOLD" />
        <StatusChip domain="OrderStatus" code={null} />
      </>,
    )
    expect(await screen.findByText('En ruta')).toHaveStyle({ color: '#0EA5E9' })
    expect(screen.getByText('En espera').getAttribute('style')).toBeNull()
    expect(screen.getByText('Sin estatus')).toBeInTheDocument()
  })

  it('StatusHistory lista el más reciente primero con comentario y autor', async () => {
    mock.handler = route
    wrap(<StatusHistory entityType="TRANSPORT_ORDER" entityId={7} domain="OrderStatus" />)
    const items = await screen.findAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Cliente confirmó')
    expect(items[0]).toHaveTextContent('Luis')
    expect(items[1]).toHaveTextContent('Ana')
  })
})
