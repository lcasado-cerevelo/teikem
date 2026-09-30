import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SummaryBar } from './SummaryBar'

describe('SummaryBar', () => {
  it('pinta cada cifra con su etiqueta, su tono y el nombre del grupo', () => {
    render(
      <SummaryBar
        label="Resumen de movimientos"
        items={[
          { label: 'Movimientos', value: 126 },
          { label: 'Entradas (uds)', value: '+132', tone: 'in' },
          { label: 'Salidas (uds)', value: '−4', tone: 'out', title: 'Unidades que salieron' },
        ]}
        aside={<span>Hoy</span>}
      />,
    )
    const group = screen.getByRole('group', { name: 'Resumen de movimientos' })
    expect(within(group).getByText('Movimientos').nextSibling).toHaveTextContent('126')
    expect(within(group).getByText('+132').closest('.t')).toHaveClass('in')
    expect(within(group).getByText('−4').closest('.t')).toHaveClass('out')
    expect(within(group).getByText('−4').closest('.t')).toHaveAttribute('title', 'Unidades que salieron')
    expect(within(group).getByText('Hoy')).toBeInTheDocument()
  })

  it('mientras carga se marca ocupado (sin cifras falsas en cero)', () => {
    render(<SummaryBar label="Resumen" loading items={[{ label: 'Movimientos', value: '—' }]} />)
    const group = screen.getByRole('group', { name: 'Resumen' })
    expect(group).toHaveAttribute('aria-busy', 'true')
    expect(group).toHaveClass('loading')
  })
})
