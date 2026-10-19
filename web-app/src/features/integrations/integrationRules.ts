// Reglas puras de Integraciones (Lote 132): tonos de los estatus, formulario de alta/edición (esquema zod y cuerpo del API) y resumen del horario. Sin React: se prueba en integrationRules.test.ts.
import { z } from 'zod'
import type { ChipTone } from '../../kernel/ui'
import { buildEmailConfig, emailIssues, emailValuesOf, emptyEmail, type EmailMessages, type EmailValues } from './emailRules'
import type { IntegrationCreateRequest, IntegrationDto, IntegrationPatchRequest } from './integrationsApi'

export const RUN_TONE: Record<string, ChipTone> = { OK: 'deliv', PARTIAL: 'warn', FAILED: 'fail', RUNNING: 'route', NEVER: 'neutral' }
export const INBOX_TONE: Record<string, ChipTone> = { PENDING: 'warn', REPROCESSED: 'deliv', DISCARDED: 'neutral' }
export const FILE_TONE: Record<string, ChipTone> = { PROCESSED: 'deliv', REPROCESSED: 'deliv', DUPLICATE: 'neutral', FAILED: 'fail' }

export const NAME_MAX = 120
export const MAX_RETENTION_DAYS = 365
export const DEFAULT_RETENTION_DAYS = 30
export const FOLDER_PATTERN = /^[A-Za-z0-9_-]+$/

export interface IntegrationMessages {
  nameRequired: string
  nameMax: string
  clientRequired: string
  templateRequired: string
  folderRequired: string
  folderInvalid: string
  interval: string
  retention: string
  scopes: string
}

export interface IntegrationValues {
  name: string
  description: string
  clientPublicId: string
  templatePublicId: string
  connectorKind: string
  folder: string
  scheduleKind: string
  /** Minutos entre corridas (con horario). */
  intervalMinutes: string
  scopes: string[]
  notifyUserIds: number[]
  includeMe: boolean
  retentionDays: string
  /** Conector de correo (Lote 133). */
  email: EmailValues
}

/** Esquema del formulario. En la edición (`editing`) el cliente y el conector no se cambian, así que no se exigen. */
export function integrationSchema(m: IntegrationMessages, editing: boolean, email?: { messages: EmailMessages; hasSavedPassword: boolean }) {
  return z
    .object({
      name: z.string().trim().min(1, m.nameRequired).max(NAME_MAX, m.nameMax),
      description: z.string(),
      clientPublicId: z.string(),
      templatePublicId: z.string().min(1, m.templateRequired),
      connectorKind: z.string(),
      folder: z.string(),
      scheduleKind: z.string(),
      intervalMinutes: z.string(),
      scopes: z.array(z.string()).min(1, m.scopes),
      notifyUserIds: z.array(z.number()),
      includeMe: z.boolean(),
      retentionDays: z.string(),
      email: z.custom<EmailValues>(),
    })
    .superRefine((v, ctx) => {
      if (v.connectorKind === 'EMAIL' && email)
        for (const issue of emailIssues(v.email, email.hasSavedPassword, email.messages)) ctx.addIssue({ code: 'custom', path: ['email', ...issue.path.split('.').map((p) => (/^\d+$/.test(p) ? Number(p) : p))], message: issue.message })
      if (!editing && !v.clientPublicId) ctx.addIssue({ code: 'custom', path: ['clientPublicId'], message: m.clientRequired })
      if (v.connectorKind === 'FOLDER') {
        if (!v.folder.trim()) ctx.addIssue({ code: 'custom', path: ['folder'], message: m.folderRequired })
        else if (!FOLDER_PATTERN.test(v.folder.trim())) ctx.addIssue({ code: 'custom', path: ['folder'], message: m.folderInvalid })
      }
      if (v.scheduleKind === 'INTERVAL') {
        const minutes = Number(v.intervalMinutes)
        if (!Number.isFinite(minutes) || minutes < 1 || minutes > 10080) ctx.addIssue({ code: 'custom', path: ['intervalMinutes'], message: m.interval })
      }
      const days = Number(v.retentionDays)
      if (!Number.isInteger(days) || days < 1 || days > MAX_RETENTION_DAYS) ctx.addIssue({ code: 'custom', path: ['retentionDays'], message: m.retention })
    })
}

export function integrationValuesOf(i: IntegrationDto | null): IntegrationValues {
  if (!i) {
    return {
      name: '',
      description: '',
      clientPublicId: '',
      templatePublicId: '',
      connectorKind: 'FOLDER',
      folder: '',
      scheduleKind: 'MANUAL',
      intervalMinutes: '5',
      scopes: [],
      notifyUserIds: [],
      includeMe: true,
      retentionDays: String(DEFAULT_RETENTION_DAYS),
      email: emptyEmail(),
    }
  }
  return {
    name: i.name ?? '',
    description: i.description ?? '',
    clientPublicId: i.clientPublicId ?? '',
    templatePublicId: i.templatePublicId ?? '',
    connectorKind: i.connectorKind ?? 'FOLDER',
    folder: i.folder ?? '',
    scheduleKind: i.scheduleKind ?? 'MANUAL',
    intervalMinutes: String(Math.max(1, Math.round((i.intervalSeconds ?? 300) / 60))),
    scopes: [...(i.scopes ?? [])],
    notifyUserIds: (i.notifyUsers ?? []).map((u) => u.id ?? 0),
    includeMe: false,
    retentionDays: String(i.retentionDays ?? DEFAULT_RETENTION_DAYS),
    email: emailValuesOf(i.email),
  }
}

const scheduleOf = (v: IntegrationValues) =>
  v.connectorKind === 'MANUAL' || v.scheduleKind !== 'INTERVAL' ? { scheduleKind: 'MANUAL' } : { scheduleKind: 'INTERVAL', intervalSeconds: Math.round(Number(v.intervalMinutes) * 60) }

export function buildCreateRequest(v: IntegrationValues): IntegrationCreateRequest {
  return {
    name: v.name.trim(),
    description: v.description.trim() || null,
    clientPublicId: v.clientPublicId,
    connectorKind: v.connectorKind,
    folder: v.connectorKind === 'FOLDER' ? v.folder.trim() : null,
    templatePublicId: v.templatePublicId,
    ...scheduleOf(v),
    scopes: v.scopes,
    notifyUserIds: v.notifyUserIds,
    includeMe: v.includeMe,
    retentionDays: Math.round(Number(v.retentionDays)),
    ...(v.connectorKind === 'EMAIL' ? { email: buildEmailConfig(v.email), mailPassword: v.email.password } : {}),
  }
}

export function buildPatchRequest(v: IntegrationValues, current: Pick<IntegrationDto, 'connectorKind'>): IntegrationPatchRequest {
  return {
    name: v.name.trim(),
    description: v.description.trim(),
    templatePublicId: v.templatePublicId,
    ...(current.connectorKind === 'FOLDER' ? { folder: v.folder.trim() } : {}),
    ...scheduleOf(v),
    scopes: v.scopes,
    notifyUserIds: v.notifyUserIds,
    includeMe: v.includeMe || undefined,
    retentionDays: Math.round(Number(v.retentionDays)),
    ...(current.connectorKind === 'EMAIL' ? { email: buildEmailConfig(v.email), mailPassword: v.email.password || undefined } : {}),
  }
}

/** «Cada 5 min», «Cada 2 h», «Cada 1 d» o «Solo a mano». */
export function scheduleText(i: Pick<IntegrationDto, 'scheduleKind' | 'intervalSeconds'>, t: (key: string, p?: Record<string, string | number>) => string): string {
  if (i.scheduleKind !== 'INTERVAL' || !i.intervalSeconds) return t('integrations.schedule.manual')
  const s = i.intervalSeconds
  if (s % 86400 === 0) return t('integrations.schedule.everyDays', { n: s / 86400 })
  if (s % 3600 === 0) return t('integrations.schedule.everyHours', { n: s / 3600 })
  return t('integrations.schedule.everyMinutes', { n: Math.round(s / 60) })
}

/** Las filas de la bandeja con una celda cambiada (no muta). */
export function withCell(rows: readonly { row?: number; cells?: string[] | null }[], row: number, index: number, value: string): { row: number; cells: string[] }[] {
  return rows.map((r) => ({ row: r.row ?? 0, cells: (r.cells ?? []).map((c, i) => (r.row === row && i === index ? value : c)) }))
}
