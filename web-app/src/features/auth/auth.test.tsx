// Login completo sobre la app montada: las ramas de AuthResultDto (ok, mfa_required; la compañía nunca se pregunta),
// error de credenciales bajo el formulario y `next` seguro (sin redirección abierta).
import { render, screen } from '@testing-library/react'
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
let onboardingReply: (path: string, req: Request) => Response | Promise<Response>

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
    onboardingReply = () => new Response(null, { status: 404 })
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
        if (path.startsWith('/api/v1/auth/onboarding')) return onboardingReply(path, req)
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
    // primera entrada al Pulso del archivo: carga diferida del módulo (Recharts, franja, filas fijas) → más de 1 s en frío
    expect(await screen.findByText('Bienvenido, Ana Admin', undefined, { timeout: 10_000 })).toBeInTheDocument()
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

  it('nunca pregunta la compañía: un solo login (sin tenantId) entra directo', async () => {
    loginReply = () => json({ status: 'ok', tokens: { ...TOKENS, tenantId: 2 } })
    await renderAppAt('/login')
    await submitLogin()
    expect(await screen.findByText('Bienvenido, Ana Admin')).toBeInTheDocument()
    expect(loginBodies).toHaveLength(1)
    expect(loginBodies[0]).toMatchObject({ tenantId: null })
  })

  it('primer ingreso: correo con código → contraseña propia → sigue al MFA (sin sesión hasta terminar)', async () => {
    const state = { email: 'a***@teikem.local', emailVerified: false, passwordChangeRequired: true, mfaConfigured: false }
    const calls: string[] = []
    loginReply = () => json({ status: 'onboarding_required', mfaChallengeToken: 'chal-1', mfaEnrollmentRequired: true, onboarding: state })
    onboardingReply = async (path, req) => {
      calls.push(`${path} ${req.headers.get('Authorization')}`)
      if (path.endsWith('/onboarding')) return json(state)
      if (path.endsWith('/email/send')) return json({ email: state.email, sent: true, devCode: '123456' })
      if (path.endsWith('/email/verify')) {
        const body = (await req.json()) as { code: string }
        if (body.code !== '123456') return json({ title: 'Datos inválidos.', status: 400, code: 'validation', errors: { code: ['El código no es válido o venció.'] } }, 400)
        return json({ state: { ...state, emailVerified: true }, mfaChallengeToken: null })
      }
      return json({ state: { ...state, emailVerified: true, passwordChangeRequired: false }, mfaChallengeToken: 'chal-2' })
    }
    await renderAppAt('/login')
    const user = await submitLogin()

    expect(await screen.findByText('Verifique su correo')).toBeInTheDocument()
    expect(screen.getByText('Paso 1 de 3')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Enviar código' }))
    expect(await screen.findByTestId('onboarding-dev-code')).toHaveTextContent('123456')
    await user.type(screen.getByLabelText('Código de verificación'), '123456')
    await user.click(screen.getByRole('button', { name: 'Verificar' }))

    expect(await screen.findByText('Ponga su propia contraseña')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Contraseña nueva'), 'corta')
    expect(screen.getByText('Use al menos 12 caracteres.')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Contraseña nueva'))
    await user.type(screen.getByLabelText('Contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.type(screen.getByLabelText('Repita la contraseña nueva'), 'Mi-Clave-Propia-2026')
    await user.click(screen.getByRole('button', { name: 'Guardar y seguir' }))

    // Paso 3: la pantalla de MFA enrola con el challenge nuevo que dio el cambio de contraseña.
    expect(await screen.findByText('Configure la verificación en dos pasos')).toBeInTheDocument()
    expect(calls.every((c) => c.includes('Bearer chal-1'))).toBe(true)
  })
})
