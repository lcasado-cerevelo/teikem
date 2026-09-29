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
/** Respuesta propia para otras rutas del API (por defecto 404). */
let other: (path: string) => Response = () => new Response(null, { status: 404 })

function problem(status: number, code: string, title: string): Response {
  return new Response(JSON.stringify({ status, code, title }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

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
    other = () => new Response(null, { status: 404 })
    vi.stubEnv('VITE_API_URL', 'http://api.test')
    vi.stubGlobal(
      'fetch',
      vi.fn(async (req: Request) =>
        new URL(req.url).pathname === '/api/v1/me'
          ? new Response(JSON.stringify(me), { status: 200, headers: { 'Content-Type': 'application/json' } })
          : other(new URL(req.url).pathname),
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
    expect(within(menu).queryByText('Sistema')).toBeNull()
    expect(screen.getByText('Demo Logística')).toBeInTheDocument()
    // el usuario se muestra solo con sus iniciales (maqueta); el nombre completo queda en el nombre accesible
    expect(screen.getByRole('link', { name: 'Mi cuenta: Ana Admin' })).toHaveTextContent('AA')
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
    // la pantalla es de carga diferida: con la máquina ocupada (npm run check) puede pasar del segundo por defecto
    const current = await screen.findByLabelText(/Contraseña actual/, undefined, { timeout: 4000 })
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

  it('una lectura con 403 module_disabled lleva a "Módulo apagado" (y forbidden a "Sin permiso")', async () => {
    setTokens({ accessToken: 'a', refreshToken: 'r', tenantId: 1 })
    other = (path) => (path === '/api/v1/auth/sessions' ? problem(403, 'module_disabled', 'Módulo apagado.') : new Response(null, { status: 404 }))
    await renderAppAt('/account?tab=sessions')
    expect(await screen.findByTestId('module-off-screen')).toBeInTheDocument()
    expect(window.location.pathname).toBe('/module-off')
  })

  it('una lectura con 403 forbidden lleva a "Sin permiso"', async () => {
    setTokens({ accessToken: 'a', refreshToken: 'r', tenantId: 1 })
    other = (path) => (path === '/api/v1/auth/sessions' ? problem(403, 'forbidden', 'Sin permiso.') : new Response(null, { status: 404 }))
    await renderAppAt('/account?tab=sessions')
    expect(await screen.findByTestId('forbidden-screen')).toBeInTheDocument()
    expect(window.location.pathname).toBe('/forbidden')
  })
})
