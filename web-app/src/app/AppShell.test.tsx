// Cabecera del shell (Lote F8a P0): tema claro/oscuro, reloj "en vivo" y paleta de comandos con el menú completo.
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../kernel/access'
import { setLang } from '../kernel/i18n/i18n'
import { closeCommandPalette } from '../kernel/ui/commandPaletteStore'
import { setTheme, THEME_STORAGE_KEY } from '../kernel/ui/theme'
import { AppShell } from './AppShell'
import { RouteGate } from './RouteGate'
import { appRoutes } from './routes'
import { SessionContext, type Session } from './session'

const SESSION: Session = {
  me: {
    userId: 1,
    fullName: 'Ana Admin',
    email: 'admin@teikem.local',
    tenantId: 1,
    tenantName: 'Demo Logística',
    memberships: [],
    permissions: [],
    enabledModules: [],
  } as unknown as Session['me'],
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 1,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(),
  modules: new Set(),
  reloadMe: async () => {},
}

function Where() {
  return <output data-testid="where">{useLocation().pathname}</output>
}

/** Shell con las rutas pendientes reales y una pantalla de prueba con un campo (para ver que nada se desmonta). */
function renderShell(access: { permissions: string[]; modules: string[] } = { permissions: ['analytics.view'], modules: ['ANALYTICS'] }) {
  return render(
    <SessionContext.Provider value={SESSION}>
      <AccessProvider permissions={access.permissions} modules={access.modules}>
        <MemoryRouter initialEntries={['/test']}>
          <Routes>
            <Route element={<AppShell />}>
              <Route path="/test" element={<input aria-label="campo de prueba" />} />
              {appRoutes
                .filter((r) => r.pending)
                .map((r) => (
                  <Route key={r.path} path={r.path} element={<RouteGate route={r} />} />
                ))}
            </Route>
          </Routes>
          <Where />
        </MemoryRouter>
      </AccessProvider>
    </SessionContext.Provider>,
  )
}

describe('AppShell — cabecera', () => {
  beforeEach(() => {
    setLang('es')
    localStorage.removeItem(THEME_STORAGE_KEY)
  })
  afterEach(() => {
    closeCommandPalette()
    vi.useRealTimers()
  })

  it('el tema cambia data-theme en <html>, se guarda en localStorage y no desmonta la pantalla', async () => {
    const user = userEvent.setup()
    setTheme('dark')
    renderShell()
    const field = screen.getByLabelText('campo de prueba')
    await user.type(field, 'a medias')
    const theme = screen.getByRole('group', { name: 'Tema' })
    const light = within(theme).getByRole('button', { name: 'Claro' })
    const dark = within(theme).getByRole('button', { name: 'Oscuro' })
    expect(dark).toHaveAttribute('aria-pressed', 'true')

    await user.click(light)
    expect(document.documentElement.dataset.theme).toBe('light')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
    expect(light).toHaveAttribute('aria-pressed', 'true')
    expect(dark).toHaveAttribute('aria-pressed', 'false')
    // misma pantalla, mismo campo, lo escrito se conserva
    expect(screen.getByLabelText('campo de prueba')).toBe(field)
    expect(field).toHaveValue('a medias')

    await user.click(dark)
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
  })

  it('el reloj "en vivo" está presente con HH:MM:SS y avanza cada segundo', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 8, 28, 14, 5, 9))
    renderShell()
    const clock = screen.getByTestId('live-clock')
    expect(clock).toHaveTextContent('en vivo')
    expect(within(clock).getByText('14:05:09')).toBeInTheDocument()
    act(() => {
      vi.advanceTimersByTime(1000)
    })
    expect(within(clock).getByText('14:05:10')).toBeInTheDocument()
  })

  it('"/" abre la paleta con los ítems visibles; filtrar + Enter navega a la pantalla pendiente', async () => {
    const user = userEvent.setup()
    renderShell()
    await user.keyboard('/')
    const dialog = screen.getByRole('dialog', { name: 'Paleta de comandos' })
    // solo lo que el usuario puede ver (analytics.view + ANALYTICS), agrupado como el menú
    expect(within(dialog).getAllByRole('group').map((g) => g.querySelector('.pgrp')?.textContent)).toEqual(['Operación', 'Análisis'])
    expect(within(dialog).queryByText('Sala de despacho')).toBeNull()
    await user.keyboard('informes')
    await user.keyboard('{Enter}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getByTestId('where')).toHaveTextContent('/analytics/reports')
    expect(screen.getByRole('heading', { level: 1, name: 'Vistas e informes' })).toBeInTheDocument()

    // "Abrir otra pantalla" vuelve a abrir la paleta; Esc la cierra
    await user.click(screen.getByRole('button', { name: 'Abrir otra pantalla' }))
    expect(screen.getByRole('dialog', { name: 'Paleta de comandos' })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('la barra "Buscar o ejecutar…" y la lupa abren la misma paleta', async () => {
    const user = userEvent.setup()
    renderShell()
    await user.click(screen.getByRole('button', { name: 'Buscar o ejecutar…' }))
    expect(screen.getByRole('dialog', { name: 'Paleta de comandos' })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    await user.click(screen.getByRole('button', { name: 'Buscar o ejecutar' }))
    expect(screen.getByRole('dialog', { name: 'Paleta de comandos' })).toBeInTheDocument()
  })

  it('orden de la maqueta: reloj → compañía → tema → idioma → iniciales, sin el nombre completo visible', () => {
    const { container } = renderShell()
    const bar = container.querySelector('header.bar') as HTMLElement
    const kids = Array.from(bar.children)
    const pos = (sel: string) => kids.findIndex((el) => el.matches(sel))
    expect(pos('.cmd')).toBeLessThan(pos('.sp'))
    expect(pos('.sp')).toBeLessThan(pos('.live'))
    expect(pos('.live')).toBeLessThan(pos('.tenant'))
    expect(pos('.tenant')).toBeLessThan(pos('.theme-seg'))
    expect(pos('.theme-seg')).toBeLessThan(pos('.who'))
    expect(within(bar.querySelector('.tenant') as HTMLElement).getByText('Demo Logística')).toBeInTheDocument()

    const who = screen.getByRole('link', { name: 'Mi cuenta: Ana Admin' })
    expect(who).toHaveTextContent(/^AA$/)
    expect(within(bar).queryByText('Ana Admin')).toBeNull()
  })

  it('el menú muestra los grupos de la maqueta que el usuario puede ver', () => {
    renderShell({ permissions: ['admin.roles', 'devices.manage'], modules: ['SYSTEM', 'WMS_LOTSERIAL'] })
    const menu = screen.getByRole('complementary', { name: 'Menú principal' })
    expect(within(menu).getByRole('button', { name: 'Operación' })).toBeInTheDocument()
    expect(within(menu).getByRole('button', { name: 'Sistema' })).toBeInTheDocument()
    expect(within(menu).queryByRole('button', { name: 'Almacén' })).toBeNull()
    expect(within(menu).getByRole('link', { name: 'Roles y usuarios' })).toHaveAttribute('href', '/system/users')
    expect(within(menu).getByRole('link', { name: 'Aparatos móviles' })).toHaveAttribute('href', '/system/devices')
  })
})
