import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearTokens, setTokens } from '../kernel/auth/tokens'
import { setLang } from '../kernel/i18n/i18n'

const ME = {
  userId: 1,
  fullName: 'Ana Admin',
  email: 'admin@teikem.local',
  tenantId: 1,
  tenantName: 'Demo Logística',
  lang: 'es',
  memberships: [{ tenantId: 1, tenantName: 'Demo Logística', status: 'ACTIVE', isDefault: true }],
  permissions: ['analytics.view'],
  enabledModules: ['ANALYTICS'],
  mfaEnabled: false,
}

let me: typeof ME & { memberships: { tenantId: number; tenantName: string; status: string; isDefault: boolean }[] } = ME

async function renderAppAt(path: string) {
  window.history.pushState({}, '', path)
  vi.resetModules()
  const { default: App } = await import('./App')
  return render(<App />)
}

describe('App (shell)', () => {
  beforeEach(() => {
    setLang('es')
    me = ME
    vi.stubEnv('VITE_API_URL', 'http://api.test')
    vi.stubGlobal(
      'fetch',
      vi.fn(async (req: Request) =>
        new URL(req.url).pathname === '/api/v1/me'
          ? new Response(JSON.stringify(me), { status: 200, headers: { 'Content-Type': 'application/json' } })
          : new Response(null, { status: 404 }),
      ),
    )
  })
  afterEach(() => {
    clearTokens()
    // cada prueba monta módulos nuevos (resetModules) que leen el idioma guardado: se deja en español
    localStorage.setItem('teikem.lang', 'es')
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('sin sesión lleva al login', async () => {
    await renderAppAt('/')
    expect(await screen.findByRole('button', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('con sesión pinta el shell: tenant, usuario y menú filtrado', async () => {
    setTokens({ accessToken: 'a', refreshToken: 'r', tenantId: 1 })
    await renderAppAt('/')
    const menu = await screen.findByRole('complementary', { name: 'Menú principal' })
    expect(within(menu).getByText('Pulso del día')).toBeInTheDocument()
    expect(within(menu).queryByText('Administración')).toBeNull()
    expect(screen.getByText('Demo Logística')).toBeInTheDocument()
    expect(screen.getByText('Ana Admin')).toBeInTheDocument()
  })

  it('ruta desconocida dentro del shell: pantalla no encontrada', async () => {
    setTokens({ accessToken: 'a', refreshToken: 'r', tenantId: 1 })
    await renderAppAt('/no-existe')
    expect(await screen.findByText('Pantalla no encontrada')).toBeInTheDocument()
  })

  it('cambiar el idioma no reinicia la pantalla, el menú ni el estado de la pantalla; los textos cambian', async () => {
    setTokens({ accessToken: 'a', refreshToken: 'r', tenantId: 1 })
    const user = userEvent.setup()
    await renderAppAt('/account?tab=password')
    const current = await screen.findByLabelText(/Contraseña actual/)
    await user.type(current, 'escrito-a-medias')
    // el usuario cierra el grupo "Operación" del menú
    const group = screen.getByRole('button', { name: 'Operación' })
    expect(group).toHaveAttribute('aria-expanded', 'true')
    await user.click(group)
    expect(group).toHaveAttribute('aria-expanded', 'false')

    await user.click(screen.getByRole('button', { name: 'Idioma' }))
    await user.click(screen.getByRole('menuitemradio', { name: 'English' }))

    // textos en inglés
    expect(await screen.findByLabelText(/Current password/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Operations' })).toBeInTheDocument()
    // misma pantalla, misma pestaña, mismo grupo cerrado y lo escrito se conserva (nada se desmontó)
    expect(window.location.pathname).toBe('/account')
    expect(window.location.search).toBe('?tab=password')
    expect(screen.getByRole('button', { name: 'Operations' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByLabelText(/Current password/)).toBe(current)
    expect(current).toHaveValue('escrito-a-medias')
    await waitFor(() => expect(document.documentElement.lang).toBe('en'))
  })

  it('selector de compañía: solo membresías activas (una suspendida o invitada no se ofrece)', async () => {
    setTokens({ accessToken: 'a', refreshToken: 'r', tenantId: 1 })
    me = {
      ...ME,
      memberships: [
        { tenantId: 1, tenantName: 'Demo Logística', status: 'ACTIVE', isDefault: true },
        { tenantId: 2, tenantName: 'Suspendida SA', status: 'SUSPENDED', isDefault: false },
        { tenantId: 3, tenantName: 'Invitada SA', status: 'INVITED', isDefault: false },
      ],
    }
    await renderAppAt('/')
    expect(await screen.findByText('Demo Logística')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Compañía' })).toBeNull()
  })

  it('selector de compañía: con dos membresías activas lista solo esas', async () => {
    setTokens({ accessToken: 'a', refreshToken: 'r', tenantId: 1 })
    me = {
      ...ME,
      memberships: [
        { tenantId: 1, tenantName: 'Demo Logística', status: 'ACTIVE', isDefault: true },
        { tenantId: 2, tenantName: 'Suspendida SA', status: 'SUSPENDED', isDefault: false },
        { tenantId: 4, tenantName: 'Otra Activa', status: 'ACTIVE', isDefault: false },
      ],
    }
    await renderAppAt('/')
    const select = await screen.findByRole('combobox', { name: 'Compañía' })
    const names = within(select).getAllByRole('option').map((o) => o.textContent)
    expect(names).toEqual(['Demo Logística', 'Otra Activa'])
  })
})
