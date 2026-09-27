import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AccessProvider } from './AccessProvider'
import { Can } from './Can'
import { ModuleGate } from './ModuleGate'

function renderWith(ui: ReactNode, permissions: string[], modules: string[]) {
  return render(
    <MemoryRouter>
      <AccessProvider permissions={permissions} modules={modules}>
        {ui}
      </AccessProvider>
    </MemoryRouter>,
  )
}

describe('Can', () => {
  it('pinta el contenido con permiso', () => {
    renderWith(<Can perm="orders.create">crear</Can>, ['orders.create'], [])
    expect(screen.getByText('crear')).toBeInTheDocument()
  })

  it('sin permiso no pinta nada (o el fallback)', () => {
    renderWith(
      <>
        <Can perm="orders.create">crear</Can>
        <Can perm={['orders.create', 'orders.view']} fallback="nada">
          ambos
        </Can>
      </>,
      ['orders.view'],
      [],
    )
    expect(screen.queryByText('crear')).toBeNull()
    expect(screen.queryByText('ambos')).toBeNull()
    expect(screen.getByText('nada')).toBeInTheDocument()
  })
})

describe('ModuleGate', () => {
  it('módulo encendido y con permiso: pinta el contenido', () => {
    renderWith(
      <ModuleGate module="CATALOG" perm="clients.view">
        clientes
      </ModuleGate>,
      ['clients.view'],
      ['CATALOG'],
    )
    expect(screen.getByText('clientes')).toBeInTheDocument()
  })

  it('módulo apagado: pantalla Módulo apagado', () => {
    renderWith(<ModuleGate module="WMS_LOTSERIAL">lotes</ModuleGate>, [], ['CATALOG'])
    expect(screen.queryByText('lotes')).toBeNull()
    expect(screen.getByTestId('module-off-screen')).toBeInTheDocument()
  })

  it('módulo encendido sin permiso: pantalla Sin permiso', () => {
    renderWith(
      <ModuleGate module="CATALOG" perm="clients.view">
        clientes
      </ModuleGate>,
      [],
      ['CATALOG'],
    )
    expect(screen.queryByText('clientes')).toBeNull()
    expect(screen.getByTestId('forbidden-screen')).toBeInTheDocument()
  })
})
