// Marca Teikem en el shell, la carga a pantalla completa y las pantallas sin sesión (Lote F8a P7).
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AuthLayout } from '../features/auth/AuthLayout'
import { AccessProvider } from '../kernel/access'
import { setLang } from '../kernel/i18n/i18n'
import { closeCommandPalette } from '../kernel/ui/commandPaletteStore'
import { setTheme, THEME_STORAGE_KEY } from '../kernel/ui/theme'
import { AppShell } from './AppShell'
import { SessionContext, type Session } from './session'
import { Splash } from './Splash'

const COLLAPSED_KEY = 'teikem.rail.collapsed'

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

function renderShell() {
  return render(
    <SessionContext.Provider value={SESSION}>
      <AccessProvider permissions={['analytics.view']} modules={['ANALYTICS']}>
        <MemoryRouter initialEntries={['/test']}>
          <Routes>
            <Route element={<AppShell />}>
              <Route path="/test" element={<input aria-label="campo de prueba" />} />
            </Route>
          </Routes>
        </MemoryRouter>
      </AccessProvider>
    </SessionContext.Provider>,
  )
}

describe('Marca Teikem en la interfaz', () => {
  beforeEach(() => {
    setLang('es')
    setTheme('dark')
    localStorage.removeItem(COLLAPSED_KEY)
  })
  afterEach(() => {
    closeCommandPalette()
    setLang('es')
    setTheme('dark')
    localStorage.removeItem(THEME_STORAGE_KEY)
    localStorage.removeItem(COLLAPSED_KEY)
  })

  it('barra lateral: lockup por idioma y tema sin desmontar; al colapsar aparece la marca cuadrada de 44 px', async () => {
    const user = userEvent.setup()
    renderShell()
    const home = screen.getByRole('link', { name: 'Teikem' })
    expect(home).toHaveAttribute('href', '/')
    const lockup = within(home).getByTestId('brand-lockup')
    expect(lockup).toHaveAttribute('src', '/brand/teikem-1b-horizontal-tagline-es-inv.svg')
    expect(lockup).toHaveClass('brand-full')
    expect(within(home).queryByTestId('brand-mark')).toBeNull()

    // idioma y tema desde el shell: el mismo <img> cambia de archivo y la pantalla sigue montada
    const field = screen.getByLabelText('campo de prueba')
    act(() => setLang('en'))
    expect(lockup).toHaveAttribute('src', '/brand/teikem-1a-horizontal-tagline-en-inv.svg')
    await user.click(within(screen.getByRole('group', { name: 'Theme' })).getByRole('button', { name: 'Light' }))
    expect(within(home).getByTestId('brand-lockup')).toBe(lockup)
    expect(lockup).toHaveAttribute('src', '/brand/teikem-1a-horizontal-tagline-en.svg')
    expect(screen.getByLabelText('campo de prueba')).toBe(field)

    await user.click(screen.getByRole('button', { name: 'Collapse' }))
    const mark = within(home).getByTestId('brand-mark')
    expect(mark).toHaveAttribute('src', '/brand/teikem-symbol.svg')
    expect(mark).toHaveAttribute('width', '44')
    expect(mark).toHaveClass('brand-mini')
    // en el cajón móvil la barra colapsada muestra el lockup (CSS): sigue en el DOM
    expect(within(home).getByTestId('brand-lockup')).toBe(lockup)

    await user.click(screen.getByRole('button', { name: 'Expand' }))
    expect(within(home).queryByTestId('brand-mark')).toBeNull()
  })

  it('carga a pantalla completa: lockup centrado arriba; la carga dentro del shell no lo lleva', () => {
    const { unmount } = render(<Splash full />)
    expect(within(screen.getByRole('status')).getByTestId('brand-lockup')).toHaveAttribute(
      'src',
      '/brand/teikem-1b-horizontal-tagline-es-inv.svg',
    )
    unmount()
    render(<Splash />)
    expect(screen.queryByTestId('brand-lockup')).toBeNull()
  })

  it('login, MFA y selección de compañía (AuthLayout): lockup arriba del título, por idioma y tema', () => {
    setTheme('light')
    render(
      <SessionContext.Provider value={SESSION}>
        <AuthLayout title="Entrar">
          <p>formulario</p>
        </AuthLayout>
      </SessionContext.Provider>,
    )
    const lockup = screen.getByRole('img', { name: 'Teikem' })
    expect(lockup).toHaveAttribute('src', '/brand/teikem-1b-horizontal-tagline-es.svg')
    expect(lockup.closest('.auth-brand')).not.toBeNull()
    // va antes del título
    expect(lockup.compareDocumentPosition(screen.getByRole('heading', { name: 'Entrar' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    act(() => setLang('en'))
    expect(lockup).toHaveAttribute('src', '/brand/teikem-1a-horizontal-tagline-en.svg')
  })
})
