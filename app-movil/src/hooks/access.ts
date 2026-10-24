import { useMemo } from 'react';
import { useAuth } from '@hooks/AuthContext';
import type { CompanyAccess } from '@services/auth/tokenStorage';

// Qué puede hacer la persona en la app (Lote 213). Los permisos vienen por compañía en la sesión; aquí se responde «¿en alguna?» y «¿en cuál?».
// El usuario «solo chofer» ve únicamente lo del chofer: aunque tuviera otros permisos, la app no los usa (doble candado con el tipo de usuario).

export interface Access {
  /** Compañías con acceso en esta sesión (las bloqueadas por MFA no cuentan). */
  companies: CompanyAccess[];
  /** ¿Tiene el permiso en alguna compañía? */
  can: (perm: string) => boolean;
  /** Las compañías donde tiene el permiso. */
  companiesWith: (perm: string) => CompanyAccess[];
  /** Es chofer activo en alguna compañía: ve sus manifiestos y envíos. */
  isDriver: boolean;
  /** Usuario «solo chofer»: solo lo del chofer. */
  driverOnly: boolean;
  /** Hay algo del personal que mostrar (atención, pulso…): el inicio del personal en vez del del chofer. */
  staff: boolean;
}

export const STAFF_PERMS = ['pulse.attention', 'analytics.view', 'orders.view', 'trips.view', 'trips.scan'] as const;

export function computeAccess(companies: CompanyAccess[], driverOnly: boolean): Access {
  const usable = companies.filter((c) => !c.blocked);
  const has = (c: CompanyAccess, perm: string) => !driverOnly && (c.permissions ?? []).includes(perm);
  const can = (perm: string) => usable.some((c) => has(c, perm));
  const companiesWith = (perm: string) => usable.filter((c) => has(c, perm));
  const isDriver = usable.some((c) => !!c.isDriver);
  return { companies: usable, can, companiesWith, isDriver, driverOnly, staff: !driverOnly && STAFF_PERMS.some((p) => can(p)) };
}

export function useAccess(): Access {
  const { companies, driverOnly } = useAuth();
  return useMemo(() => computeAccess(companies, driverOnly), [companies, driverOnly]);
}
