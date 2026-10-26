// Membresías a las que el usuario puede cambiar desde la cabecera.
import type { components } from '../kernel/api/schema'

type Membership = components['schemas']['MembershipDto']

/** Estatus de membresía que `POST /api/v1/auth/switch-tenant` acepta: ACTIVE (miembro) y PLATFORM (administrador de
 *  plataforma sin membresías: /me le lista las demás compañías; PLATFORM_INACTIVE = una compañía desactivada, a la que solo la plataforma puede entrar para volver a activarla). SUSPENDED/INVITED se rechazan con 403. */
const SWITCHABLE = new Set(['ACTIVE', 'PLATFORM', 'PLATFORM_INACTIVE'])

/** Membresías elegibles en el selector de compañía (el selector solo se pinta si hay más de una). */
export function switchableMemberships(memberships: readonly Membership[] | null | undefined): Membership[] {
  return (memberships ?? []).filter((m) => SWITCHABLE.has((m.status ?? '').toUpperCase()))
}
