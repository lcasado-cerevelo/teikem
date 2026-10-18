// Llamadas del portal al API (todas sobre los tipos generados). Las lecturas llegan ya limitadas al cliente de la sesión: el servidor es quien lo garantiza.
import { api, unwrap } from '../kernel/api/client'
import type { components } from '../kernel/api/schema'
import { getBrowserToken } from '../kernel/auth/browserToken'

type Schemas = components['schemas']
export type PortalMeDto = Schemas['PortalMeDto']
export type PortalClientDto = Schemas['PortalClientDto']
export type PortalSummaryDto = Schemas['PortalSummaryDto']
export type PortalOrderListItemDto = Schemas['PortalOrderListItemDto']
export type PortalOrderDto = Schemas['PortalOrderDto']
export type PortalNotificationDto = Schemas['PortalNotificationDto']
export type LegalDocumentDto = Schemas['LegalDocumentDto']
export type LegalStatusDto = Schemas['LegalStatusDto']
export type TrustedBrowserDto = Schemas['TrustedBrowserDto']
export type PortalAuthConfigDto = Schemas['PortalAuthConfigDto']

export type PublicTrackDto = Schemas['PublicTrackDto']
export type PortalMessagesDto = Schemas['PortalMessagesDto']
export type ClientMessageDto = Schemas['ClientMessageDto']
export type PortalOrderFormDto = Schemas['PortalOrderFormDto']
export type PortalPlaceDto = Schemas['PortalPlaceDto']
export type PortalOrderCreateRequest = Schemas['PortalOrderCreateRequest']
export type PortalOrderCreatedDto = Schemas['PortalOrderCreatedDto']

export const PAGE_SIZE = 20

/** «¿Cómo va mi orden?»: consulta PÚBLICA por el conduce de una compañía (su identificador va en el enlace). Solo devuelve el avance. */
export const trackOrder = (slug: string, waybill: string, lang: string) =>
  unwrap(api.GET('/api/v1/public/track/{slug}/{waybill}', { params: { path: { slug, waybill }, query: { lang } } }))

// ---------------------------------------------------------------- nueva orden (Lote 105): el cliente sale de la sesión
export const getOrderForm = () => unwrap(api.GET('/api/v1/portal/order-form'))
export const createOrder = (body: PortalOrderCreateRequest) => unwrap(api.POST('/api/v1/portal/orders', { body, headers: { 'Idempotency-Key': newKey() } }))
function newKey(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}-${Math.random().toString(36).slice(2)}`
}

// ---------------------------------------------------------------- mensajes con el equipo de servicio al cliente (la conversación es la MÍA: la persona y el cliente salen de la sesión)
export const getMessages = (before?: number, take = 30) => unwrap(api.GET('/api/v1/portal/messages', { params: { query: { before, take } } }))
export const sendMessage = (body: string) => unwrap(api.POST('/api/v1/portal/messages', { body: { body } }))
export const markMessagesRead = async () => {
  await unwrap(api.POST('/api/v1/portal/messages/read'))
}
export const getMessagesUnread = () => unwrap(api.GET('/api/v1/portal/messages/unread'))

export const getAuthConfig = () => unwrap(api.GET('/api/v1/portal/auth/config'))
export const getMe = () => unwrap(api.GET('/api/v1/portal/me'))
export const getSummary = () => unwrap(api.GET('/api/v1/portal/summary'))

export interface OrderQuery {
  search?: string
  status?: string
  skip?: number
  take?: number
}

export const listOrders = (q: OrderQuery) =>
  unwrap(api.GET('/api/v1/portal/orders', { params: { query: { search: q.search || undefined, status: q.status || undefined, skip: q.skip ?? 0, take: q.take ?? PAGE_SIZE } } }))

export const getOrder = (publicId: string) => unwrap(api.GET('/api/v1/portal/orders/{publicId}', { params: { path: { publicId } } }))

export const listNotifications = (skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/notifications', { params: { query: { skip, take } } }))

export const getLegalDocument = (lang: string) => unwrap(api.GET('/api/v1/legal/current', { params: { query: { lang } } }))
export const getLegalStatus = () => unwrap(api.GET('/api/v1/legal/status'))
export const acceptLegal = (version: string, lang: string) => unwrap(api.POST('/api/v1/legal/accept', { body: { version, lang } }))

export async function listTrustedBrowsers(): Promise<TrustedBrowserDto[]> {
  return unwrap(api.GET('/api/v1/auth/trusted-browsers', { headers: { 'X-Trusted-Browser': await getBrowserToken() } }))
}

export async function revokeTrustedBrowser(publicId: string): Promise<void> {
  await unwrap(api.DELETE('/api/v1/auth/trusted-browsers/{publicId}', { params: { path: { publicId } } }))
}

export async function confirmBrowser(token: string): Promise<void> {
  await unwrap(api.POST('/api/v1/auth/confirm-browser', { body: { token } }))
}
