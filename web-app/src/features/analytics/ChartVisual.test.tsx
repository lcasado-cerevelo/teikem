// Lote 15 (P1) — "siempre gráfico": ChartVisual dibuja barra, línea, dona y pastel con 1, 2 o 3 puntos (antes, una lista),
// una línea de un solo punto pinta su punto, sin puntos sale el aviso; tamaño mini sin ejes; colores por punto; etiqueta
// accesible con los valores; fechas del tooltip y del eje.
import { render, screen } from '@testing-library/react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ChartVisual, type ChartVisualPoint } from './ChartVisual'
import { donutCenter, isDayLabel, longDay, shortDay } from './format'

const originalRect = HTMLElement.prototype.getBoundingClientRect

beforeAll(() => {
  setLang('es')
  // Recharts (ResponsiveContainer) mide el contenedor: jsdom no trae ResizeObserver ni tamaños.
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  HTMLElement.prototype.getBoundingClientRect = function () {
    return { width: 480, height: 240, top: 0, left: 0, right: 480, bottom: 240, x: 0, y: 0, toJSON: () => ({}) } as DOMRect
  }
})
afterAll(() => {
  HTMLElement.prototype.getBoundingClientRect = originalRect
  vi.unstubAllGlobals()
})

const pts = (n: number): ChartVisualPoint[] => Array.from({ length: n }, (_, i) => ({ label: `P${i + 1}`, value: (i + 1) * 2 }))

describe('ChartVisual — siempre gráfico', () => {
  for (const chartType of ['BAR', 'LINE', 'DONUT', 'PIE']) {
    for (const n of [1, 2, 3]) {
      it(`${chartType} con ${n} punto(s): se dibuja el gráfico (svg), nunca la lista`, () => {
        const { container } = render(<ChartVisual chartType={chartType} points={pts(n)} isMoney={false} name="Prueba" />)
        const box = screen.getByRole('img', { name: /^Prueba\./ })
        expect(box).toHaveClass('pulse-chartbox')
        expect(box).toHaveAttribute('data-points', String(n))
        expect(box.querySelector('svg.recharts-surface')).not.toBeNull()
        expect(container.querySelector('.pulse-pts')).toBeNull()
        expect(screen.queryByRole('list', { name: 'Prueba' })).toBeNull()
      })
    }
  }

  it('una línea de un solo punto pinta su punto (hallazgo 6); con muchos puntos, sin puntos', () => {
    const { container, unmount } = render(<ChartVisual chartType="LINE" points={[{ label: '2026-09-30', value: 7 }]} isMoney={false} />)
    expect(container.querySelector('.recharts-line-dots circle, .recharts-line-dot')).not.toBeNull()
    unmount()
    const many = Array.from({ length: 20 }, (_, i) => ({ label: `D${i}`, value: i }))
    const { container: c2 } = render(<ChartVisual chartType="LINE" points={many} isMoney={false} />)
    expect(c2.querySelector('.recharts-line-dot')).toBeNull()
  })

  it('sin puntos: el aviso (el del Pulso si se pasa)', () => {
    const { unmount } = render(<ChartVisual chartType="BAR" points={[]} isMoney={false} />)
    expect(screen.getByText('Este gráfico no tiene datos con los filtros y el rango actuales.')).toBeInTheDocument()
    unmount()
    render(<ChartVisual chartType="BAR" points={null} isMoney={false} emptyText="Nada aquí." />)
    expect(screen.getByText('Nada aquí.')).toBeInTheDocument()
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('etiqueta accesible con los valores formateados y las fechas en largo', () => {
    render(
      <ChartVisual
        chartType="BAR"
        points={[
          { label: '2026-09-29', value: 1234.5 },
          { label: 'Otras', key: '$others', value: 3 },
        ]}
        isMoney
        name="Valor"
      />,
    )
    expect(screen.getByRole('img')).toHaveAccessibleName('Valor. martes, 29 de septiembre de 2026: $1,234.50; Otras: $3.00')
  })

  it('mini: sin ejes ni rejilla ni leyenda; tarjeta: con ejes', () => {
    const { container, unmount } = render(<ChartVisual chartType="BAR" points={pts(3)} isMoney={false} size="mini" />)
    expect(container.querySelector('.pulse-chartbox.mini')).not.toBeNull()
    expect(container.querySelector('.recharts-cartesian-axis-tick')).toBeNull()
    expect(container.querySelector('.recharts-cartesian-grid')).toBeNull()
    unmount()
    const { container: card } = render(<ChartVisual chartType="BAR" points={pts(3)} isMoney={false} />)
    expect(card.querySelector('.recharts-cartesian-grid')).not.toBeNull()
  })

  it('colores por punto: cada barra con el suyo (mini, sin animación; un día en 0 también se ve)', () => {
    const { container } = render(
      <ChartVisual
        chartType="BAR"
        size="mini"
        points={[
          { label: 'A', value: 0, color: '#111111' },
          { label: 'B', value: 2, color: '#222222' },
        ]}
        isMoney={false}
      />,
    )
    const fills = Array.from(container.querySelectorAll('.recharts-bar-rectangle path')).map((p) => p.getAttribute('fill'))
    expect(fills).toEqual(['#111111', '#222222'])
  })

  it('la dona lleva el total al centro (D10); si es largo, compacto', () => {
    const { container } = render(
      <ChartVisual
        chartType="DONUT"
        points={[
          { label: 'A', value: 100 },
          { label: 'B', value: 50.25 },
        ]}
        isMoney
      />,
    )
    expect(container.querySelector('.recharts-label')?.textContent).toBe('$150.25')
    expect(donutCenter(150.25, true)).toBe('$150.25')
    expect(donutCenter(1234567.89, true)).toBe('$1.2M')
    expect(donutCenter(98765, false)).toBe('98,765')
  })
})

describe('ChartVisual — fechas', () => {
  it('día del eje en corto y del tooltip en largo, sin corrimiento por zona', () => {
    expect(isDayLabel('2026-09-30')).toBe(true)
    expect(isDayLabel('Recepción')).toBe(false)
    expect(shortDay('2026-09-30', 'es')).toBe('30 sept')
    expect(shortDay('2026-09-30', 'en')).toBe('Sep 30')
    expect(longDay('2026-09-30', 'es')).toBe('miércoles, 30 de septiembre de 2026')
    expect(longDay('2026-09-30', 'en')).toBe('Wednesday, September 30, 2026')
  })
})
