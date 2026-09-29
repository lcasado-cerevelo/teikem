// Login completo sobre la app montada: las tres ramas de AuthResultDto (ok, mfa_required, tenant_selection),
// error de credenciales bajo el formulario y `next` seguro (sin redirección abierta).
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearTokens, setMfaChallenge } from '../../kernel/auth/tokens'
import { setLang } from '../../kernel/i18n/i18n'
import { safeNext } from './next'

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
const TOKENS = { accessToken: 'a', refreshToken: 'r', tenantId: 1 }

type LoginReply = (body: { email: string; password: string; tenantId: number | null }) => Response
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })

let loginReply: LoginReply
let loginBodies: { email: string; password: string; tenantId: number | null }[]
let mfaReply: () => Response

async function renderAppAt(path: string) {
  window.history.pushState({}, '', path)
  vi.resetModules()
  const { default: App } = await import('../../app/App')
  return render(<App />)
}

async function submitLogin(email = 'admin@teikem.local', password = 'Teikem_Admin_2026!') {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Correo electrónico'), email)
  await user.type(screen.getByLabelText('Contraseña'), password)
  await user.click(screen.getByRole('button', { name: 'Entrar' }))
  return user
}

describe('safeNext', () => {
  it('solo acepta rutas internas', () => {
    expect(safeNext('/account')).toBe('/account')
    expect(safeNext(null)).toBe('/')
    expect(safeNext('//evil.example')).toBe('/')
    expect(safeNext('https://evil.example')).toBe('/')
  })
})

describe('Login (AuthResultDto)', () => {
  beforeEach(() => {
    setLang('es')
    clearTokens()
    setMfaChallenge(null)
    loginBodies = []
    loginReply = () => json({ status: 'ok', tokens: TOKENS })
    mfaReply = () => json({ title: 'El código no es válido.', status: 401, code: 'unauthorized' }, 401)
    vi.stubEnv('VITE_API_URL', 'http://api.test')
    vi.stubGlobal(
      'fetch',
      vi.fn(async (req: Request) => {
        const path = new URL(req.url).pathname
        if (path === '/api/v1/auth/login') {
          const body = await req.json()
          loginBodies.push(body)
          return loginReply(body)
        }
        if (path === '/api/v1/auth/mfa/verify') return mfaReply()
        if (path === '/api/v1/me') return json(ME)
        if (path === '/api/v1/analytics/pulse') return json({ indicators: [], charts: [], panels: [] })
        return new Response(null, { status: 404 })
      }),
    )
  })
  afterEach(() => {
    clearTokens()
    setMfaChallenge(null)
    // cada prueba monta módulos nuevos (resetModules) que leen el idioma guardado: se deja en español
    localStorage.setItem('teikem.lang', 'es')
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('credenciales inválidas: el mensaje del API queda sobre el formulario y no se sale del login', async () => {
    loginReply = () => json({ title: 'Correo o contraseña incorrectos.', status: 401, code: 'unauthorized' }, 401)
    await renderAppAt('/login')
    await submitLogin('admin@teikem.local', 'mala')
    expect(await screen.findByRole('alert')).toHaveTextContent('Correo o contraseña incorrectos.')
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('ok: entra al destino pedido (next)', async () => {
    await renderAppAt('/login?next=%2F')
    await submitLogin()
    expect(await screen.findByText('Bienvenido, Ana Admin')).toBeInTheDocument()
    expect(loginBodies[0]).toMatchObject({ email: 'admin@teikem.local', tenantId: null })
  })

  it('ok con next externo: va al inicio (sin redirección abierta)', async () => {
    await renderAppAt('/login?next=%2F%2Fevil.example')
    await submitLogin()
    expect(await screen.findByText('Bienvenido, Ana Admin')).toBeInTheDocument()
    expect(window.location.pathname).toBe('/')
  })

  it('mfa_required: pide el código; uno inválido muestra el error del API; volver deja la sesión cerrada', async () => {
    loginReply = () => json({ status: 'mfa_required', mfaChallengeToken: 'chal', mfaEnrollmentRequired: false })
    await renderAppAt('/login')
    const user = await submitLogin()
    expect(await screen.findByText('Verificación en dos pasos')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Código de verificación'), '000000')
    await user.click(screen.getByRole('button', { name: 'Verificar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('El código no es válido.')
    await user.click(screen.getByRole('button', { name: 'Volver al inicio de sesión' }))
    expect(await screen.findByRole('button', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('mfa_required con enrolamiento obligatorio: ofrece configurar el segundo factor', async () => {
    loginReply = () => json({ status: 'mfa_required', mfaChallengeToken: 'chal', mfaEnrollmentRequired: true })
    await renderAppAt('/login')
    await submitLogin()
    expect(await screen.findByText('Configure la verificación en dos pasos')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Comenzar configuración' })).toBeInTheDocument()
  })

  it('tenant_selection: lista las compañías y repite el login con la elegida', async () => {
    loginReply = (body) =>
      body.tenantId == null
        ? json({
            status: 'tenant_selection',
            tenants: [
              { tenantId: 1, name: 'Demo Logística', isDefault: true },
              { tenantId: 2, name: 'Otra Compañía', isDefault: false },
            ],
          })
        : json({ status: 'ok', tokens: { ...TOKENS, tenantId: body.tenantId } })
    await renderAppAt('/login')
    const user = await submitLogin()
    expect(await screen.findByText('Elija la compañía')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Otra Compañía/ }))
    expect(await screen.findByText('Bienvenido, Ana Admin')).toBeInTheDocument()
    await waitFor(() => expect(loginBodies).toHaveLength(2))
    expect(loginBodies[1]).toMatchObject({ email: 'admin@teikem.local', password: 'Teikem_Admin_2026!', tenantId: 2 })
  })
})
