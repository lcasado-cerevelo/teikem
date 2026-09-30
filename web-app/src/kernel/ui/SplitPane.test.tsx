import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { SplitPane } from './SplitPane'

const KEY = 'teikem.split.test'

beforeAll(() => setLang('es'))
beforeEach(() => window.localStorage.clear())
afterEach(() => vi.restoreAllMocks())

/** Ancho del contenedor `.split` (el resto de elementos mide 0, como jsdom). Se fija ANTES de montar. */
function containerWidth(width: number) {
  const original = HTMLElement.prototype.getBoundingClientRect
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    if (this.classList.contains('split')) return { left: 0, top: 0, right: width, bottom: 400, width, height: 400, x: 0, y: 0, toJSON: () => ({}) } as DOMRect
    return original.call(this)
  })
}

/** Simula la ventana: `matches` para las media queries de ancho máximo ≥ `px`. */
function viewport(px: number): () => void {
  const original = window.matchMedia
  window.matchMedia = ((query: string) => {
    const m = /max-width:\s*([\d.]+)px/.exec(query)
    return {
      matches: m ? px <= Number(m[1]) : false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }
  }) as typeof window.matchMedia
  return () => {
    window.matchMedia = original
  }
}

function renderSplit(props: Partial<Parameters<typeof SplitPane>[0]> = {}) {
  return render(
    <SplitPane storageKey="test" {...props}>
      <section aria-label="Recolección">A</section>
      <section aria-label="Recolecciones">B</section>
    </SplitPane>,
  )
}

const bar = () => screen.getByRole('separator', { name: 'Cambiar el ancho de los paneles' })

describe('SplitPane', () => {
  it('dos paneles con la barra accesible entre ellos: 60/40 por defecto', () => {
    const { container } = renderSplit()
    const sep = bar()
    expect(sep).toHaveAttribute('aria-orientation', 'vertical')
    expect(sep).toHaveAttribute('aria-valuenow', '60')
    expect(sep).toHaveAttribute('aria-valuemin', '35')
    expect(sep).toHaveAttribute('aria-valuemax', '75')
    expect(sep).toHaveAttribute('tabindex', '0')
    const split = container.querySelector('.split') as HTMLElement
    expect(split.style.getPropertyValue('--split-a')).toBe('calc((100% - 10px) * 0.6000)')
    // orden: A, barra, B
    expect(Array.from(split.children).map((c) => c.className)).toEqual(['split-pane', 'split-bar', 'split-pane'])
    expect(sep).toHaveAttribute('aria-controls', split.children[0].id)
  })

  it('label propio y defaultRatio', () => {
    renderSplit({ label: 'Ancho de Recolección', defaultRatio: 0.5 })
    expect(screen.getByRole('separator', { name: 'Ancho de Recolección' })).toHaveAttribute('aria-valuenow', '50')
  })

  it('lee lo guardado en teikem.split.<storageKey>; inválido = por defecto; fuera de límites se acota', () => {
    window.localStorage.setItem(KEY, '0.45')
    const first = renderSplit()
    expect(bar()).toHaveAttribute('aria-valuenow', '45')
    first.unmount()

    window.localStorage.setItem(KEY, 'abc')
    const second = renderSplit()
    expect(bar()).toHaveAttribute('aria-valuenow', '60')
    second.unmount()

    window.localStorage.setItem(KEY, '0.9')
    renderSplit()
    expect(bar()).toHaveAttribute('aria-valuenow', '75')
  })

  it('si localStorage falla, usa el por defecto y el teclado sigue funcionando (sin guardar)', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError')
    })
    const user = userEvent.setup()
    renderSplit()
    expect(bar()).toHaveAttribute('aria-valuenow', '60')
    bar().focus()
    await user.keyboard('{ArrowRight}')
    expect(bar()).toHaveAttribute('aria-valuenow', '65')
  })

  it('teclado: ←/→ 5 %, Home/End a los extremos, Enter vuelve a 60/40; cada tecla guarda', async () => {
    const user = userEvent.setup()
    renderSplit()
    bar().focus()
    await user.keyboard('{ArrowRight}')
    expect(bar()).toHaveAttribute('aria-valuenow', '65')
    expect(window.localStorage.getItem(KEY)).toBe('0.65')
    await user.keyboard('{ArrowLeft}{ArrowLeft}{ArrowLeft}')
    expect(bar()).toHaveAttribute('aria-valuenow', '50')
    await user.keyboard('{Home}')
    expect(bar()).toHaveAttribute('aria-valuenow', '35')
    // no baja del mínimo
    await user.keyboard('{ArrowLeft}')
    expect(bar()).toHaveAttribute('aria-valuenow', '35')
    await user.keyboard('{End}')
    expect(bar()).toHaveAttribute('aria-valuenow', '75')
    await user.keyboard('{ArrowRight}')
    expect(bar()).toHaveAttribute('aria-valuenow', '75')
    expect(window.localStorage.getItem(KEY)).toBe('0.75')
    await user.keyboard('{Enter}')
    expect(bar()).toHaveAttribute('aria-valuenow', '60')
    // el por defecto no se guarda: se quita la clave
    expect(window.localStorage.getItem(KEY)).toBeNull()
  })

  it('doble clic en la barra vuelve a 60/40', async () => {
    window.localStorage.setItem(KEY, '0.4')
    const user = userEvent.setup()
    renderSplit()
    expect(bar()).toHaveAttribute('aria-valuenow', '40')
    await user.dblClick(bar())
    expect(bar()).toHaveAttribute('aria-valuenow', '60')
    expect(window.localStorage.getItem(KEY)).toBeNull()
  })

  it('arrastre: sigue al puntero dentro de los mínimos en px y guarda solo al soltar', () => {
    // 1010 px = 1000 útiles + 10 de barra: A entre 420 px (0.42) y 1000-320 px (0.68)
    containerWidth(1010)
    renderSplit()
    const sep = bar()
    expect(sep).toHaveAttribute('aria-valuemin', '42')
    expect(sep).toHaveAttribute('aria-valuemax', '68')
    fireEvent.pointerDown(sep, { pointerId: 1, button: 0, pointerType: 'mouse', clientX: 605 })
    expect(sep).toHaveClass('dragging')
    fireEvent.pointerMove(sep, { pointerId: 1, pointerType: 'mouse', clientX: 505 })
    expect(sep).toHaveAttribute('aria-valuenow', '50')
    expect(window.localStorage.getItem(KEY)).toBeNull()
    // más allá del mínimo de B: se detiene en 68 %
    fireEvent.pointerMove(sep, { pointerId: 1, pointerType: 'mouse', clientX: 990 })
    expect(sep).toHaveAttribute('aria-valuenow', '68')
    fireEvent.pointerMove(sep, { pointerId: 1, pointerType: 'mouse', clientX: 555 })
    fireEvent.pointerUp(sep, { pointerId: 1, pointerType: 'mouse', clientX: 555 })
    expect(sep).not.toHaveClass('dragging')
    expect(sep).toHaveAttribute('aria-valuenow', '55')
    expect(window.localStorage.getItem(KEY)).toBe('0.55')
    // sin arrastre activo, mover el puntero no cambia nada
    fireEvent.pointerMove(sep, { pointerId: 1, pointerType: 'mouse', clientX: 700 })
    expect(sep).toHaveAttribute('aria-valuenow', '55')
  })

  it('el botón derecho no arrastra', () => {
    containerWidth(1010)
    renderSplit()
    fireEvent.pointerDown(bar(), { pointerId: 1, button: 2, pointerType: 'mouse', clientX: 605 })
    fireEvent.pointerMove(bar(), { pointerId: 1, pointerType: 'mouse', clientX: 505 })
    expect(bar()).toHaveAttribute('aria-valuenow', '60')
  })

  it('bajo stackBelow (900 px): una columna sin barra, primero el panel A', () => {
    const restore = viewport(800)
    try {
      const { container } = renderSplit()
      expect(screen.queryByRole('separator')).toBeNull()
      const split = container.querySelector('.split') as HTMLElement
      expect(split).toHaveClass('stacked')
      expect(split.style.getPropertyValue('--split-a')).toBe('')
      const panes = Array.from(split.children)
      expect(panes).toHaveLength(2)
      expect(panes[0]).toHaveTextContent('A')
      expect(panes[1]).toHaveTextContent('B')
    } finally {
      restore()
    }
  })

  it('stackBelow propio: a 1100 px con stackBelow=1200 se apila; con el de por defecto no', () => {
    const restore = viewport(1100)
    try {
      const first = renderSplit({ stackBelow: 1200 })
      expect(screen.queryByRole('separator')).toBeNull()
      first.unmount()
      renderSplit()
      expect(bar()).toBeInTheDocument()
    } finally {
      restore()
    }
  })

  it('si el contenedor no alcanza para los dos mínimos (420 + 320 px), también se apila', () => {
    containerWidth(700)
    renderSplit()
    expect(screen.queryByRole('separator')).toBeNull()
  })
})
