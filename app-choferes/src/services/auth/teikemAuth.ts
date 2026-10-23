import axios from 'axios';
import { Platform } from 'react-native';
import { env } from '@config/env';
import type { AuthUser } from './tokenStorage';

// Acceso contra el API de Teikem (/api/v1/auth/*). Usa un cliente propio SIN los interceptores de renovación de sesión: estas llamadas son las
// que crean o renuevan la sesión, y no pueden depender de ella (evita ciclos).
//
// Pasos del login: correo y contraseña → (si la compañía o el usuario lo exigen) código MFA → sesión.
// La huella NO reemplaza ninguno de estos pasos: solo abre la sesión ya guardada en el teléfono (ver biometricAuth.ts y AuthContext).

const http = axios.create({ baseURL: env.baseUrl.replace(/\/api\/?$/, ''), timeout: 20000, headers: { 'Content-Type': 'application/json' } });

/** Una compañía del chofer con su token de acceso (null si la compañía exige MFA y esta sesión no lo cumplió: «blocked»). */
export interface CompanyTokenDto {
  tenantId: number;
  tenantName: string;
  accessToken?: string | null;
  accessExpiresAtUtc?: string | null;
  blocked?: string | null;
}

/** Sesión del chofer: un refresh token (rota en cada renovación) y los tokens de acceso de TODAS sus compañías. */
export interface DriverSessionDto {
  refreshToken: string;
  refreshExpiresAtUtc: string;
  companies: CompanyTokenDto[];
}

/** Respuesta del login: el servidor entrega un par de tokens de UNA compañía solo para arrancar; la sesión real sale de company-tokens. */
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
  | { status: 'ok'; refreshToken: string }
  | { status: 'mfa_required'; challenge: MfaChallenge }
  | { status: 'browser_confirmation_required'; browser: BrowserConfirmationDto }
  | { status: 'onboarding_required' };

export function deviceInfo(): string {
  return `Teikem Chofer · ${Platform.OS}`;
}

function handle(result: AuthResultDto): LoginOutcome {
  if (result.status === 'browser_confirmation_required' && result.browser) return { status: 'browser_confirmation_required', browser: result.browser };
  if (result.status === 'onboarding_required') return { status: 'onboarding_required' };
  if (result.status === 'mfa_required' && result.mfaChallengeToken) {
    return { status: 'mfa_required', challenge: { token: result.mfaChallengeToken, enrollmentRequired: result.mfaEnrollmentRequired ?? false, devCode: result.devMfaCode ?? null } };
  }
  if (!result.tokens?.accessToken || !result.tokens.refreshToken) throw new Error('Respuesta de autenticación sin tokens.');
  return { status: 'ok', refreshToken: result.tokens.refreshToken };
}

const bearer = (token: string) => ({ Authorization: `Bearer ${token}` });

/** Paso 1: correo y contraseña. Lanza el error de axios (401 «Credenciales inválidas.», 403 sin acceso…). */
export async function login(email: string, password: string, language: string): Promise<LoginOutcome> {
  const { data } = await http.post<AuthResultDto>('/api/v1/driver-app/auth/login', { email: email.trim(), password, tenantId: null, deviceInfo: deviceInfo() }, { headers: { 'Accept-Language': language } });
  return handle(data);
}

/** Paso 2: código TOTP (o de recuperación) con el challenge del paso 1. */
export async function verifyMfa(challenge: MfaChallenge, code: string, language: string): Promise<LoginOutcome> {
  const { data } = await http.post<AuthResultDto>('/api/v1/driver-app/auth/mfa/verify', { code, deviceInfo: deviceInfo() }, { headers: { ...bearer(challenge.token), 'Accept-Language': language } });
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

/**
 * Abre o renueva la sesión: cambia el refresh token por uno nuevo (el anterior queda revocado; el vencimiento absoluto no se alarga) y trae el token de acceso
 * de cada compañía. Lanza si el servidor rechaza la sesión (vencida o revocada).
 */
export async function fetchDriverSession(refreshToken: string): Promise<DriverSessionDto> {
  const { data } = await http.post<DriverSessionDto>('/api/v1/driver-app/auth/company-tokens', { refreshToken });
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
}

/** Quién es el usuario (para el nombre en pantalla). */
export async function fetchMe(accessToken: string): Promise<AuthUser> {
  const { data } = await http.get<MeDto>('/api/v1/me', { headers: bearer(accessToken) });
  return { userId: data.userId, fullName: data.fullName, email: data.email };
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
