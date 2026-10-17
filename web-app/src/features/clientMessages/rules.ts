// Reglas puras de «Mensajes de clientes» (Lote 104): validación del mensaje (espejo del servidor), orden y mezcla de mensajes
// y de conversaciones, y alineación de las burbujas. Sin React ni red.
import type { components } from '../../kernel/api/schema'

export type ConversationSummary = components['schemas']['ClientConversationSummaryDto']
export type Conversation = components['schemas']['ClientConversationDto']
export type ClientMessage = components['schemas']['ClientMessageDto']
export type ClientPerson = components['schemas']['ClientPersonDto']
export type TeamCandidate = components['schemas']['TeamCandidateDto']
export type ServiceTeam = components['schemas']['ClientServiceTeamDto']

/** Máximo de caracteres de un mensaje (igual que el servidor). */
export const MESSAGE_MAX = 2000
export const SENDER_STAFF = 'STAFF'
export const PAGE_STEP = 30

export type MessageError = 'empty' | 'tooLong' | null

/** Valida el texto a enviar: vacío (o solo espacios) o más de 2000 caracteres. */
export function validateMessage(body: string): MessageError {
  const text = body.trim()
  if (text.length === 0) return 'empty'
  if (text.length > MESSAGE_MAX) return 'tooLong'
  return null
}

/** Caracteres que quedan para escribir (puede ser negativo si se pasó). */
export function charsLeft(body: string): number {
  return MESSAGE_MAX - body.length
}

/** Mensajes en orden cronológico (por id), sin repetir; si un id llega dos veces gana el entrante. */
export function mergeMessages(existing: readonly ClientMessage[], incoming: readonly ClientMessage[]): ClientMessage[] {
  const byId = new Map<number, ClientMessage>()
  for (const m of existing) if (m.id !== undefined) byId.set(m.id, m)
  for (const m of incoming) if (m.id !== undefined) byId.set(m.id, m)
  return [...byId.values()].sort((a, b) => (a.id ?? 0) - (b.id ?? 0))
}

/** Conversaciones con lo más reciente primero (por la hora del último mensaje; empate por nombre de la persona). */
export function sortConversations(list: readonly ConversationSummary[]): ConversationSummary[] {
  return [...list].sort((a, b) => {
    const d = Date.parse(b.lastMessageAtUtc ?? '') - Date.parse(a.lastMessageAtUtc ?? '')
    if (Number.isFinite(d) && d !== 0) return d
    return (a.personName ?? '').localeCompare(b.personName ?? '')
  })
}

/** true = burbuja del equipo (a la derecha); false = de la persona del cliente (a la izquierda). */
export function isStaffMessage(m: ClientMessage): boolean {
  return m.senderKind === SENDER_STAFF
}

/** Total de no leídos de una lista de conversaciones. */
export function totalUnread(list: readonly ConversationSummary[]): number {
  return list.reduce((n, c) => n + (c.unread ?? 0), 0)
}

/** Texto de la insignia del menú: vacío con 0, «99+» desde 100. */
export function badgeText(n: number | undefined): string {
  if (!n || n <= 0) return ''
  return n > 99 ? '99+' : String(n)
}
