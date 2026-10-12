// Lógica pura de «Usuarios de portal» del cliente (F-A3, manual 02 cap. 7): validación espejo del servidor
// (PortalUserService.NormalizeEmail / OptionalText), request de la invitación y qué acciones admite cada estatus.
// Sin React ni red, para probarla sola (portalRules.test.ts).
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'
import { isValidEmail } from './clientRules'

type Schemas = components['schemas']
export type PortalUser = Schemas['PortalUserDto']
export type PortalInviteRequest = Schemas['PortalInviteRequest']
export type PortalInviteResult = Schemas['PortalInviteResultDto']

/** Dominio de catálogo de los roles de portal (CatalogDomains.PortalRole). */
export const PORTAL_ROLE_DOMAIN = 'PortalRole'
/** Dominio de estatus del usuario de portal y su EntityType. */
export const PORTAL_STATUS_DOMAIN = 'PortalUserStatus'

/** Máximos del servidor: correo 150 (PortalUserService.EmailMaxLength) y nombre 150. */
export const PORTAL_EMAIL_MAX = 150
export const PORTAL_NAME_MAX = 150

/** Estatus (PortalUserStatuses): INVITED → ACTIVE; SUSPENDED lateral; DISABLED terminal (baja definitiva). */
export const PortalStatus = { Invited: 'INVITED', Active: 'ACTIVE', Suspended: 'SUSPENDED', Disabled: 'DISABLED' } as const

/** Mensaje del 409 del cliente dado de baja (D39), por si hay que mostrarlo antes de que el servidor responda. */
export const CLIENT_INACTIVE_MESSAGE = 'El cliente está dado de baja; solo se consulta su historial.'

export interface InviteValues {
  email: string
  fullName: string
  role: string
}

export interface InviteMessages {
  emailRequired: string
  emailInvalid: string
  emailMax: string
  nameMax: string
  roleRequired: string
}

export function inviteSchema(m: InviteMessages) {
  return z.object({
    email: z
      .string()
      .trim()
      .min(1, m.emailRequired)
      .max(PORTAL_EMAIL_MAX, m.emailMax)
      .refine((v) => v === '' || isValidEmail(v), m.emailInvalid),
    fullName: z.string().trim().max(PORTAL_NAME_MAX, m.nameMax),
    role: z.string().min(1, m.roleRequired),
  })
}

/** Request de la invitación: correo en minúsculas (el servidor lo normaliza igual), nombre vacío → null. */
export function buildInviteRequest(v: InviteValues): PortalInviteRequest {
  return { email: v.email.trim().toLowerCase(), fullName: v.fullName.trim() || null, role: v.role }
}

export function emptyInvite(): InviteValues {
  return { email: '', fullName: '', role: '' }
}

/** Valores para «Volver a invitar» a alguien dado de baja: mismo correo, nombre y rol. */
export function reinviteValues(u: Pick<PortalUser, 'email' | 'fullName' | 'role'>): InviteValues {
  return { email: u.email ?? '', fullName: u.fullName ?? '', role: u.role ?? '' }
}

export interface PortalActions {
  /** INVITED: emite un enlace nuevo (el anterior deja de valer). */
  resend: boolean
  /** ACTIVE → SUSPENDED (reversible). */
  suspend: boolean
  /** SUSPENDED → ACTIVE (conserva la contraseña). */
  reactivate: boolean
  /** Cualquier estatus no terminal → DISABLED (baja definitiva). */
  remove: boolean
  /** DISABLED: se invita de nuevo con el mismo correo (la fila renace en INVITED). */
  reinvite: boolean
}

/** Qué admite cada estatus (las mismas guardas del servidor). `clientActive=false` quita lo que el servidor rechaza con 409 (D39). */
export function portalActions(user: Pick<PortalUser, 'status'>, clientActive: boolean): PortalActions {
  const s = (user.status ?? '').toUpperCase()
  return {
    resend: s === PortalStatus.Invited && clientActive,
    suspend: s === PortalStatus.Active,
    reactivate: s === PortalStatus.Suspended,
    remove: s !== PortalStatus.Disabled && s !== '',
    reinvite: s === PortalStatus.Disabled && clientActive,
  }
}

/** Orden de la tabla: primero los vivos (invitados, activos, suspendidos), al final los dados de baja; luego por correo. */
export function sortPortalUsers(users: readonly PortalUser[]): PortalUser[] {
  const rank = (u: PortalUser) => (u.status === PortalStatus.Disabled ? 1 : 0)
  return [...users].sort((a, b) => rank(a) - rank(b) || (a.email ?? '').localeCompare(b.email ?? ''))
}

/** Quien invita desde un cliente con la cuenta ya activa en otro: el servidor la deja ACTIVE sin enlace (no hay `expiresAtUtc`). */
export function wasAddedWithoutLink(r: Pick<PortalInviteResult, 'user' | 'expiresAtUtc'>): boolean {
  return !r.expiresAtUtc && r.user?.status === PortalStatus.Active
}
