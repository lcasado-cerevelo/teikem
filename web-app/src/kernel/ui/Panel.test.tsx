// Panel de contenido (Fase 0 de la reconciliación con la maqueta): `.panel` + cabecera `.ph2` de una línea
// (ícono + título + contador `.r` + acciones), separado del modal (`Modal`, `.scrim > .pal`).
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Modal } from './Modal'
import { Panel } from './Panel'
import { IconWarehouse } from './screenIcons'

describe('Panel', () => {
  it('es .panel (no .pal) con cabecera .ph2: ícono, título h2, contador .r y acciones en la misma cabecera', () => {
    const { container } = render(
      <Panel icon={<IconWarehouse />} title="Almacenes" badge={12} actions={<button type="button">Nuevo</button>} flush>
        <p>cuerpo</p>
      </Panel>,
    )
    const section = container.querySelector('section') as HTMLElement
    expect(section).toHaveClass('panel')
    expect(section).not.toHaveClass('pal')
    const header = section.querySelector('header') as HTMLElement
    expect(header).toHaveClass('ph2')
    expect(header.querySelector('svg')).not.toBeNull()
    expect(within(header).getByRole('heading', { level: 2, name: 'Almacenes' })).toBeInTheDocument()
    const badge = within(header).getByText('12')
    expect(badge).toHaveClass('r')
    expect(within(header).getByRole('button', { name: 'Nuevo' })).toBeInTheDocument()
    // el contador no es una línea de subtítulo
    expect(header.querySelector('.kit-sub')).toBeNull()
    expect(screen.getByText('cuerpo').parentElement).toHaveClass('pb', 'flush')
  })

  it('el contador 0 se pinta; subtítulo descriptivo en su propia línea; pie .ft', () => {
    render(
      <Panel title="Conciliación" badge={0} subtitle="Compara el Kárdex contra los saldos." footer={<span>pie</span>}>
        x
      </Panel>,
    )
    expect(screen.getByText('0')).toHaveClass('r')
    expect(screen.getByText('Compara el Kárdex contra los saldos.')).toHaveClass('kit-sub')
    expect(screen.getByText('pie').parentElement).toHaveClass('ft')
  })

  it('sin título, ícono, contador ni acciones no hay cabecera', () => {
    const { container } = render(<Panel>solo cuerpo</Panel>)
    expect(container.querySelector('header')).toBeNull()
    expect(screen.getByText('solo cuerpo')).toHaveClass('pb')
  })

  it('el modal sigue siendo .pal (no .panel)', () => {
    render(
      <Modal open title="Editar" onClose={() => {}}>
        <p>contenido</p>
      </Modal>,
    )
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveClass('pal')
    expect(dialog).not.toHaveClass('panel')
  })
})
