// Qué ofrece la compañía de la sesión a este acceso (GET /portal/me → capabilities): decide qué pestañas se muestran. Una persona con un acceso en una compañía de almacén y otro en una de
// transporte ve, en cada sesión, solo lo que esa compañía ofrece. Mientras no se sabe (cargando o sin respuesta) se asume la app de siempre: transporte, sin almacén.
import { useSessionState } from '../kernel/auth/session'
import { useAsync } from '../kernel/ui/useAsync'
import { getMe, type PortalCapabilitiesDto } from './portalApi'

export interface Offer {
  /** Inicio y Órdenes (transporte). */
  transport: boolean
  /** Existencias, productos, movimientos y valor (almacén). */
  warehouse: boolean
  canSeeValue: boolean
  /** Lote 156: lo que este cliente puede crear desde su portal (lo enciende la compañía por cliente). */
  canCreateProducts: boolean
  canCreateArrivals: boolean
  canCreateExits: boolean
  companyName: string
  known: boolean
}

export const DEFAULT_OFFER: Offer = { transport: true, warehouse: false, canSeeValue: false, canCreateProducts: false, canCreateArrivals: false, canCreateExits: false, companyName: '', known: false }

export function offerOf(caps: PortalCapabilitiesDto | null | undefined): Offer {
  if (!caps) return DEFAULT_OFFER
  return { transport: !!caps.transport, warehouse: !!caps.warehouse, canSeeValue: !!caps.canSeeValue, canCreateProducts: !!caps.canCreateProducts, canCreateArrivals: !!caps.canCreateArrivals, canCreateExits: !!caps.canCreateExits, companyName: caps.companyName ?? '', known: true }
}

/** Qué pestañas lleva la barra: el consignatario solo Envíos/Avisos/Mensajes/Ajustes; el cliente, Inicio y Órdenes con transporte y Almacén con almacén. */
export function visibleTabs(offer: Offer, consignee: boolean): { home: boolean; orders: boolean; warehouse: boolean } {
  if (consignee) return { home: false, orders: true, warehouse: false }
  return { home: offer.transport, orders: offer.transport, warehouse: offer.warehouse }
}

/** La pestaña a la que se entra: Inicio con transporte; si no, Almacén. */
export function landingTab(offer: Offer): 'home' | 'warehouse' {
  return offer.transport || !offer.warehouse ? 'home' : 'warehouse'
}

export function useOffer(): Offer {
  const { session } = useSessionState()
  // cambiar de acceso emite otro par de tokens: se vuelve a preguntar qué ofrece esa compañía
  const me = useAsync(getMe, [session?.refreshToken])
  return me.data ? offerOf(me.data.capabilities) : DEFAULT_OFFER
}
