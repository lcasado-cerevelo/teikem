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
export type PortalInvoiceListItemDto = Schemas['PortalInvoiceListItemDto']
export type PortalInvoiceDto = Schemas['PortalInvoiceDto']
export type PortalNoticePrefDto = Schemas['PortalNoticePrefDto']
export type PortalProfileDto = Schemas['PortalProfileDto']
export type PortalIndicatorsDto = Schemas['PortalIndicatorsDto']
export type PortalNoticePrefsDto = Schemas['PortalNoticePrefsDto']
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

// ---------------------------------------------------------------- portal de almacén (Lote 154): existencias, productos, movimientos y valor de MI inventario (solo lectura)
export type PortalCapabilitiesDto = Schemas['PortalCapabilitiesDto']
export type PortalStockItemDto = Schemas['PortalStockItemDto']
export type PortalProductDto = Schemas['PortalProductDto']
export type PortalMovementDto = Schemas['PortalMovementDto']
export type ValuationReportDto = Schemas['ValuationReportDto']
export type ValuationRowDto = Schemas['ValuationRowDto']
export const listStock = (search: string, skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/warehouse/stock', { params: { query: { search: search || undefined, skip, take } } }))
export const listWarehouseProducts = (search: string, skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/warehouse/products', { params: { query: { search: search || undefined, skip, take } } }))
export const listMovements = (search: string, skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/warehouse/movements', { params: { query: { search: search || undefined, skip, take } } }))
export type PortalProductCreateRequest = Schemas['PortalProductCreateRequest']
export type PortalArrivalRequest = Schemas['PortalArrivalRequest']
export type PortalArrivalDto = Schemas['PortalArrivalDto']
export type PortalExitRequest = Schemas['PortalExitRequest']
export type PortalExitDto = Schemas['PortalExitDto']
export type PortalExitListItemDto = Schemas['PortalExitListItemDto']
export type PortalWarehouseOptionDto = Schemas['PortalWarehouseOptionDto']
/** Lote 156: lo que el cliente crea desde su portal (si la compañía se lo permite): productos, avisos de llegada y avisos de salida. */
export const listWarehouseOptions = () => unwrap(api.GET('/api/v1/portal/warehouse/warehouses'))
export const createProduct = (body: PortalProductCreateRequest) => unwrap(api.POST('/api/v1/portal/warehouse/products', { body }))
export const listArrivals = (search: string) => unwrap(api.GET('/api/v1/portal/warehouse/arrivals', { params: { query: { search: search || undefined } } }))
export const createArrival = (body: PortalArrivalRequest) => unwrap(api.POST('/api/v1/portal/warehouse/arrivals', { body }))
export const updateArrival = (id: number, body: PortalArrivalRequest) => unwrap(api.PATCH('/api/v1/portal/warehouse/arrivals/{id}', { params: { path: { id } }, body }))
export const cancelArrival = (id: number) => unwrap(api.POST('/api/v1/portal/warehouse/arrivals/{id}/cancel', { params: { path: { id } } }))
export const listExits = (search: string, skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/warehouse/exits', { params: { query: { search: search || undefined, skip, take } } }))
export const createExit = (body: PortalExitRequest) => unwrap(api.POST('/api/v1/portal/warehouse/exits', { body }))
export const updateExit = (publicId: string, body: Schemas['PortalExitHeaderRequest']) => unwrap(api.PATCH('/api/v1/portal/warehouse/exits/{publicId}', { params: { path: { publicId } }, body }))
export const modifyExitLine = (publicId: string, lineId: number, quantity: number | null) => unwrap(api.PATCH('/api/v1/portal/warehouse/exits/{publicId}/lines/{lineId}', { params: { path: { publicId, lineId } }, body: { quantity } }))
export const getExit = (publicId: string) => unwrap(api.GET('/api/v1/portal/warehouse/exits/{publicId}', { params: { path: { publicId } } }))
export const cancelExit = (publicId: string) => unwrap(api.POST('/api/v1/portal/warehouse/exits/{publicId}/cancel', { params: { path: { publicId } }, body: {} }))
export const getValuation = (search: string, skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/warehouse/valuation', { params: { query: { search: search || undefined, skip, take } } }))

/** «¿Cómo va mi orden?»: consulta PÚBLICA por el ENLACE (token propio de la orden, imposible de adivinar). Solo devuelve el avance. */
export const trackByLink = (token: string, lang: string) => unwrap(api.GET('/api/v1/public/track/link/{token}', { params: { path: { token }, query: { lang } } }))

/** Baja de los avisos con el enlace de un aviso (sin sesión; idempotente). */
export const unsubscribeConsignee = async (token: string) => {
  await unwrap(api.POST('/api/v1/public/consignee-unsubscribe/{token}', { params: { path: { token } } }))
}

// ---------------------------------------------------------------- avisos al consignatario (Lote 116): los prende el CLIENTE y marca, por consignatario, correo y/o texto
export type ConsigneeNoticeSettingsDto = Schemas['ConsigneeNoticeSettingsDto']
export type ConsigneeNoticeConsigneeDto = Schemas['ConsigneeNoticeConsigneeDto']
export const getConsigneeNotices = () => unwrap(api.GET('/api/v1/portal/consignee-notices'))
export const setConsigneeNotices = (body: { enabled?: boolean; lang?: string }) => unwrap(api.PUT('/api/v1/portal/consignee-notices', { body }))
export const listNoticeConsignees = (search: string, skip = 0, take = PAGE_SIZE) =>
  unwrap(api.GET('/api/v1/portal/consignee-notices/consignees', { params: { query: { search: search || undefined, skip, take } } }))
export const markConsignee = (publicId: string, channel: 'EMAIL' | 'SMS', accepted: boolean, warningAccepted: boolean) =>
  unwrap(api.PUT('/api/v1/portal/consignee-notices/consignees/{publicId}', { params: { path: { publicId } }, body: { channel, accepted, warningAccepted } }))

// ---------------------------------------------------------------- consignatario con acceso propio (Lote 117): su espacio (perfil, bandeja, mensajes con su cliente)
export type ConsigneeMeDto = Schemas['ConsigneeMeDto']
export type ConsigneeChannelDto = Schemas['ConsigneeChannelDto']
export type ConsigneeInboxItemDto = Schemas['ConsigneeInboxItemDto']
export const getConsigneeMe = () => unwrap(api.GET('/api/v1/portal/consignee/me'))
export const setConsigneeChannel = (channel: 'EMAIL' | 'SMS', enabled: boolean) =>
  unwrap(api.PUT('/api/v1/portal/consignee/me/notices/{channel}', { params: { path: { channel } }, body: { enabled } }))
export const listConsigneeInbox = (skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/consignee/notices', { params: { query: { skip, take } } }))
export type ConsigneeOrderListItemDto = Schemas['ConsigneeOrderListItemDto']
export type ConsigneeOrderDto = Schemas['ConsigneeOrderDto']
/** Mis envíos (solo lectura: el consignatario no crea órdenes). */
export const listConsigneeOrders = (search: string, skip = 0, take = PAGE_SIZE) => unwrap(api.GET('/api/v1/portal/consignee/orders', { params: { query: { search: search || undefined, skip, take } } }))
export const getConsigneeOrder = (publicId: string) => unwrap(api.GET('/api/v1/portal/consignee/orders/{publicId}', { params: { path: { publicId } } }))
export const getConsigneeOwnMessages = (before?: number, take = 30) => unwrap(api.GET('/api/v1/portal/consignee/messages', { params: { query: { before, take } } }))
export const sendConsigneeOwnMessage = (body: string) => unwrap(api.POST('/api/v1/portal/consignee/messages', { body: { body } }))
export const markConsigneeOwnMessagesRead = async () => {
  await unwrap(api.POST('/api/v1/portal/consignee/messages/read'))
}
export const getConsigneeOwnUnread = () => unwrap(api.GET('/api/v1/portal/consignee/messages/unread'))

// ---------------------------------------------------------------- el cliente le da acceso a un consignatario y lee sus mensajes (Lote 117)
export type ConsigneeAccessDto = Schemas['ConsigneeAccessDto']
export type ConsigneeAccessInviteResultDto = Schemas['ConsigneeAccessInviteResultDto']
export type ConsigneeConversationSummaryDto = Schemas['ConsigneeConversationSummaryDto']
export type ConsigneeConversationDto = Schemas['ConsigneeConversationDto']
export const listConsigneeAccess = (locationId: string) => unwrap(api.GET('/api/v1/portal/consignee-access/{locationId}', { params: { path: { locationId } } }))
export const inviteConsigneeAccess = (locationId: string, email: string, fullName?: string) =>
  unwrap(api.POST('/api/v1/portal/consignee-access/{locationId}', { params: { path: { locationId } }, body: { email: email || undefined, fullName: fullName || undefined } }))
export const resendConsigneeAccess = (locationId: string, id: number) => unwrap(api.POST('/api/v1/portal/consignee-access/{locationId}/{id}/resend', { params: { path: { locationId, id } } }))
export const removeConsigneeAccess = async (locationId: string, id: number) => {
  await unwrap(api.DELETE('/api/v1/portal/consignee-access/{locationId}/{id}', { params: { path: { locationId, id } } }))
}
export const listConsigneeConversations = (unreadOnly: boolean, q: string, skip = 0, take = PAGE_SIZE) =>
  unwrap(api.GET('/api/v1/portal/consignee-messages', { params: { query: { unreadOnly, q: q || undefined, skip, take } } }))
export const getConsigneeConversationsUnread = () => unwrap(api.GET('/api/v1/portal/consignee-messages/unread'))
export const getConsigneeConversation = (publicId: string, before?: number, take = 30) =>
  unwrap(api.GET('/api/v1/portal/consignee-messages/{publicId}', { params: { path: { publicId }, query: { before, take } } }))
export const replyConsigneeConversation = (publicId: string, body: string) => unwrap(api.POST('/api/v1/portal/consignee-messages/{publicId}', { params: { path: { publicId } }, body: { body } }))
export const markConsigneeConversationRead = async (publicId: string) => {
  await unwrap(api.POST('/api/v1/portal/consignee-messages/{publicId}/read', { params: { path: { publicId } } }))
}

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

/** Lote 119: mis facturas (solo lectura; sin borradores). `status` opcional: ISSUED, OVERDUE, PAID o VOID. */
export const listInvoices = (status: string | undefined, skip = 0, take = PAGE_SIZE) =>
  unwrap(api.GET('/api/v1/portal/invoices', { params: { query: { status: status || undefined, skip, take } } }))
export const getInvoice = (publicId: string) => unwrap(api.GET('/api/v1/portal/invoices/{publicId}', { params: { path: { publicId } } }))

/** Lote 119: mis preferencias de aviso (la compañía manda; yo solo apago lo que no quiero recibir). */
export const getNoticePrefs = () => unwrap(api.GET('/api/v1/portal/notice-prefs'))
export const saveNoticePrefs = (prefs: PortalNoticePrefDto[]) => unwrap(api.PUT('/api/v1/portal/notice-prefs', { body: { prefs } }))

/** Lote 120: mi perfil (solo cambio mi nombre), los datos de mi empresa, mi contraseña y los indicadores de mi cliente (7, 30 o 90 días). */
export const getProfile = () => unwrap(api.GET('/api/v1/portal/profile'))
export const saveProfile = (fullName: string) => unwrap(api.PUT('/api/v1/portal/profile', { body: { fullName } }))
export const changePassword = (currentPassword: string, newPassword: string) => unwrap(api.PUT('/api/v1/auth/password', { body: { currentPassword, newPassword } }))
export const getIndicators = (days: number) => unwrap(api.GET('/api/v1/portal/indicators', { params: { query: { days } } }))

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
