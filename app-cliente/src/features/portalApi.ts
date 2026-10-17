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

export const PAGE_SIZE = 20

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
