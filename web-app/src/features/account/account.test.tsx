import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import { ReauthContext } from '../../kernel/auth/reauthContext'
import { setLang } from '../../kernel/i18n/i18n'
import AccountPage from './AccountPage'
import { describeDevice, formatDateTime } from './format'
import { MfaTab } from './MfaTab'
import { PasswordTab } from './PasswordTab'
import { SessionsTab } from './SessionsTab'

// Cliente de la app sobre un fetch simulado (misma política que el real).
type Handler = (method: string, url: URL, body: unknown) => Response | unknown
const mock = vi.hoisted(() => ({ calls: [] as { method: string; path: string; body: unknown }[], handler: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.text()
    const body: unknown = text ? JSON.parse(text) : undefined
    mock.calls.push({ method: req.method, path: url.pathname, body })
    const result = (mock.handler as Handler)(req.method, url, body)
    if (result instanceof Response) return result
    if (result === undefined) return new Response(null, { status: 204 })
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, body: object) =>
  new Response(JSON.stringify({ status, ...body }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const ME: MeDto = {
  userId: 1,
  fullName: 'Ana Admin',
  email: 'admin@teikem.local',
  tenantId: 1,
  tenantName: 'Demo Logística',
  lang: 'es',
  memberships: [{ tenantId: 1, tenantName: 'Demo Logística', status: 'ACTIVE', isDefault: true }],
  permissions: [],
  enabledModules: [],
  mfaEnabled: false,
}

function session(me: MeDto, reloadMe = vi.fn(async () => {})): Session {
  return {
    me,
    isAuthenticated: true,
    isLoading: false,
    error: null,
    tenantId: me.tenantId ?? null,
    lang: 'es',
    setLang: vi.fn(),
    logout: vi.fn(async () => {}),
    switchTenant: vi.fn(async () => {}),
    permissions: new Set(),
    modules: new Set(),
    reloadMe,
  }
}

function wrap(ui: ReactNode, opts: { me?: MeDto; reauth?: () => Promise<boolean>; reloadMe?: () => Promise<void>; path?: string } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const value = session(opts.me ?? ME, vi.fn(opts.reloadMe ?? (async () => {})))
  return render(
    <MemoryRouter initialEntries={[opts.path ?? '/account']}>
      <QueryClientProvider client={client}>
        <SessionContext.Provider value={value}>
          <ReauthContext.Provider value={{ reauth: opts.reauth ?? (async () => true) }}>{ui}</ReauthContext.Provider>
        </SessionContext.Provider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.handler = (() => new Response(null, { status: 404 })) satisfies Handler
})

describe('format', () => {
  it('describe el dispositivo por navegador y sistema', () => {
    expect(describeDevice('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36')).toBe('Chrome · Windows')
    expect(describeDevice('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile Safari/604.1')).toBe('Safari · iOS')
    expect(describeDevice('Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0')).toBe('Firefox · Linux')
    expect(describeDevice('Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/126.0 Safari/537.36 Edg/126.0')).toBe('Edge · Windows')
    expect(describeDevice('curl/8.0')).toBe('curl/8.0')
    expect(describeDevice('  ')).toBeNull()
    expect(describeDevice(null)).toBeNull()
  })

  it('formatea fechas y tolera vacíos o inválidas', () => {
    expect(formatDateTime(null, 'es')).toBe('')
    expect(formatDateTime('no-es-fecha', 'es')).toBe('')
    expect(formatDateTime('2026-09-27T10:00:00Z', 'es')).toMatch(/2026/)
  })
})

describe('AccountPage', () => {
  it('muestra el perfil y cambia de pestaña', async () => {
    const user = userEvent.setup()
    mock.handler = ((method: string, url: URL) => (method === 'GET' && url.pathname === '/api/v1/auth/sessions' ? [] : undefined)) satisfies Handler
    wrap(<AccountPage />)
    expect(screen.getByRole('heading', { name: 'Mi cuenta' })).toBeInTheDocument()
    expect(screen.getAllByText('admin@teikem.local').length).toBeGreaterThan(0)
    expect(screen.getByText('Predeterminada')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Contraseña' }))
    expect(screen.getByLabelText(/Contraseña actual/)).toBeInTheDocument()
  })

  it('abre la pestaña indicada en ?tab=', () => {
    wrap(<AccountPage />, { path: '/account?tab=mfa' })
    expect(screen.getByRole('tab', { name: 'Verificación en dos pasos' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('button', { name: 'Activar' })).toBeInTheDocument()
  })
})

describe('PasswordTab', () => {
  it('valida en cliente que las contraseñas coincidan', async () => {
    const user = userEvent.setup()
    wrap(<PasswordTab />)
    await user.type(screen.getByLabelText(/Contraseña actual/), 'Actual_2026!')
    await user.type(screen.getByLabelText(/^Nueva contraseña/), 'Nueva_Clave_2026!')
    await user.type(screen.getByLabelText(/Repita/), 'Otra_Clave_2026!')
    await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }))
    expect(await screen.findByText('Las contraseñas no coinciden.')).toBeInTheDocument()
    expect(mock.calls).toHaveLength(0)
  })

  it('envía PUT /auth/password y pone el error del servidor bajo el campo', async () => {
    const user = userEvent.setup()
    mock.handler = (() => problem(400, { title: 'Datos inválidos.', code: 'validation', errors: { newPassword: ['Incorrect password.'] } })) satisfies Handler
    wrap(<PasswordTab />)
    await user.type(screen.getByLabelText(/Contraseña actual/), 'Mala_2026!')
    await user.type(screen.getByLabelText(/^Nueva contraseña/), 'Nueva_Clave_2026!')
    await user.type(screen.getByLabelText(/Repita/), 'Nueva_Clave_2026!')
    await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }))
    expect(await screen.findByText('Incorrect password.')).toBeInTheDocument()
    expect(screen.getByLabelText(/^Nueva contraseña/)).toHaveAttribute('aria-invalid', 'true')
    expect(mock.calls[0]).toEqual({
      method: 'PUT',
      path: '/api/v1/auth/password',
      body: { currentPassword: 'Mala_2026!', newPassword: 'Nueva_Clave_2026!' },
    })
  })
})

describe('MfaTab', () => {
  const ENROLL = { secret: 'JBSWY3DPEHPK3PXP', otpAuthUri: 'otpauth://totp/Teikem:admin%40teikem.local?secret=JBSWY3DPEHPK3PXP&issuer=Teikem' }

  it('activar: muestra clave y URI, un código inválido muestra el error del API y cancelar deja MFA desactivado', async () => {
    const user = userEvent.setup()
    mock.handler = ((_m: string, url: URL) => {
      if (url.pathname === '/api/v1/auth/mfa/totp/enroll') return ENROLL
      return problem(400, { title: 'Datos inválidos.', code: 'validation', errors: { code: ['Código inválido.'] } })
    }) satisfies Handler
    wrap(<MfaTab />)
    await user.click(screen.getByRole('button', { name: 'Activar' }))
    expect(await screen.findByTestId('mfa-secret')).toHaveTextContent(ENROLL.secret)
    expect(screen.getByTestId('mfa-uri')).toHaveTextContent(ENROLL.otpAuthUri)

    await user.type(screen.getByLabelText(/Código de verificación/), '123456')
    await user.click(screen.getByRole('button', { name: 'Confirmar y activar' }))
    expect(await screen.findByText('Código inválido.')).toBeInTheDocument()
    expect(mock.calls.at(-1)).toEqual({ method: 'POST', path: '/api/v1/auth/mfa/totp/confirm', body: { code: '123456' } })

    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    expect(screen.getByText('Desactivada')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Activar' })).toBeInTheDocument()
  })

  it('confirmar con código válido muestra los códigos de recuperación una sola vez y recarga me', async () => {
    const user = userEvent.setup()
    const reloadMe = vi.fn(async () => {})
    mock.handler = ((_m: string, url: URL) =>
      url.pathname === '/api/v1/auth/mfa/totp/enroll' ? ENROLL : { recoveryCodes: ['AAAA-1111', 'BBBB-2222'] }) satisfies Handler
    wrap(<MfaTab />, { reloadMe })
    await user.click(screen.getByRole('button', { name: 'Activar' }))
    await user.type(await screen.findByLabelText(/Código de verificación/), '654321')
    await user.click(screen.getByRole('button', { name: 'Confirmar y activar' }))
    const codes = await screen.findByTestId('recovery-codes')
    expect(within(codes).getByText('AAAA-1111')).toBeInTheDocument()
    expect(reloadMe).toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Ya los guardé' }))
    expect(screen.queryByTestId('recovery-codes')).toBeNull()
  })

  it('desactivar pide reautenticación; si se cancela no llama al API', async () => {
    const user = userEvent.setup()
    const reauth = vi.fn(async () => false)
    wrap(<MfaTab />, { me: { ...ME, mfaEnabled: true }, reauth })
    await user.click(screen.getByRole('button', { name: 'Desactivar' }))
    expect(reauth).toHaveBeenCalled()
    expect(mock.calls).toHaveLength(0)
  })

  it('desactivar tras reautenticarse llama DELETE /mfa/totp y recarga me', async () => {
    const user = userEvent.setup()
    const reloadMe = vi.fn(async () => {})
    mock.handler = (() => undefined) satisfies Handler
    wrap(<MfaTab />, { me: { ...ME, mfaEnabled: true }, reloadMe })
    await user.click(screen.getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(reloadMe).toHaveBeenCalled())
    expect(mock.calls).toEqual([{ method: 'DELETE', path: '/api/v1/auth/mfa/totp', body: undefined }])
  })
})

describe('SessionsTab', () => {
  const SESSIONS = [
    { id: 10, deviceInfo: 'Mozilla/5.0 (Windows NT 10.0) Chrome/126.0 Safari/537.36', issuedAtUtc: '2026-09-27T10:00:00Z', expiresAtUtc: '2026-10-27T10:00:00Z', isCurrent: true },
    { id: 11, deviceInfo: 'Mozilla/5.0 (X11; Linux x86_64) Firefox/128.0', issuedAtUtc: '2026-09-20T10:00:00Z', expiresAtUtc: '2026-10-20T10:00:00Z', isCurrent: false },
  ]

  it('lista las sesiones; la actual no se revoca y otra se revoca con DELETE', async () => {
    const user = userEvent.setup()
    mock.handler = ((method: string, url: URL) => {
      if (method === 'GET' && url.pathname === '/api/v1/auth/sessions') return SESSIONS
      if (method === 'DELETE' && url.pathname === '/api/v1/auth/sessions/11') return undefined
      return new Response(null, { status: 404 })
    }) satisfies Handler
    wrap(<SessionsTab />)
    expect((await screen.findAllByText('Esta sesión')).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/Firefox · Linux/).length).toBeGreaterThan(0)
    const revokeButtons = screen.getAllByRole('button', { name: 'Cerrar sesión' })
    expect(revokeButtons).toHaveLength(1)
    await user.click(revokeButtons[0])
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar sesión' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/auth/sessions/11')).toBe(true))
  })
})
