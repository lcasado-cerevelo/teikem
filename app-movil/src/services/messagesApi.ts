import { api, getAccessToken } from '@config/axios';
import { env } from '@config/env';
import type { Company } from '@services/driverApi';

// Llamadas a /api/v1/messages/*. Cada una lleva `tenantId` (el chat es de cada compañía; el token de esa compañía firma la llamada).

export interface MessagePerson {
  id: number;
  name: string;
  email: string;
  online: boolean;
}

export interface MessageRefCard {
  entity: 'ORDER' | 'TRIP';
  publicId: string;
  title: string;
  subtitle: string | null;
  status: string | null;
  canOpen: boolean;
}

export interface MessagePhoto {
  publicId: string;
  contentType: string;
  sizeBytes: number;
  /** ACTIVE | ARCHIVED | DELETED: solo ACTIVE se puede ver. */
  state: string;
  hasThumb: boolean;
}

export interface ChatMessage {
  id: number;
  senderId: number;
  senderName: string;
  body: string;
  createdAtUtc: string;
  mine: boolean;
  ref: MessageRefCard | null;
  attachments: MessagePhoto[];
}

export interface ChatThread {
  /** Clave única entre compañías. */
  key: string;
  tenantId: number;
  companyName: string;
  publicId: string;
  kind: 'DIRECT' | 'GROUP';
  name: string;
  memberCount: number;
  lastMessagePreview: string | null;
  lastSenderName: string | null;
  lastMessageAtUtc: string;
  unread: number;
  isMuted: boolean;
}

export interface ChatThreadDetail {
  thread: ChatThread;
  messages: ChatMessage[];
  hasMore: boolean;
}

export interface Suggestion {
  source: string;
  sourceLabel: string;
  userId: number | null;
  roleId: number | null;
  name: string;
  online: boolean;
}

export interface OutgoingPhoto {
  fileName?: string;
  dataBase64: string;
}

export interface OutgoingRef {
  entity: 'ORDER' | 'TRIP';
  publicId: string;
}

interface ThreadSummaryDto {
  publicId: string;
  kind: 'DIRECT' | 'GROUP';
  name: string;
  memberCount: number;
  lastMessagePreview?: string | null;
  lastSenderName?: string | null;
  lastMessageAtUtc: string;
  unread: number;
  isMuted: boolean;
}

interface MessageDto {
  id: number;
  senderId: number;
  senderName: string;
  body: string;
  createdAtUtc: string;
  mine: boolean;
  ref?: MessageRefCard | null;
  attachments?: MessagePhoto[] | null;
}

interface ThreadDto {
  thread: ThreadSummaryDto;
  messages: MessageDto[];
  hasMore: boolean;
}

const toThread = (c: Company, t: ThreadSummaryDto): ChatThread => ({
  key: `${c.tenantId}:${t.publicId}`,
  tenantId: c.tenantId,
  companyName: c.name,
  publicId: t.publicId,
  kind: t.kind,
  name: t.name,
  memberCount: t.memberCount,
  lastMessagePreview: t.lastMessagePreview ?? null,
  lastSenderName: t.lastSenderName ?? null,
  lastMessageAtUtc: t.lastMessageAtUtc,
  unread: t.unread,
  isMuted: t.isMuted,
});

const toMessage = (m: MessageDto): ChatMessage => ({
  id: m.id,
  senderId: m.senderId,
  senderName: m.senderName,
  body: m.body,
  createdAtUtc: m.createdAtUtc,
  mine: m.mine,
  ref: m.ref ?? null,
  attachments: m.attachments ?? [],
});

export async function fetchCompanyThreads(c: Company): Promise<ChatThread[]> {
  const { data } = await api.get<ThreadSummaryDto[]>('messages/threads', { tenantId: c.tenantId });
  return data.map((t) => toThread(c, t));
}

export async function fetchThread(c: Company, publicId: string, before?: number): Promise<ChatThreadDetail> {
  const { data } = await api.get<ThreadDto>(`messages/threads/${publicId}`, { tenantId: c.tenantId, params: before ? { before } : undefined });
  return { thread: toThread(c, data.thread), messages: data.messages.map(toMessage), hasMore: data.hasMore };
}

export async function markThreadRead(tenantId: number, publicId: string): Promise<void> {
  await api.post(`messages/threads/${publicId}/read`, undefined, { tenantId });
}

export async function fetchPeople(tenantId: number, q: string): Promise<MessagePerson[]> {
  const { data } = await api.get<MessagePerson[]>('messages/people', { tenantId, params: { q: q || undefined, take: 30 } });
  return data;
}

export async function fetchSuggested(tenantId: number, entity: 'ORDER' | 'TRIP', publicId: string): Promise<Suggestion[]> {
  const { data } = await api.get<Suggestion[]>('messages/suggested', { tenantId, params: { entity, publicId } });
  return data;
}

export interface CreateThreadInput {
  userIds?: number[];
  roleId?: number;
  followRole?: boolean;
  text?: string;
  ref?: OutgoingRef;
  photos?: OutgoingPhoto[];
}

export async function createThread(c: Company, input: CreateThreadInput): Promise<ChatThreadDetail> {
  const { data } = await api.post<ThreadDto>(
    'messages/threads',
    {
      userIds: input.userIds,
      roleId: input.roleId,
      followRole: input.followRole,
      text: input.text,
      ref: input.ref ? { entity: input.ref.entity, publicId: input.ref.publicId } : undefined,
      attachments: input.photos?.length ? input.photos.map((p) => ({ fileName: p.fileName, dataBase64: p.dataBase64 })) : undefined,
    },
    { tenantId: c.tenantId, timeout: 120_000 },
  );
  return { thread: toThread(c, data.thread), messages: data.messages.map(toMessage), hasMore: data.hasMore };
}

/** El cuerpo de un mensaje en un hilo que ya existe (también lo usa la cola sin conexión). */
export function sendBody(text: string, photos?: OutgoingPhoto[], ref?: OutgoingRef) {
  return {
    text: text.trim() || undefined,
    ref: ref ? { entity: ref.entity, publicId: ref.publicId } : undefined,
    attachments: photos?.length ? photos.map((p) => ({ fileName: p.fileName, dataBase64: p.dataBase64 })) : undefined,
  };
}

/** Cómo pedir la foto de un mensaje (con la firma de la compañía): para `<Image source={...}>`. */
export function photoSource(tenantId: number, photoId: string, thumb: boolean): { uri: string; headers: Record<string, string> } {
  const base = env.baseUrl.endsWith('/') ? env.baseUrl : `${env.baseUrl}/`;
  return { uri: `${base}messages/attachments/${photoId}${thumb ? '?thumb=true' : ''}`, headers: { Authorization: `Bearer ${getAccessToken(tenantId) ?? ''}` } };
}
