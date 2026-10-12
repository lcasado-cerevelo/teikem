// Lote F-E2b — Plano de ruta esquemático: pines por precisión y estado, vacío, sin ubicación, selección, teclado, punto vivo y clic en el plano.
import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { RouteMap, type RouteMapStop } from './RouteMap'

beforeAll(() => setLang('es'))

const A: RouteMapStop = { id: 1, seq: 1, label: 'Parada 1 · Farmacia Sol · Bayamón', point: { lat: 18.4, lng: -66.1 }, accuracy: 'exact' }
const B: RouteMapStop = { id: 2, seq: 2, label: 'Parada 2 · Colmado Luna · Caguas', point: { lat: 18.2, lng: -66.0 }, accuracy: 'approximate' }
const C: RouteMapStop = { id: 3, seq: 3, label: 'Parada 3 · Taller Díaz · Toa Baja', point: { lat: 18.45, lng: -66.2 }, accuracy: 'manual', state: 'done' }
const N: RouteMapStop = { id: 4, seq: 4, label: 'Parada 4 · Sin punto · Vieques', short: 'OT-0004', point: null }

const plane = () => screen.getByRole('group', { name: 'Plano de la ruta 2026-0001' })
const props = { label: 'Plano de la ruta 2026-0001' }

describe('RouteMap', () => {
  it('pinta un pin por parada con punto, con su número y su nombre accesible «Parada 3 · Consignatario · pueblo»', () => {
    render(<RouteMap {...props} stops={[A, B, C, N]} />)
    for (const s of [A, B, C]) expect(within(plane()).getByRole('img', { name: s.label })).toBeInTheDocument()
    expect(within(plane()).queryByRole('img', { name: N.label })).toBeNull() // sin punto: no se dibuja
    expect(within(plane()).getByRole('img', { name: A.label })).toHaveTextContent('1')
    expect(within(plane()).getByRole('img', { name: C.label })).toHaveTextContent('3')
  })

  it('la precisión se distingue por la forma: exacto = círculo sólido, aproximado = círculo punteado con halo, manual = rombo', () => {
    render(<RouteMap {...props} stops={[A, B, C]} />)
    const pin = (s: RouteMapStop) => within(plane()).getByRole('img', { name: s.label })
    expect(pin(A)).toHaveClass('exact')
    expect(pin(A).querySelectorAll('circle[stroke-dasharray]')).toHaveLength(0)
    expect(pin(B)).toHaveClass('approximate')
    expect(pin(B).querySelectorAll('circle[stroke-dasharray]')).toHaveLength(1)
    expect(pin(B).querySelectorAll('circle[opacity]').length).toBeGreaterThanOrEqual(1) // halo
    expect(pin(C)).toHaveClass('manual')
    expect(pin(C).querySelector('rect[transform="rotate(45)"]')).not.toBeNull()
    // el tooltip dice la precisión y el estado
    expect(pin(B).querySelector('title')).toHaveTextContent('Pin aproximado')
    expect(pin(C).querySelector('title')).toHaveTextContent('Pin manual · Entregada')
  })

  it('los estados se distinguen por color y por data-state', () => {
    const failed: RouteMapStop = { ...A, id: 9, seq: 9, label: 'Parada 9 · Falla · X', point: { lat: 18.41, lng: -66.11 }, state: 'failed' }
    const current: RouteMapStop = { ...A, id: 8, seq: 8, label: 'Parada 8 · En curso · Y', point: { lat: 18.3, lng: -66.3 }, state: 'current' }
    render(<RouteMap {...props} stops={[A, C, failed, current]} />)
    expect(within(plane()).getByRole('img', { name: A.label })).toHaveAttribute('data-state', 'pending')
    expect(within(plane()).getByRole('img', { name: C.label })).toHaveAttribute('data-state', 'done')
    expect(within(plane()).getByRole('img', { name: failed.label })).toHaveAttribute('data-state', 'failed')
    expect(within(plane()).getByRole('img', { name: current.label })).toHaveAttribute('data-state', 'current')
    // la leyenda dice solo los estados que aparecen
    expect(screen.getByText('Entregada')).toBeInTheDocument()
    expect(screen.getByText('Fallida')).toBeInTheDocument()
    expect(screen.getByText('En curso')).toBeInTheDocument()
  })

  it('la línea une las paradas con punto en orden de la secuencia (una menos que pines) y no pasa por las que no tienen punto', () => {
    const { container } = render(<RouteMap {...props} stops={[A, N, B, C]} />)
    expect(container.querySelectorAll('line.rmap-line')).toHaveLength(2)
  })

  it('sin ninguna parada con coordenadas: mensaje claro, la indicación y las paradas listadas «Sin ubicación: N»', () => {
    render(<RouteMap {...props} stops={[{ ...N, id: 1, seq: 1 }, { ...N, id: 2, seq: 2, short: 'OT-0005', label: 'Parada 2 · X · Y' }]} emptyHint="Ponga un pin manual a cada parada." />)
    expect(screen.getByRole('status')).toHaveTextContent('Ninguna parada tiene coordenadas todavía')
    expect(screen.getByRole('status')).toHaveTextContent('Ponga un pin manual a cada parada.')
    expect(screen.getByText('Sin ubicación: 2')).toBeInTheDocument()
    expect(screen.getByText('OT-0004')).toBeInTheDocument()
    expect(screen.getByText('OT-0005')).toBeInTheDocument()
  })

  it('sin paradas dice que la ruta no tiene paradas', () => {
    render(<RouteMap {...props} stops={[]} />)
    expect(screen.getByRole('status')).toHaveTextContent('La ruta no tiene paradas.')
    expect(screen.queryByText(/Sin ubicación/)).toBeNull()
  })

  it('con algún punto no hay mensaje de vacío; las paradas sin punto se listan aparte', () => {
    render(<RouteMap {...props} stops={[A, N]} />)
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.getByText('Sin ubicación: 1')).toBeInTheDocument()
  })

  it('un punto con coordenadas fuera de rango cuenta como sin ubicación', () => {
    render(<RouteMap {...props} stops={[{ ...A, point: { lat: 99, lng: 0 } }]} />)
    expect(screen.getByText('Sin ubicación: 1')).toBeInTheDocument()
  })

  it('con onSelect los pines son botones accesibles: foco por teclado, clic, Enter y espacio, y aria-pressed sigue a la selección', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    const { rerender } = render(<RouteMap {...props} stops={[A, B, C]} onSelect={onSelect} selectedId={null} />)
    const pinB = within(plane()).getByRole('button', { name: B.label })
    expect(pinB).toHaveAttribute('tabindex', '0')
    expect(pinB).toHaveAttribute('aria-pressed', 'false')
    await user.click(pinB)
    expect(onSelect).toHaveBeenLastCalledWith(2)
    within(plane()).getByRole('button', { name: A.label }).focus()
    await user.keyboard('{Enter}')
    expect(onSelect).toHaveBeenLastCalledWith(1)
    within(plane()).getByRole('button', { name: C.label }).focus()
    await user.keyboard(' ')
    expect(onSelect).toHaveBeenLastCalledWith(3)
    rerender(<RouteMap {...props} stops={[A, B, C]} onSelect={onSelect} selectedId={2} />)
    expect(within(plane()).getByRole('button', { name: B.label })).toHaveAttribute('aria-pressed', 'true')
    expect(within(plane()).getByRole('button', { name: A.label })).toHaveAttribute('aria-pressed', 'false')
    // el seleccionado se dibuja con su aro
    expect(within(plane()).getByRole('button', { name: B.label }).querySelector('circle.sel')).not.toBeNull()
  })

  it('las paradas sin ubicación también se eligen (chip) cuando hay onSelect', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    render(<RouteMap {...props} stops={[A, N]} onSelect={onSelect} selectedId={4} />)
    const chip = screen.getByRole('button', { name: N.label })
    expect(chip).toHaveAttribute('aria-pressed', 'true')
    await user.click(chip)
    expect(onSelect).toHaveBeenCalledWith(4)
  })

  it('sin onSelect nada es botón ni entra en el orden de tabulación', () => {
    render(<RouteMap {...props} stops={[A, B, N]} />)
    expect(screen.queryAllByRole('button')).toHaveLength(0)
    expect(plane().querySelector('[tabindex]')).toBeNull()
  })

  it('pines en la misma dirección se abren en abanico: todos se ven y se pueden tocar', () => {
    const same = [1, 2, 3].map((n) => ({ id: n, seq: n, label: `Parada ${n} · X · Y`, point: { lat: 18.4, lng: -66.1 } }))
    render(<RouteMap {...props} stops={same} onSelect={() => undefined} />)
    const spots = same.map((s) => within(plane()).getByRole('button', { name: s.label }).getAttribute('transform'))
    expect(new Set(spots).size).toBe(3)
  })

  it('punto vivo, origen y leyenda: solo aparecen si se dan', () => {
    const { rerender } = render(<RouteMap {...props} stops={[A, B]} />)
    expect(screen.queryByRole('img', { name: 'Chofer D-01' })).toBeNull()
    expect(screen.queryByText('Posición del chofer')).toBeNull()
    rerender(<RouteMap {...props} stops={[A, B]} live={{ lat: 18.3, lng: -66.05, label: 'Chofer D-01' }} origin={{ lat: 18.35, lng: -66.0, label: 'Almacén central' }} />)
    expect(within(plane()).getByRole('img', { name: 'Chofer D-01' })).toBeInTheDocument()
    expect(within(plane()).getByRole('img', { name: 'Almacén central' })).toBeInTheDocument()
    expect(screen.getByText('Posición del chofer')).toBeInTheDocument()
  })

  it('el punto vivo cuenta para el ajuste aunque no haya paradas con punto (no hay mensaje de vacío)', () => {
    render(<RouteMap {...props} stops={[N]} live={{ lat: 18.3, lng: -66.05, label: 'Chofer D-01' }} />)
    expect(screen.queryByText('Ninguna parada tiene coordenadas todavía')).toBeNull()
  })

  it('la leyenda lleva exacto, aproximado y manual, y avisa que es un plano esquemático', () => {
    render(<RouteMap {...props} stops={[A]} />)
    expect(screen.getByText('Pin exacto')).toBeInTheDocument()
    expect(screen.getByText('Aproximado (centro del CP o del pueblo)')).toBeInTheDocument()
    expect(screen.getByText('Pin manual')).toBeInTheDocument()
    expect(screen.getByText(/Plano esquemático/)).toBeInTheDocument()
  })

  it('mide su alto (300 por omisión) y no usa imágenes ni servidores externos', () => {
    const { container } = render(<RouteMap {...props} stops={[A, B]} />)
    const svg = container.querySelector('svg.rmap-svg')!
    expect(svg).toHaveAttribute('height', '300')
    expect(container.querySelectorAll('img, image, iframe')).toHaveLength(0)
  })

  it('con onPick, un clic en el plano entrega una posición dentro del área de los puntos y el clic en un pin no la entrega', () => {
    const onPick = vi.fn()
    const { container } = render(<RouteMap {...props} stops={[A, B, C]} onPick={onPick} onSelect={() => undefined} />)
    // sin layout (jsdom) el plano mide 600×300 y el rectángulo del SVG es 0,0: el clic (300,150) cae al centro del plano
    fireEvent.click(container.querySelector('rect.rmap-bg')!, { clientX: 300, clientY: 150 })
    expect(onPick).toHaveBeenCalledTimes(1)
    const p = onPick.mock.calls[0][0] as { lat: number; lng: number }
    expect(p.lat).toBeGreaterThan(18.2)
    expect(p.lat).toBeLessThan(18.45)
    expect(p.lng).toBeGreaterThan(-66.2)
    expect(p.lng).toBeLessThan(-66.0)
    expect(screen.getByText('Toque el plano para tomar una posición aproximada.')).toBeInTheDocument()
    fireEvent.click(within(plane()).getByRole('button', { name: A.label }))
    expect(onPick).toHaveBeenCalledTimes(1)
  })

  it('sin onPick el clic en el plano no hace nada y no hay cursor de mira', () => {
    const { container } = render(<RouteMap {...props} stops={[A, B]} />)
    expect(container.querySelector('svg.picking')).toBeNull()
  })

  it('el borrador (pin en prueba) se dibuja sin mover el ajuste del plano', () => {
    const base = render(<RouteMap {...props} stops={[A, B]} />)
    const before = within(base.container).getByRole('img', { name: A.label }).getAttribute('transform')
    base.unmount()
    const withDraft = render(<RouteMap {...props} stops={[A, B]} draft={{ lat: 18.3, lng: -66.05 }} />)
    expect(withDraft.container.querySelector('g.rmap-draft')).not.toBeNull()
    expect(within(withDraft.container).getByRole('img', { name: A.label }).getAttribute('transform')).toBe(before)
  })
})
