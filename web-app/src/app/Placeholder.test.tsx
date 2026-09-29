import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import { setLang } from '../kernel/i18n/i18n'
import { closeCommandPalette, isCommandPaletteOpen } from '../kernel/ui/commandPaletteStore'
import Placeholder from './Placeholder'
import { appRoutes } from './routes'

describe('Placeholder (pantalla pendiente)', () => {
  afterEach(() => {
    closeCommandPalette()
    setLang('es')
  })

  it('muestra el título y el subtítulo del ítem, el aviso y "Abrir otra pantalla" abre la paleta', async () => {
    setLang('es')
    const user = userEvent.setup()
    render(<Placeholder navKey="dispatch" />)
    expect(screen.getByRole('heading', { level: 1, name: 'Sala de despacho' })).toBeInTheDocument()
    expect(screen.getByText('armar y despachar rutas')).toBeInTheDocument()
    expect(screen.getByText('Esta pantalla llega en un lote posterior.')).toBeInTheDocument()
    expect(isCommandPaletteOpen()).toBe(false)
    await user.click(screen.getByRole('button', { name: 'Abrir otra pantalla' }))
    expect(isCommandPaletteOpen()).toBe(true)
  })

  it('en inglés usa los textos en inglés del ítem', () => {
    setLang('en')
    render(<Placeholder navKey="devices" />)
    expect(screen.getByRole('heading', { level: 1, name: 'Mobile devices' })).toBeInTheDocument()
    expect(screen.getByText('This screen arrives in a later batch.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open another screen' })).toBeInTheDocument()
  })

  it('arriba va la marca pequeña de Teikem por defecto; `brand={null}` la quita y otra la sustituye', () => {
    const first = render(<Placeholder navKey="audit" />)
    const mark = within(first.container.querySelector('.pending-brand') as HTMLElement).getByTestId('brand-mark')
    expect(mark).toHaveAttribute('src', expect.stringMatching(/\/brand\/teikem-symbol\.svg$/))
    expect(mark).toHaveAttribute('width', '40')
    first.unmount()
    const second = render(<Placeholder navKey="audit" brand={null} />)
    expect(second.container.querySelector('.pending-brand')).toBeNull()
    second.unmount()
    render(<Placeholder navKey="audit" brand={<span>marca</span>} />)
    expect(screen.getByText('marca')).toBeInTheDocument()
    expect(screen.queryByTestId('brand-mark')).toBeNull()
  })

  it('la ruta pendiente de routes.tsx pinta el Placeholder de su ítem', () => {
    setLang('es')
    const route = appRoutes.find((r) => r.path === '/catalog/clients')!
    const Screen = route.element
    render(<Screen />)
    expect(screen.getByTestId('placeholder-screen')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Clientes y contratos' })).toBeInTheDocument()
    expect(screen.getByText('cuentas, SLA, tarifas')).toBeInTheDocument()
  })
})
