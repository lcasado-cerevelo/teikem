// Flujo de acceso del portal contra /api/v1/portal/auth/* y /api/v1/auth/*: login → (computadora nueva: confirmar por correo) → (segundo paso MFA) → sesión. El challenge del MFA vive solo en memoria.
import { Platform } from 'react-native'

import { api, unwrap } from '../api/client'
import type { components } from '../api/schema'
import { getBrowserToken } from './browserToken'
import { getNoTrust } from './preferences'
import { clearSession, getSessionState, saveSession } from './session'

type Schemas = components['schemas']
export type AuthResultDto = Schemas['AuthResultDto']
export type BrowserConfirmationDto = Schemas['BrowserConfirmationDto']
export type MfaEnrollResultDto = Schemas['MfaEnrollResultDto']

export type LoginOutcome =
  | { status: 'ok' }
  | { status: 'mfa_required'; enrollmentRequired: boolean }
  | { status: 'browser_confirmation_required'; browser: BrowserConfirmationDto }
  | { status: 'captcha_required' }

interface Challenge {
  token: string
  enrollmentRequired: boolean
}
let challenge: Challenge | null = null

export function getChallenge(): Challenge | null {
  return challenge
}

export function cancelMfa(): void {
  challenge = null
}

function deviceInfo(): string {
  return `Teikem Cliente · ${Platform.OS}`
}

function challengeHeaders(): { Authorization: string } {
  if (!challenge) throw new Error('No hay un paso de verificación pendiente.')
  return { Authorization: `Bearer ${challenge.token}` }
}

async function handle(result: AuthResultDto): Promise<LoginOutcome> {
  if (result.status === 'browser_confirmation_required' && result.browser) return { status: 'browser_confirmation_required', browser: result.browser }
  if (result.status === 'captcha_required') return { status: 'captcha_required' }
  if (result.status === 'mfa_required' && result.mfaChallengeToken) {
    challenge = { token: result.mfaChallengeToken, enrollmentRequired: result.mfaEnrollmentRequired ?? false }
    return { status: 'mfa_required', enrollmentRequired: challenge.enrollmentRequired }
  }
  const pair = result.tokens
  if (!pair?.accessToken || !pair.refreshToken) throw new Error('Respuesta de autenticación sin tokens.')
  challenge = null
  await saveSession({
    accessToken: pair.accessToken,
    accessExpiresAtUtc: pair.accessExpiresAtUtc,
    refreshToken: pair.refreshToken,
    refreshExpiresAtUtc: pair.refreshExpiresAtUtc,
    tenantId: pair.tenantId ?? 0,
  })
  return { status: 'ok' }
}

/** Paso 1: correo y contraseña. Manda la marca de este aparato y la casilla «No confiar en esta computadora» (recordada). Lanza ApiError (401 `Credenciales inválidas.`, 403 sin acceso…). */
export async function login(email: string, password: string, captchaToken?: string): Promise<LoginOutcome> {
  const [trustedBrowserToken, doNotTrust] = await Promise.all([getBrowserToken(), getNoTrust()])
  const result = await unwrap(
    api.POST('/api/v1/portal/auth/login', {
      body: { email: email.trim(), password, clientPublicId: null, deviceInfo: deviceInfo(), trustedBrowserToken, captchaToken: captchaToken ?? null, doNotTrust },
    }),
  )
  return handle(result)
}

/** Paso 2: código TOTP (o de recuperación) con el challenge del paso 1. */
export async function verifyMfa(code: string): Promise<LoginOutcome> {
  const trustedBrowserToken = await getBrowserToken()
  const result = await unwrap(api.POST('/api/v1/auth/mfa/verify', { body: { code, deviceInfo: deviceInfo(), trustedBrowserToken }, headers: challengeHeaders() }))
  return handle(result)
}

/** Inscripción obligatoria del MFA (la primera vez): secreto y enlace otpauth para la app de autenticación. */
export async function enrollMfa(): Promise<MfaEnrollResultDto> {
  return unwrap(api.POST('/api/v1/auth/mfa/totp/enroll', { headers: challengeHeaders() }))
}

/** Confirma la inscripción con un código; devuelve los códigos de recuperación (se muestran una sola vez). */
export async function confirmMfa(code: string): Promise<string[]> {
  const result = await unwrap(api.POST('/api/v1/auth/mfa/totp/confirm', { body: { code }, headers: challengeHeaders() }))
  return result.recoveryCodes ?? []
}

/** Cambia el cliente de la sesión (cuentas con acceso a varios). */
export async function switchClient(clientPublicId: string): Promise<void> {
  const session = getSessionState().session
  if (!session) throw new Error('No hay sesión.')
  const pair = await unwrap(api.POST('/api/v1/portal/auth/switch-client', { body: { refreshToken: session.refreshToken, clientPublicId } }))
  if (!pair.accessToken || !pair.refreshToken) throw new Error('Respuesta de autenticación sin tokens.')
  await saveSession({
    accessToken: pair.accessToken,
    accessExpiresAtUtc: pair.accessExpiresAtUtc,
    refreshToken: pair.refreshToken,
    refreshExpiresAtUtc: pair.refreshExpiresAtUtc,
    tenantId: pair.tenantId ?? session.tenantId,
  })
}

/** Cierra la sesión en el servidor (revoca el refresh token) y borra la local. Nunca lanza. */
export async function logout(): Promise<void> {
  const session = getSessionState().session
  if (session) {
    try {
      await api.POST('/api/v1/auth/logout', { body: { refreshToken: session.refreshToken } })
    } catch {
      // sin red: igual se borra la sesión local
    }
  }
  challenge = null
  await clearSession()
}
