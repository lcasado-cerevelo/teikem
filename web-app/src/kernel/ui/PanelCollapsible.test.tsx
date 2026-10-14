// Panel desplegable (`collapsible`): flecha en la cabecera, cerrado muestra el resumen y oculta cuerpo y acciones (el cuerpo sigue
// montado), un clic en la cabecera también abre y cierra (sin pisar los botones), y `storageKey` recuerda abierto/cerrado.
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { Panel } from './Panel'

beforeAll(() => setLang('es'))
beforeEach(() => window.localStorage.clear())

function renderPanel(props: Partial<React.ComponentProps<typeof Panel>> = {}) {
  return render(
    <Panel title="Contactos" collapsible summary="Personas de contacto: 3" actions={<button type="button">Agregar</button>} {...props}>
      <input aria-label="nombre" defaultValue="" />
    </Panel>,
  )
}

describe('Panel — desplegable', () => {
  it('abierto: no muestra el resumen y la flecha dice «Cerrar el panel»', () => {
    renderPanel()
    const arrow = screen.getByRole('button', { name: 'Cerrar el panel' })
    expect(arrow).toHaveAttribute('aria-expanded', 'true')
    expect(screen.queryByText('Personas de contacto: 3')).toBeNull()
    expect(screen.getByLabelText('nombre')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Agregar' })).toBeVisible()
  })

  it('cerrado: muestra el resumen, oculta el cuerpo y las acciones, pero el cuerpo sigue montado (conserva lo escrito)', async () => {
    const user = userEvent.setup()
    renderPanel()
    await user.type(screen.getByLabelText('nombre'), 'Ana')
    await user.click(screen.getByRole('button', { name: 'Cerrar el panel' }))
    expect(screen.getByText('Personas de contacto: 3')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Abrir el panel' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByLabelText('nombre', { selector: 'input' })).not.toBeVisible()
    expect(document.querySelector('section')).toHaveClass('kit-collapsed')
    await user.click(screen.getByRole('button', { name: 'Abrir el panel' }))
    expect(screen.getByLabelText('nombre')).toHaveValue('Ana')
    expect(screen.queryByText('Personas de contacto: 3')).toBeNull()
  })

  it('un clic en la cabecera abre y cierra, pero un botón de acciones hace lo suyo sin cerrar', async () => {
    const user = userEvent.setup()
    renderPanel()
    await user.click(screen.getByRole('heading', { name: 'Contactos' }))
    expect(screen.getByText('Personas de contacto: 3')).toBeInTheDocument()
    await user.click(screen.getByRole('heading', { name: 'Contactos' }))
    expect(screen.queryByText('Personas de contacto: 3')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Agregar' }))
    expect(screen.queryByText('Personas de contacto: 3')).toBeNull()
  })

  it('defaultCollapsed arranca cerrado y storageKey recuerda lo que el usuario eligió', async () => {
    const user = userEvent.setup()
    const first = renderPanel({ defaultCollapsed: true, storageKey: 'clients.people' })
    expect(screen.getByText('Personas de contacto: 3')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Abrir el panel' }))
    expect(window.localStorage.getItem('teikem.panel.clients.people')).toBe('0')
    first.unmount()
    renderPanel({ defaultCollapsed: true, storageKey: 'clients.people' })
    expect(screen.queryByText('Personas de contacto: 3')).toBeNull() // recordó «abierto» aunque el panel arranque cerrado
  })

  it('sin storage disponible sigue funcionando (solo vale para esa visita)', async () => {
    const user = userEvent.setup()
    const original = Storage.prototype.setItem
    Storage.prototype.setItem = () => {
      throw new Error('bloqueado')
    }
    try {
      renderPanel({ storageKey: 'x' })
      fireEvent.click(screen.getByRole('button', { name: 'Cerrar el panel' }))
      expect(screen.getByText('Personas de contacto: 3')).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Abrir el panel' }))
    } finally {
      Storage.prototype.setItem = original
    }
  })

  it('sin `collapsible` el panel es el de siempre: sin flecha y sin resumen', () => {
    render(
      <Panel title="Normal" summary="no se ve">
        <p>cuerpo</p>
      </Panel>,
    )
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.queryByText('no se ve')).toBeNull()
  })
})
