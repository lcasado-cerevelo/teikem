// Flujo de autenticación contra AuthController (/api/v1/auth/*).
import { api, unwrap } from '../api/client'
import type { components } from '../api/schema'
import { clearTokens, getMfaChallenge, getRefreshToken, setMfaChallenge, setTokens } from './tokens'

type AuthResultDto = components['schemas']['AuthResultDto']
type TokenPairDto = components['schemas']['TokenPairDto']
export type TenantOptionDto = components['schemas']['TenantOptionDto']
export type MfaEnrollResultDto = components['schemas']['MfaEnrollResultDto']
export type OnboardingStateDto = components['schemas']['OnboardingStateDto']
export type OnboardingEmailSentDto = components['schemas']['OnboardingEmailSentDto']

export type LoginOutcome =
  | { status: 'ok' }
  | { status: 'mfa_required'; enrollmentRequired: boolean }
  | { status: 'onboarding_required'; onboarding: OnboardingStateDto }

function deviceInfo(): string | undefined {
  return globalThis.navigator?.userAgent?.slice(0, 200)
}

/** Guarda el par de tokens emitido por el API. */
export function storeTokenPair(pair: TokenPairDto | undefined): void {
  if (!pair?.accessToken || !pair.refreshToken) throw new Error('Respuesta de autenticación sin tokens.')
  setTokens({
    accessToken: pair.accessToken,
    accessExpiresAtUtc: pair.accessExpiresAtUtc,
    refreshToken: pair.refreshToken,
    refreshExpiresAtUtc: pair.refreshExpiresAtUtc,
    tenantId: pair.tenantId ?? 0,
  })
}

function handleResult(result: AuthResultDto): LoginOutcome {
  // Primer ingreso pendiente (2026-09-30): el challenge sirve para /auth/onboarding/* y para enrolar el MFA al final.
  if (result.status === 'onboarding_required' && result.mfaChallengeToken && result.onboarding) {
    setMfaChallenge({ token: result.mfaChallengeToken, enrollmentRequired: result.mfaEnrollmentRequired ?? true })
    return { status: 'onboarding_required', onboarding: result.onboarding }
  }
  if (result.status === 'mfa_required' && result.mfaChallengeToken) {
    setMfaChallenge({ token: result.mfaChallengeToken, enrollmentRequired: result.mfaEnrollmentRequired ?? false })
    return { status: 'mfa_required', enrollmentRequired: result.mfaEnrollmentRequired ?? false }
  }
  storeTokenPair(result.tokens)
  setMfaChallenge(null)
  return { status: 'ok' }
}

/** Paso 1 del login. Lanza ApiError (401 credenciales inválidas, 403 sin compañía activa…). Nunca pregunta la compañía: el
 *  API entra a la predeterminada (o la primera por nombre) y se cambia desde el selector de la cabecera. */
export async function login(email: string, password: string): Promise<LoginOutcome> {
  const result = await unwrap(
    api.POST('/api/v1/auth/login', { body: { email, password, tenantId: null, deviceInfo: deviceInfo() } }),
  )
  return handleResult(result)
}

function challengeHeaders(): { Authorization: string } {
  const challenge = getMfaChallenge()
  if (!challenge) throw new Error('No hay un challenge MFA pendiente.')
  return { Authorization: `Bearer ${challenge.token}` }
}

/** Paso 2 del login: código TOTP o de recuperación con el challenge token. */
export async function verifyMfa(code: string): Promise<LoginOutcome> {
  const result = await unwrap(
    api.POST('/api/v1/auth/mfa/verify', { body: { code, deviceInfo: deviceInfo() }, headers: challengeHeaders() }),
  )
  return handleResult(result)
}

/** Enrolamiento obligatorio (el tenant exige MFA y el usuario no lo tiene): secreto y URI otpauth. */
export async function enrollMfaWithChallenge(): Promise<MfaEnrollResultDto> {
  return unwrap(api.POST('/api/v1/auth/mfa/totp/enroll', { headers: challengeHeaders() }))
}

/** Confirma el enrolamiento con un código; devuelve los códigos de recuperación (se muestran una sola vez). */
export async function confirmMfaWithChallenge(code: string): Promise<string[]> {
  const result = await unwrap(api.POST('/api/v1/auth/mfa/totp/confirm', { body: { code }, headers: challengeHeaders() }))
  return result.recoveryCodes ?? []
}

/** Primer ingreso: qué falta (correo, contraseña, MFA). */
export async function getOnboarding(): Promise<OnboardingStateDto> {
  return unwrap(api.GET('/api/v1/auth/onboarding', { headers: challengeHeaders() }))
}

/** Primer ingreso, paso 1a: manda al correo un código de 6 dígitos. */
export async function sendOnboardingEmail(): Promise<OnboardingEmailSentDto> {
  return unwrap(api.POST('/api/v1/auth/onboarding/email/send', { headers: challengeHeaders() }))
}

/** Primer ingreso, paso 1b: confirma el código del correo. */
export async function verifyOnboardingEmail(code: string): Promise<OnboardingStateDto> {
  const step = await unwrap(api.POST('/api/v1/auth/onboarding/email/verify', { body: { code }, headers: challengeHeaders() }))
  return step.state
}

/** Primer ingreso, paso 2: contraseña propia. El sello de seguridad rota: se guarda el challenge nuevo para el MFA. */
export async function setOnboardingPassword(newPassword: string): Promise<OnboardingStateDto> {
  const step = await unwrap(api.POST('/api/v1/auth/onboarding/password', { body: { newPassword }, headers: challengeHeaders() }))
  if (step.mfaChallengeToken) setMfaChallenge({ token: step.mfaChallengeToken, enrollmentRequired: !step.state.mfaConfigured })
  return step.state
}

export function cancelMfa(): void {
  setMfaChallenge(null)
}

/** Cierra la sesión en el servidor (revoca el refresh token) y limpia los tokens locales. Nunca lanza. */
export async function logout(): Promise<void> {
  const refreshToken = getRefreshToken()
  if (refreshToken) {
    try {
      await api.POST('/api/v1/auth/logout', { body: { refreshToken } })
    } catch {
      // sin red: igual se limpia la sesión local
    }
  }
  clearTokens()
}

/** Cambio de compañía: nuevo par de tokens con el TenantId elegido. */
export async function switchTenantTokens(tenantId: number): Promise<void> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) throw new Error('Sin sesión.')
  const pair = await unwrap(api.POST('/api/v1/auth/switch-tenant', { body: { refreshToken, tenantId } }))
  storeTokenPair(pair)
}
