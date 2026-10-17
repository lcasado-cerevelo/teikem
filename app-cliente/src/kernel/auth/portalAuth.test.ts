// Flujo de acceso del portal con un fetch simulado: lo que se manda (marca de la computadora y «No confiar») y cada resultado posible.
jest.mock('expo-crypto', () => ({ getRandomBytes: (n: number) => new Uint8Array(n).fill(171) }))

import { resetBrowserTokenCache } from './browserToken'
import { cancelMfa, confirmMfa, enrollMfa, getChallenge, login, logout, verifyMfa } from './portalAuth'
import { setNoTrust } from './preferences'
import { clearSession, getSessionState } from './session'

type Call = { method: string; path: string; body: unknown; auth: string | null }
let calls: Call[]
let reply: (path: string) => unknown

const PAIR = { accessToken: 'acc', accessExpiresAtUtc: '2026-10-17T10:00:00Z', refreshToken: 'ref', refreshExpiresAtUtc: '2026-11-17T10:00:00Z', tenantId: 1 }

beforeEach(async () => {
  calls = []
  reply = () => ({ status: 'ok', tokens: PAIR })
  resetBrowserTokenCache()
  await clearSession()
  cancelMfa()
  await setNoTrust(false)
  globalThis.fetch = jest.fn(async (req: Request) => {
    const path = new URL(req.url).pathname
    const text = await req.clone().text()
    calls.push({ method: req.method, path, body: text ? JSON.parse(text) : undefined, auth: req.headers.get('Authorization') })
    const r = reply(path)
    return new Response(JSON.stringify(r), { status: (r as { _status?: number })._status ?? 200, headers: { 'Content-Type': 'application/json' } })
  }) as unknown as typeof fetch
})

describe('login del portal', () => {
  it('manda la marca de la computadora y la casilla «No confiar», y guarda la sesión', async () => {
    const outcome = await login('  ana@cliente.co ', 'Clave-1234')
    expect(outcome).toEqual({ status: 'ok' })
    const body = calls[0].body as Record<string, unknown>
    expect(calls[0].path).toBe('/api/v1/portal/auth/login')
    expect(body.email).toBe('ana@cliente.co')
    expect(body.trustedBrowserToken).toMatch(/^[0-9a-f]{64}$/)
    expect(body.doNotTrust).toBe(false)
    expect(getSessionState().session?.accessToken).toBe('acc')
  })

  it('la casilla recordada viaja en el login y la marca de la computadora es siempre la misma', async () => {
    await setNoTrust(true)
    await login('ana@cliente.co', 'x')
    await login('ana@cliente.co', 'x')
    expect((calls[0].body as { doNotTrust: boolean }).doNotTrust).toBe(true)
    expect((calls[1].body as { trustedBrowserToken: string }).trustedBrowserToken).toBe((calls[0].body as { trustedBrowserToken: string }).trustedBrowserToken)
  })

  it('computadora nueva: no hay sesión y se devuelve el aviso del correo', async () => {
    reply = () => ({ status: 'browser_confirmation_required', browser: { email: 'a***@cliente.co', sent: true } })
    const outcome = await login('ana@cliente.co', 'x')
    expect(outcome.status).toBe('browser_confirmation_required')
    expect(getSessionState().session).toBeNull()
  })

  it('reto anti-robot: se avisa y no hay sesión; con el token del reto se manda en captchaToken', async () => {
    reply = () => ({ status: 'captcha_required' })
    expect(await login('ana@cliente.co', 'x')).toEqual({ status: 'captcha_required' })
    await login('ana@cliente.co', 'x', 'tok-cf')
    expect((calls[1].body as { captchaToken: string }).captchaToken).toBe('tok-cf')
  })

  it('un 401 llega como ApiError con el mensaje del servidor', async () => {
    reply = () => ({ title: 'Credenciales inválidas.', status: 401, _status: 401 })
    await expect(login('ana@cliente.co', 'mala')).rejects.toMatchObject({ message: 'Credenciales inválidas.', status: 401 })
  })
})

describe('segundo paso (MFA)', () => {
  it('el challenge se usa como Bearer en verificar, y al terminar queda la sesión y se limpia el challenge', async () => {
    reply = (path) => (path.endsWith('/portal/auth/login') ? { status: 'mfa_required', mfaChallengeToken: 'chal', mfaEnrollmentRequired: false } : { status: 'ok', tokens: PAIR })
    expect(await login('ana@cliente.co', 'x')).toEqual({ status: 'mfa_required', enrollmentRequired: false })
    expect(getChallenge()?.token).toBe('chal')
    expect(await verifyMfa('123456')).toEqual({ status: 'ok' })
    const verify = calls.find((c) => c.path === '/api/v1/auth/mfa/verify')!
    expect(verify.auth).toBe('Bearer chal')
    expect((verify.body as { trustedBrowserToken: string }).trustedBrowserToken).toMatch(/^[0-9a-f]{64}$/)
    expect(getChallenge()).toBeNull()
    expect(getSessionState().session?.refreshToken).toBe('ref')
  })

  it('la primera vez pide inscribir el MFA: enrolar y confirmar usan el challenge y devuelven los códigos de recuperación', async () => {
    reply = (path) => {
      if (path.endsWith('/portal/auth/login')) return { status: 'mfa_required', mfaChallengeToken: 'chal', mfaEnrollmentRequired: true }
      if (path.endsWith('/totp/enroll')) return { secret: 'ABCDEF', otpAuthUri: 'otpauth://totp/x' }
      return { recoveryCodes: ['aaaa-1', 'bbbb-2'] }
    }
    expect(await login('ana@cliente.co', 'x')).toEqual({ status: 'mfa_required', enrollmentRequired: true })
    expect((await enrollMfa()).secret).toBe('ABCDEF')
    expect(await confirmMfa('123456')).toEqual(['aaaa-1', 'bbbb-2'])
    expect(calls.filter((c) => c.path.includes('/totp/')).every((c) => c.auth === 'Bearer chal')).toBe(true)
  })
})

describe('cerrar sesión', () => {
  it('revoca el refresh token en el servidor y borra la sesión local, aunque no haya red', async () => {
    await login('ana@cliente.co', 'x')
    await logout()
    expect(calls.at(-1)).toMatchObject({ path: '/api/v1/auth/logout', body: { refreshToken: 'ref' } })
    expect(getSessionState().session).toBeNull()
    await login('ana@cliente.co', 'x')
    globalThis.fetch = jest.fn(async () => {
      throw new Error('sin red')
    }) as unknown as typeof fetch
    await logout()
    expect(getSessionState().session).toBeNull()
  })
})
