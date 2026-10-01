// API de "Mi cuenta" sobre AuthController (/api/v1/auth/*): contraseña, MFA (TOTP) y sesiones activas.
// Las acciones con [RequireAal2] (desactivar MFA) responden 403 aal2_required si la reautenticación no es reciente:
// el cliente abre solo el modal de useReauth y reintenta la llamada una vez.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

export type SessionDto = components['schemas']['SessionDto']
export type ChangePasswordRequest = components['schemas']['ChangePasswordRequest']
export type MfaEnrollResultDto = components['schemas']['MfaEnrollResultDto']
export type PinStatusDto = components['schemas']['PinStatusDto']
export type PinSetRequest = components['schemas']['PinSetRequest']

export const MY_PIN_KEY = ['/api/v1/me/pin'] as const

/** Estado del PIN propio (Lote 8A, solo con el módulo WMS_LOTSERIAL): nunca devuelve el PIN, solo si hay uno. */
export function useMyPin() {
  return useQuery({ queryKey: MY_PIN_KEY, queryFn: () => unwrap(api.GET('/api/v1/me/pin')) })
}

/** Fijar o cambiar el PIN propio (`PinSetRequest`: contraseña actual + PIN nuevo). Cambia las sesiones del usuario en aparatos. */
export function useSetMyPin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: PinSetRequest) => unwrap(api.PUT('/api/v1/me/pin', { body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: MY_PIN_KEY }),
  })
}

/** Quitar el PIN propio. */
export function useRemoveMyPin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/v1/me/pin')),
    onSuccess: () => qc.invalidateQueries({ queryKey: MY_PIN_KEY }),
  })
}

export const SESSIONS_KEY = ['/api/v1/auth/sessions'] as const

/** Sesiones activas del usuario (refresh tokens vigentes); `isCurrent` marca la de este navegador. */
export function useSessions() {
  return useQuery({
    queryKey: SESSIONS_KEY,
    queryFn: () => unwrap(api.GET('/api/v1/auth/sessions')),
  })
}

/** Revoca otra sesión (la actual no se revoca desde la lista: 409). */
export function useRevokeSession() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      await unwrap(api.DELETE('/api/v1/auth/sessions/{id}', { params: { path: { id } } }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: SESSIONS_KEY }),
  })
}

/** PUT /api/v1/auth/password. Al cambiarla, el servidor cierra las demás sesiones. */
export function useChangePassword() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (body: ChangePasswordRequest) => {
      await unwrap(api.PUT('/api/v1/auth/password', { body }))
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: SESSIONS_KEY }),
  })
}

/** Inicia el enrolamiento TOTP: clave en Base32 y URI otpauth (el factor queda sin confirmar). */
export function useEnrollTotp() {
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/auth/mfa/totp/enroll')),
  })
}

/** Confirma el enrolamiento con un código de la app; devuelve los códigos de recuperación (se muestran una sola vez). */
export function useConfirmTotp() {
  return useMutation({
    mutationFn: async (code: string) => {
      const result = await unwrap(api.POST('/api/v1/auth/mfa/totp/confirm', { body: { code } }))
      return result.recoveryCodes ?? []
    },
  })
}

/** Desactiva el MFA (requiere AAL2: el cliente pide la reautenticación si hace falta). */
/** 2026-10-01: códigos de recuperación nuevos (los anteriores dejan de servir). Exige reautenticación reciente. */
export function useRegenerateRecoveryCodes() {
  return useMutation({
    mutationFn: async () => (await unwrap(api.POST('/api/v1/auth/mfa/recovery-codes'))).recoveryCodes,
  })
}

export function useDisableTotp() {
  return useMutation({
    mutationFn: async () => {
      await unwrap(api.DELETE('/api/v1/auth/mfa/totp'))
    },
  })
}
