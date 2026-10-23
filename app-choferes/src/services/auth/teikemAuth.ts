import axios from 'axios';
import { Platform } from 'react-native';
import { env } from '@config/env';
import type { AuthUser, StoredSession } from './tokenStorage';

// Acceso contra el API de Teikem (/api/v1/auth/*). Usa un cliente propio SIN los interceptores de renovación de sesión: estas llamadas son las
// que crean o renuevan la sesión, y no pueden depender de ella (evita ciclos).
//
// Pasos del login: correo y contraseña → (si la compañía o el usuario lo exigen) código MFA → sesión.
// La huella NO reemplaza ninguno de estos pasos: solo abre la sesión ya guardada en el teléfono (ver biometricAuth.ts y AuthContext).

const http = axios.create({ baseURL: env.baseUrl.replace(/\/api\/?$/, ''), timeout: 20000, headers: { 'Content-Type': 'application/json' } });

export interface TokenPairDto {
  accessToken: string;
  accessExpiresAtUtc: string;
  refreshToken: string;
  refreshExpiresAtUtc: string;
  tenantId: number;
}

export interface BrowserConfirmationDto {
  email: string;
  sent: boolean;
  message?: string | null;
  devLink?: string | null;
}

export interface AuthResultDto {
  status: string;
  tokens?: TokenPairDto | null;
  mfaChallengeToken?: string | null;
  mfaEnrollmentRequired?: boolean;
  browser?: BrowserConfirmationDto | null;
  devMfaCode?: string | null;
}

export interface MfaChallenge {
  token: string;
  /** true = la persona todavía no tiene su autenticador: hay que inscribirlo primero. */
  enrollmentRequired: boolean;
  /** Solo con el API en Development: el código vigente para mostrarlo en pantalla. */
  devCode?: string | null;
}

/** Resultado de un paso del login, ya traducido para la pantalla. */
export type LoginOutcome =
  | { status: 'ok'; session: StoredSession }
  | { status: 'mfa_required'; challenge: MfaChallenge }
  | { status: 'browser_confirmation_required'; browser: BrowserConfirmationDto }
  | { status: 'onboarding_required' };

export function deviceInfo(): string {
  return `Teikem Chofer · ${Platform.OS}`;
}

export function toSession(pair: TokenPairDto, previous?: StoredSession): StoredSession {
  return {
    accessToken: pair.accessToken,
    expiresAt: Date.parse(pair.accessExpiresAtUtc),
    refreshToken: pair.refreshToken,
    refreshExpiresAt: Date.parse(pair.refreshExpiresAtUtc),
    tenantId: pair.tenantId,
    user: previous?.user,
  };
}

function handle(result: AuthResultDto): LoginOutcome {
  if (result.status === 'browser_confirmation_required' && result.browser) return { status: 'browser_confirmation_required', browser: result.browser };
  if (result.status === 'onboarding_required') return { status: 'onboarding_required' };
  if (result.status === 'mfa_required' && result.mfaChallengeToken) {
    return { status: 'mfa_required', challenge: { token: result.mfaChallengeToken, enrollmentRequired: result.mfaEnrollmentRequired ?? false, devCode: result.devMfaCode ?? null } };
  }
  if (!result.tokens?.accessToken || !result.tokens.refreshToken) throw new Error('Respuesta de autenticación sin tokens.');
  return { status: 'ok', session: toSession(result.tokens) };
}

const bearer = (token: string) => ({ Authorization: `Bearer ${token}` });

/** Paso 1: correo y contraseña. Lanza el error de axios (401 «Credenciales inválidas.», 403 sin acceso…). */
export async function login(email: string, password: string, language: string): Promise<LoginOutcome> {
  const { data } = await http.post<AuthResultDto>('/api/v1/auth/login', { email: email.trim(), password, tenantId: null, deviceInfo: deviceInfo() }, { headers: { 'Accept-Language': language } });
  return handle(data);
}

/** Paso 2: código TOTP (o de recuperación) con el challenge del paso 1. */
export async function verifyMfa(challenge: MfaChallenge, code: string, language: string): Promise<LoginOutcome> {
  const { data } = await http.post<AuthResultDto>('/api/v1/auth/mfa/verify', { code, deviceInfo: deviceInfo() }, { headers: { ...bearer(challenge.token), 'Accept-Language': language } });
  return handle(data);
}

export interface MfaEnrollResultDto {
  secret: string;
  otpAuthUri: string;
  devMfaCode?: string | null;
}

/** Inscripción obligatoria del MFA (la primera vez): la clave para el autenticador. */
export async function enrollMfa(challenge: MfaChallenge, language: string): Promise<MfaEnrollResultDto> {
  const { data } = await http.post<MfaEnrollResultDto>('/api/v1/auth/mfa/totp/enroll', undefined, { headers: { ...bearer(challenge.token), 'Accept-Language': language } });
  return data;
}

/** Confirma la inscripción con un código; devuelve los códigos de recuperación (se muestran una sola vez). */
export async function confirmMfa(challenge: MfaChallenge, code: string, language: string): Promise<string[]> {
  const { data } = await http.post<{ recoveryCodes?: string[] | null }>('/api/v1/auth/mfa/totp/confirm', { code }, { headers: { ...bearer(challenge.token), 'Accept-Language': language } });
  return data.recoveryCodes ?? [];
}

/** Renueva la sesión con el refresh token. Responde con el par nuevo; lanza si el servidor la rechaza (vencida o revocada). */
export async function refreshTokens(refreshToken: string): Promise<TokenPairDto> {
  const { data } = await http.post<TokenPairDto>('/api/v1/auth/refresh', { refreshToken });
  return data;
}

/** Cambia la compañía activa (usuarios con acceso a varias): devuelve la sesión nueva de esa compañía. */
export async function switchTenant(refreshToken: string, tenantId: number): Promise<TokenPairDto> {
  const { data } = await http.post<TokenPairDto>('/api/v1/auth/switch-tenant', { refreshToken, tenantId });
  return data;
}

/** Cierra la sesión en el servidor (revoca el refresh token). Nunca lanza: sin red, igual se borra la local. */
export async function serverLogout(refreshToken: string): Promise<void> {
  try {
    await http.post('/api/v1/auth/logout', { refreshToken });
  } catch {
    // sin red
  }
}

interface MeDto {
  userId: number;
  fullName?: string | null;
  email?: string | null;
  tenantId?: number | null;
  tenantName?: string | null;
  memberships?: { tenantId: number; tenantName: string; status?: string }[] | null;
}

/** Quién es el usuario y a qué compañías pertenece (para el nombre en pantalla y el cambio de compañía). */
export async function fetchMe(accessToken: string): Promise<AuthUser> {
  const { data } = await http.get<MeDto>('/api/v1/me', { headers: bearer(accessToken) });
  return {
    userId: data.userId,
    fullName: data.fullName,
    email: data.email,
    tenantId: data.tenantId ?? 0,
    tenantName: data.tenantName,
    memberships: (data.memberships ?? []).map((m) => ({ tenantId: m.tenantId, tenantName: m.tenantName })),
  };
}

export interface LegalDocumentDto {
  version?: string | null;
  title?: string | null;
  intro?: string | null;
  partPrivacy?: string | null;
  partTerms?: string | null;
  sections?: { id: string; part?: string | null; heading?: string | null; paragraphs?: string[] | null }[] | null;
}

export interface LegalStatusDto {
  currentVersion: string;
  required: boolean;
  accepted: boolean;
}

export async function fetchLegalStatus(accessToken: string): Promise<LegalStatusDto> {
  const { data } = await http.get<LegalStatusDto>('/api/v1/legal/status', { headers: bearer(accessToken) });
  return data;
}

export async function fetchLegalDocument(accessToken: string, lang: string): Promise<LegalDocumentDto> {
  const { data } = await http.get<LegalDocumentDto>('/api/v1/legal/current', { params: { lang }, headers: bearer(accessToken) });
  return data;
}

export async function acceptLegal(accessToken: string, version: string, lang: string): Promise<void> {
  await http.post('/api/v1/legal/accept', { version, lang }, { headers: bearer(accessToken) });
}
