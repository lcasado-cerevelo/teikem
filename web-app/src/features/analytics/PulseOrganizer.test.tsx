import { QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../app/queryClient'
import { setLang } from '../../kernel/i18n/i18n'
import { dismissToast, getToast } from '../../kernel/ui/toastStore'
import { PulseOrganizer } from './PulseOrganizer'
import {
  buildLayoutRequest,
  initOrganizer,
  moveEntry,
  moveInList,
  shownItems,
  shownPanels,
  sortPanels,
  toggleEntry,
  type PulseDto,
  type PulseScope,
} from './pulseLayout'

// Cliente de la app sobre un fetch simulado (misma política que el real).
type Handler = (path: string, method: string, url: URL) => Response | unknown
const mock = vi.hoisted(() => ({ writes: [] as { method: string; url: string; body: unknown }[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    if (req.method !== 'GET') mock.writes.push({ method: req.method, url: `${url.pathname}${url.search}`, body: await req.clone().json().catch(() => null) })
    const result = (mock.handler as Handler)(url.pathname, req.method, url)
    if (result instanceof Response) return result
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

/** Pulso de partida: cuatro paneles (Actividad oculta, uno desconocido), dos indicadores (uno oculto) y un gráfico. */
const PULSE: PulseDto = {
  panels: [
    { key: 'WAREHOUSE', sortOrder: 40, isVisible: true, source: 'default' },
    { key: 'INDICATORS', sortOrder: 20, isVisible: true, source: 'default' },
    { key: 'RADAR', sortOrder: 25, isVisible: true, source: 'default' },
    { key: 'ACTIVITY', sortOrder: 50, isVisible: false, source: 'user' },
    { key: 'CHARTS', sortOrder: 30, isVisible: true, source: 'company' },
  ],
  indicators: [
    { id: 7, name: 'Órdenes', sortOrder: 10, isVisible: false, businessModule: 'OPERATIONS' },
    { id: 5, name: 'Ventas', sortOrder: 0, isVisible: true, isMoney: true, businessModule: 'ACCOUNTING' },
  ],
  charts: [{ id: 9, name: 'Recibos por día', sortOrder: 0, isVisible: true, businessModule: 'WAREHOUSE' }],
  hasPersonalLayout: false,
  canOrganizeCompany: true,
}

function renderOrganizer(scope: PulseScope = 'mine', pulse: PulseDto = PULSE) {
  const client = createQueryClient()
  client.setDefaultOptions({ queries: { retry: false } })
  const onClose = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <PulseOrganizer scope={scope} pulse={pulse} onClose={onClose} />
    </QueryClientProvider>,
  )
  return { onClose }
}

/** Nombres de las filas de una lista del organizador, en orden. */
const rowNames = (list: HTMLElement) =>
  within(list)
    .getAllByRole('listitem')
    .filter((li) => li.parentElement === list)
    .map((li) => li.querySelector('.orgname .nm')?.textContent)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.writes = []
  mock.handler = (_path: string, method: string) => (method === 'PUT' ? PULSE : PULSE)
  dismissToast()
})

describe('pulseLayout (lógica pura)', () => {
  it('shownPanels: orden por sortOrder, sin ocultos ni claves desconocidas; shownItems: orden y sin ocultos', () => {
    expect(shownPanels(PULSE.panels).map((p) => p.key)).toEqual(['INDICATORS', 'CHARTS', 'WAREHOUSE'])
    expect(sortPanels(PULSE.panels).map((p) => p.key)).toEqual(['INDICATORS', 'RADAR', 'CHARTS', 'WAREHOUSE', 'ACTIVITY'])
    expect(shownItems(PULSE.indicators).map((i) => i.name)).toEqual(['Ventas'])
    // empate de sortOrder: por nombre sin distinguir mayúsculas ni acentos
    expect(shownItems([{ name: 'beta', sortOrder: 0 }, { name: 'Álamo', sortOrder: 0 }]).map((i) => i.name)).toEqual(['Álamo', 'beta'])
  })

  it('moveInList / moveEntry / toggleEntry / buildLayoutRequest (orden = índice × 10, todo el estado)', () => {
    expect(moveInList(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b'])
    expect(moveInList(['a', 'b'], 0, 5)).toEqual(['a', 'b'])
    let s = initOrganizer(PULSE)
    expect(s.panels.map((p) => p.key)).toEqual(['INDICATORS', 'CHARTS', 'WAREHOUSE', 'ACTIVITY'])
    s = moveEntry(s, 'charts', 0, 0)
    s = moveEntry(s, 'indicators', 1, 0)
    s = toggleEntry(s, 'panels', 3)
    expect(buildLayoutRequest(s)).toEqual({
      panels: [
        { key: 'INDICATORS', sortOrder: 0, isVisible: true },
        { key: 'CHARTS', sortOrder: 10, isVisible: true },
        { key: 'WAREHOUSE', sortOrder: 20, isVisible: true },
        { key: 'ACTIVITY', sortOrder: 30, isVisible: true },
      ],
      items: [
        { kind: 'indicator', id: 7, sortOrder: 0, isVisible: false },
        { kind: 'indicator', id: 5, sortOrder: 10, isVisible: true },
        { kind: 'chart', id: 9, sortOrder: 0, isVisible: true },
      ],
    })
  })
})

describe('PulseOrganizer', () => {
  it('mine: mover y ocultar generan UN PUT scope=mine con todos los paneles y elementos; toast "Pulso guardado"', async () => {
    const user = userEvent.setup()
    const { onClose } = renderOrganizer('mine')
    expect(screen.getByRole('region', { name: 'Organizando mi Pulso' })).toBeInTheDocument()
    const panels = screen.getByRole('list', { name: 'Paneles del Pulso' })
    // el panel desconocido no se ofrece; los ocultos se quedan en su sitio con "Mostrar"
    expect(rowNames(panels)).toEqual(['Tus indicadores', 'Tus gráficos', 'Almacén', 'Actividad reciente'])
    expect(screen.getByRole('button', { name: 'Mostrar Actividad reciente' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mostrar Órdenes' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Subir Tus indicadores' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Bajar Actividad reciente' })).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Subir Almacén' }))
    await user.click(screen.getByRole('button', { name: 'Ocultar Ventas' }))
    await user.click(screen.getByRole('button', { name: 'Bajar Tus indicadores' }))
    expect(rowNames(panels)).toEqual(['Almacén', 'Tus indicadores', 'Tus gráficos', 'Actividad reciente'])
    // el foco se queda en el botón del panel movido
    expect(screen.getByRole('button', { name: 'Bajar Tus indicadores' })).toHaveFocus()
    // el oculto se queda en su sitio, atenuado, con "Mostrar"
    const items = screen.getByRole('list', { name: 'Elementos de Tus indicadores' })
    expect(rowNames(items)).toEqual(['Ventas', 'Órdenes'])
    expect(screen.getByRole('button', { name: 'Mostrar Ventas' }).closest('li')).toHaveClass('off')
    expect(mock.writes).toEqual([])

    await user.click(screen.getByRole('button', { name: 'Listo' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(mock.writes).toEqual([
      {
        method: 'PUT',
        url: '/api/v1/analytics/pulse/layout?scope=mine',
        body: {
          panels: [
            { key: 'WAREHOUSE', sortOrder: 0, isVisible: true },
            { key: 'INDICATORS', sortOrder: 10, isVisible: true },
            { key: 'CHARTS', sortOrder: 20, isVisible: true },
            { key: 'ACTIVITY', sortOrder: 30, isVisible: false },
          ],
          items: [
            { kind: 'indicator', id: 5, sortOrder: 0, isVisible: false },
            { kind: 'indicator', id: 7, sortOrder: 10, isVisible: false },
            { kind: 'chart', id: 9, sortOrder: 0, isVisible: true },
          ],
        },
      },
    ])
    expect(getToast()?.message).toBe('Pulso guardado')
  })

  it('company: aviso en el título, "Mostrar" y teclado ↑/↓ en el asa; PUT scope=company', async () => {
    const user = userEvent.setup()
    const { onClose } = renderOrganizer('company')
    expect(
      screen.getByRole('region', { name: 'Organizando el Pulso de la compañía (lo verá todo el que no tenga uno personal)' }),
    ).toBeInTheDocument()
    // sin Pulso personal no hay nota de que el orden propio pasa a la compañía
    expect(screen.queryByText(/pasan a ser los de la compañía/)).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Mostrar Actividad reciente' }))
    screen.getByRole('button', { name: 'Mover Tus gráficos' }).focus()
    await user.keyboard('{ArrowUp}')
    const panels = () => rowNames(screen.getByRole('list', { name: 'Paneles del Pulso' }))
    expect(panels()).toEqual(['Tus gráficos', 'Tus indicadores', 'Almacén', 'Actividad reciente'])
    expect(screen.getByRole('button', { name: 'Mover Tus gráficos' })).toHaveFocus()
    await user.keyboard('{ArrowUp}') // ya es el primero: no cambia
    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(panels()).toEqual(['Tus indicadores', 'Almacén', 'Tus gráficos', 'Actividad reciente'])
    // dentro de Tus indicadores: el asa de Órdenes sube
    screen.getByRole('button', { name: 'Mover Órdenes' }).focus()
    await user.keyboard('{ArrowUp}')

    await user.click(screen.getByRole('button', { name: 'Listo' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(mock.writes).toHaveLength(1)
    expect(mock.writes[0].url).toBe('/api/v1/analytics/pulse/layout?scope=company')
    expect(mock.writes[0].body).toEqual({
      panels: [
        { key: 'INDICATORS', sortOrder: 0, isVisible: true },
        { key: 'WAREHOUSE', sortOrder: 10, isVisible: true },
        { key: 'CHARTS', sortOrder: 20, isVisible: true },
        { key: 'ACTIVITY', sortOrder: 30, isVisible: true },
      ],
      items: [
        { kind: 'indicator', id: 7, sortOrder: 0, isVisible: false },
        { kind: 'indicator', id: 5, sortOrder: 10, isVisible: true },
        { kind: 'chart', id: 9, sortOrder: 0, isVisible: true },
      ],
    })
  })

  it('company con Pulso personal: avisa que ese orden pasa a ser el de la compañía', () => {
    renderOrganizer('company', { ...PULSE, hasPersonalLayout: true })
    expect(screen.getByText(/pasan a ser los de la compañía/)).toBeInTheDocument()
  })

  it('arrastrar con el ratón (HTML5): soltar un panel sobre otro lo coloca en su lugar; no cruza de lista', async () => {
    const user = userEvent.setup()
    const { onClose } = renderOrganizer('mine')
    const dataTransfer = { setData: vi.fn(), setDragImage: vi.fn(), effectAllowed: '', dropEffect: '' }
    const panels = screen.getByRole('list', { name: 'Paneles del Pulso' })
    const rowOf = (name: string) => screen.getByRole('button', { name: `Mover ${name}` }).closest('li') as HTMLElement

    // la fila solo se arrastra si se toma por el asa
    expect(rowOf('Almacén')).toHaveAttribute('draggable', 'false')
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Mover Almacén' }))
    expect(rowOf('Almacén')).toHaveAttribute('draggable', 'true')
    fireEvent.dragStart(rowOf('Almacén'), { dataTransfer })
    expect(dataTransfer.setData).toHaveBeenCalledWith('text/plain', 'panel:WAREHOUSE')
    // un elemento de otra lista no acepta el panel: soltar sobre "Ventas" cae en su panel (Tus indicadores)
    fireEvent.dragOver(rowOf('Ventas'), { dataTransfer })
    expect(rowOf('Tus indicadores')).toHaveClass('over')
    fireEvent.drop(rowOf('Ventas'), { dataTransfer })
    fireEvent.dragEnd(rowOf('Almacén'), { dataTransfer })
    expect(rowNames(panels)).toEqual(['Almacén', 'Tus indicadores', 'Tus gráficos', 'Actividad reciente'])
    expect(rowOf('Almacén')).toHaveAttribute('draggable', 'false')

    // un indicador solo se reordena dentro de su panel
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Mover Órdenes' }))
    fireEvent.dragStart(rowOf('Órdenes'), { dataTransfer })
    fireEvent.dragOver(rowOf('Ventas'), { dataTransfer })
    fireEvent.drop(rowOf('Ventas'), { dataTransfer })
    expect(rowNames(screen.getByRole('list', { name: 'Elementos de Tus indicadores' }))).toEqual(['Órdenes', 'Ventas'])

    await user.click(screen.getByRole('button', { name: 'Listo' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    const body = mock.writes[0].body as { panels: { key: string }[]; items: { id: number }[] }
    expect(body.panels.map((p) => p.key)).toEqual(['WAREHOUSE', 'INDICATORS', 'CHARTS', 'ACTIVITY'])
    expect(body.items.map((i) => i.id)).toEqual([7, 5, 9])
  })

  it('en táctil (pointer: coarse) el asa no se arrastra: solo ▲ ▼', () => {
    vi.stubGlobal(
      'matchMedia',
      vi.fn((query: string) => ({
        matches: query === '(pointer: coarse)',
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    )
    try {
      renderOrganizer('mine')
      const handle = screen.getByRole('button', { name: 'Mover Almacén' })
      fireEvent.pointerDown(handle)
      expect(handle.closest('li')).toHaveAttribute('draggable', 'false')
      expect(screen.getByText(/Use ▲ ▼ para mover/)).toBeInTheDocument()
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('"Cancelar" descarta los cambios sin llamar al API', async () => {
    const user = userEvent.setup()
    const { onClose } = renderOrganizer('mine')
    await user.click(screen.getByRole('button', { name: 'Subir Almacén' }))
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    expect(onClose).toHaveBeenCalled()
    expect(mock.writes).toEqual([])
  })

  it('error del servidor: el title del ProblemDetails va al toast y el modo Organizar sigue abierto', async () => {
    mock.handler = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation', title: 'Panel de Pulso desconocido: X.', errors: {} }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    const user = userEvent.setup()
    const { onClose } = renderOrganizer('mine')
    await user.click(screen.getByRole('button', { name: 'Listo' }))
    await waitFor(() => expect(getToast()?.message).toBe('Panel de Pulso desconocido: X.'))
    expect(getToast()?.kind).toBe('error')
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Listo' })).toBeEnabled()
  })
})

