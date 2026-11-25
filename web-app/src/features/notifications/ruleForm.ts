// Reglas puras del formulario de una regla de aviso (Lote 52): estado, validación (mismos límites y mensajes que el servidor,
// NotificationRules), armado del request y cómo se inserta una {variable} en un texto.
import type { NotificationEventDto, NotificationRuleDto, NotificationRuleRequest } from './notificationsApi'

type Translate = (key: string, params?: Record<string, string | number>) => string

export const CHANNELS = ['EMAIL', 'SMS', 'APP', 'PUSH'] as const
export type ChannelCode = (typeof CHANNELS)[number]
export const NEEDS_SUBJECT: readonly string[] = ['EMAIL', 'APP']
export const MAX_NAME = 100
export const MAX_SUBJECT = 200
export const MAX_BODY = 1000

export interface ChannelForm {
  channel: string
  enabled: boolean
  subjectEs: string
  subjectEn: string
  bodyEs: string
  bodyEn: string
}

/** CONTEXT (Lote 243) = «Del evento»: la clave (value) la declara el evento, p. ej. SELLER o CLIENT. */
export type RecipientKind = 'ROLE' | 'USER' | 'EMAIL' | 'PHONE' | 'CONTEXT'

export interface RecipientForm {
  kind: RecipientKind
  roleId?: number
  userId?: number
  value?: string
  label: string
}

export interface RuleForm {
  eventCode: string
  name: string
  isActive: boolean
  channels: ChannelForm[]
  recipients: RecipientForm[]
}

/** Los cuatro canales con los textos por omisión del evento (o vacíos si todavía no hay evento), todos apagados. Un canal con plantilla propia (`channelDefaults`, p. ej. el correo con la orden completa) la usa. */
export function defaultChannels(event: NotificationEventDto | undefined): ChannelForm[] {
  return CHANNELS.map((channel) => {
    const own = (event?.channelDefaults ?? []).find((d) => d.channel === channel)
    return {
      channel,
      enabled: false,
      subjectEs: own?.subject ?? event?.defaultSubject ?? '',
      subjectEn: own?.subjectEn ?? event?.defaultSubjectEn ?? '',
      bodyEs: own?.body ?? event?.defaultBody ?? '',
      bodyEn: own?.bodyEn ?? event?.defaultBodyEn ?? '',
    }
  })
}

/** Los destinatarios «Del evento» que el evento trae marcados por omisión (se prellenan al escoger el evento en una regla nueva). */
export function defaultContextRecipients(event: NotificationEventDto | undefined, lang: 'es' | 'en' = 'es'): RecipientForm[] {
  return (event?.contextRecipients ?? [])
    .filter((c) => c.isDefault && c.key)
    .map((c) => ({ kind: 'CONTEXT' as const, value: c.key!, label: (lang === 'en' ? c.labelEn : c.label) ?? c.key! }))
}

/**
 * Destinatarios al cambiar de evento: se quitan los «Del evento» que el evento nuevo no declara (el servidor los rechaza con 400
 * «Este evento no tiene el destinatario '…'.») y se conservan los demás; si no queda ninguno se prellenan los del evento marcados por omisión.
 */
export function recipientsForEvent(current: readonly RecipientForm[], event: NotificationEventDto | undefined, lang: 'es' | 'en' = 'es'): RecipientForm[] {
  const keys = new Set((event?.contextRecipients ?? []).map((c) => c.key).filter((k): k is string => !!k))
  const kept = current.filter((r) => r.kind !== 'CONTEXT' || keys.has(r.value ?? ''))
  return kept.length === 0 ? defaultContextRecipients(event, lang) : kept
}

/** Nombres de las variables bloque del evento (solo van en el texto del correo, nunca en un asunto). */
export function blockVariables(event: NotificationEventDto | undefined): string[] {
  return (event?.variables ?? []).filter((v) => v.isBlock && v.name).map((v) => v.name!)
}

/** La primera variable bloque que algún asunto usa, o undefined. Mismo criterio del servidor (NotificationRules.VariablesOf). */
export function blockInSubject(f: Pick<RuleForm, 'channels'>, event: NotificationEventDto | undefined): string | undefined {
  const blocks = blockVariables(event)
  if (blocks.length === 0) return undefined
  for (const c of f.channels) {
    for (const text of [c.subjectEs, c.subjectEn]) {
      for (const m of text.matchAll(/\{(\w+)\}/g)) if (blocks.includes(m[1])) return m[1]
    }
  }
  return undefined
}

export function emptyForm(): RuleForm {
  return { eventCode: '', name: '', isActive: false, channels: defaultChannels(undefined), recipients: [] }
}

export function formOfRule(rule: NotificationRuleDto): RuleForm {
  return {
    eventCode: rule.eventCode ?? '',
    name: rule.name ?? '',
    isActive: !!rule.isActive,
    channels: CHANNELS.map((channel) => {
      const c = (rule.channels ?? []).find((x) => x.channel === channel)
      return { channel, enabled: !!c?.isEnabled, subjectEs: c?.subjectEs ?? '', subjectEn: c?.subjectEn ?? '', bodyEs: c?.bodyEs ?? '', bodyEn: c?.bodyEn ?? '' }
    }),
    recipients: (rule.recipients ?? []).map((r) => ({
      kind: r.kind as RecipientKind,
      roleId: r.roleId ?? undefined,
      userId: r.userId ?? undefined,
      value: r.value ?? undefined,
      label: r.label ?? '',
    })),
  }
}

export interface FormErrors {
  name?: string
  eventCode?: string
  channels?: string
}

export function validateForm(f: RuleForm, creating: boolean, t: Translate, event?: NotificationEventDto): FormErrors {
  const errors: FormErrors = {}
  const name = f.name.trim()
  if (!name) errors.name = t('notifications.rules.errors.nameRequired')
  else if (name.length > MAX_NAME) errors.name = t('notifications.rules.errors.nameMax')
  if (creating && !f.eventCode) errors.eventCode = t('notifications.rules.errors.eventRequired')
  const block = blockInSubject(f, event)
  if (block) errors.channels = t('notifications.rules.errors.blockInSubject', { name: `{${block}}` })
  return errors
}

export function recipientRequests(recipients: readonly RecipientForm[]) {
  return recipients.map((r) => ({ kind: r.kind, roleId: r.roleId, userId: r.userId, value: r.value }))
}

/** Alta: todo. Edición: nombre, activa, los cuatro canales con su texto y la lista COMPLETA de destinatarios (reemplaza) + rowVersion. */
export function buildRequest(f: RuleForm, original: NotificationRuleDto | null): NotificationRuleRequest {
  const channels = f.channels.map((c) => ({
    channel: c.channel,
    isEnabled: c.enabled,
    subjectEs: c.subjectEs.trim(),
    subjectEn: c.subjectEn.trim(),
    bodyEs: c.bodyEs.trim(),
    bodyEn: c.bodyEn.trim(),
  }))
  const common = { name: f.name.trim(), isActive: f.isActive, channels, recipients: recipientRequests(f.recipients) }
  return original ? { ...common, rowVersion: original.rowVersion } : { eventCode: f.eventCode, ...common }
}

/** Mismas reglas del servidor para los destinatarios sueltos (NotificationRules.IsValidEmail / IsValidPhone). */
export function isValidEmail(v: string): boolean {
  const s = v.trim()
  return s.length > 0 && s.length <= 200 && /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(s)
}

export function isValidPhone(v: string): boolean {
  const s = v.trim()
  if (!s || s.length > 30 || /[^\d+\s\-()]/.test(s)) return false
  const digits = (s.match(/\d/g) ?? []).length
  return digits >= 7 && digits <= 15
}

/** Clave que identifica a un destinatario para no repetirlo. */
export function recipientKey(r: Pick<RecipientForm, 'kind' | 'roleId' | 'userId' | 'value'>): string {
  if (r.kind === 'CONTEXT') return `C${(r.value ?? '').trim()}`
  return r.kind === 'ROLE' ? `R${r.roleId}` : r.kind === 'USER' ? `U${r.userId}` : `${r.kind[0]}${(r.value ?? '').trim().toLowerCase()}`
}

/** Inserta `{variable}` en el texto en la posición del cursor (o al final si no hay) y devuelve el texto nuevo y dónde queda el cursor. */
export function insertVariable(text: string, variable: string, start: number | null, end: number | null): { text: string; caret: number } {
  const token = `{${variable}}`
  const s = start ?? text.length
  const e = end ?? s
  return { text: text.slice(0, s) + token + text.slice(e), caret: s + token.length }
}
