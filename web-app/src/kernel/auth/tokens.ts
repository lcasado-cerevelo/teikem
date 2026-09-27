// Tokens de la sesión: en memoria y espejados en sessionStorage (sobreviven a recargar la pestaña, no a cerrarla).
// Sin dependencias del cliente del API para evitar ciclos: el cliente lee de aquí.

export interface StoredTokens {
  accessToken: string
  accessExpiresAtUtc?: string
  refreshToken: string
  refreshExpiresAtUtc?: string
  tenantId: number
}

/** Token de challenge MFA (segundo paso del login). */
export interface MfaChallenge {
  token: string
  enrollmentRequired: boolean
}

const TOKENS_KEY = 'teikem.auth'
const MFA_KEY = 'teikem.mfa'

function read<T>(key: string): T | null {
  try {
    const raw = globalThis.sessionStorage?.getItem(key)
    return raw ? (JSON.parse(raw) as T) : null
  } catch {
    return null
  }
}

function write(key: string, value: unknown): void {
  try {
    if (value == null) globalThis.sessionStorage?.removeItem(key)
    else globalThis.sessionStorage?.setItem(key, JSON.stringify(value))
  } catch {
    // sin almacenamiento: los tokens quedan solo en memoria
  }
}

let tokens: StoredTokens | null = read<StoredTokens>(TOKENS_KEY)
let challenge: MfaChallenge | null = read<MfaChallenge>(MFA_KEY)
const listeners = new Set<() => void>()

function emit() {
  listeners.forEach((l) => l())
}

export function getTokens(): StoredTokens | null {
  return tokens
}

export function getAccessToken(): string | null {
  return tokens?.accessToken ?? null
}

export function getRefreshToken(): string | null {
  return tokens?.refreshToken ?? null
}

export function setTokens(next: StoredTokens | null): void {
  tokens = next
  write(TOKENS_KEY, next)
  emit()
}

/** Reautenticación (AAL2): cambia solo el access token; el refresh token sigue igual. */
export function updateAccessToken(accessToken: string, accessExpiresAtUtc?: string): void {
  if (!tokens) return
  setTokens({ ...tokens, accessToken, accessExpiresAtUtc })
}

export function clearTokens(): void {
  challenge = null
  write(MFA_KEY, null)
  setTokens(null)
}

export function subscribeTokens(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function getMfaChallenge(): MfaChallenge | null {
  return challenge
}

export function setMfaChallenge(next: MfaChallenge | null): void {
  challenge = next
  write(MFA_KEY, next)
  emit()
}
