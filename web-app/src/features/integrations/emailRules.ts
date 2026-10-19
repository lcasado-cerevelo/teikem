// Reglas puras del conector de correo de Integraciones (Lote 133): valores del formulario, validación en cliente (mismos mensajes que el servidor) y el cuerpo del API.
// El cuerpo de un correo se lee con «campos a leer» (etiqueta, expresión regular o bloque de renglones); cada campo va a una columna de la plantilla de importación de la integración.
import type { components } from '../../kernel/api/schema'

type EmailConfig = components['schemas']['EmailConfig']

export const MATCH_OPERATORS = ['CONTAINS', 'EQUALS', 'STARTS', 'ENDS', 'WILDCARD', 'REGEX'] as const
export const ATTACHMENT_MODES = ['ALL', 'FIRST', 'BY_NAME'] as const
export const BODY_KINDS = ['LABEL', 'REGEX', 'BLOCK'] as const
export const TRANSFORMS = ['NONE', 'UPPER', 'DIGITS', 'MONEY'] as const

export interface BodyFieldValues {
  position: string
  kind: string
  label: string
  pattern: string
  required: boolean
  transform: string
  start: string
  end: string
  separator: string
  /** Solo bloques: «6=1, 7=2» (posición de la plantilla = número de columna del renglón). */
  columns: string
}

export interface EmailValues {
  host: string
  port: string
  useSsl: boolean
  username: string
  /** Vacío al editar = se conserva la guardada. */
  password: string
  mailbox: string
  processedFolder: string
  subjectOperator: string
  subjectValue: string
  /** Un remitente (correo o dominio) por línea o separados por coma. */
  allowedSenders: string
  requireAttachment: boolean
  attachmentMode: string
  attachmentNameOperator: string
  attachmentNameValue: string
  /** Extensiones permitidas, separadas por coma (csv, txt). */
  attachmentTypes: string
  readBody: boolean
  bodyFields: BodyFieldValues[]
}

export interface EmailMessages {
  host: string
  port: string
  username: string
  password: string
  mailbox: string
  noFields: string
  position: string
  label: string
  pattern: string
  block: string
  blockColumns: string
  nameFilter: string
  senders: string
}

export const emptyBodyField = (): BodyFieldValues => ({ position: '', kind: 'LABEL', label: '', pattern: '', required: false, transform: 'NONE', start: '', end: '', separator: ' ', columns: '' })

export function emptyEmail(): EmailValues {
  return {
    host: '', port: '993', useSsl: true, username: '', password: '', mailbox: 'INBOX', processedFolder: 'Procesados', subjectOperator: 'CONTAINS', subjectValue: '', allowedSenders: '', requireAttachment: false,
    attachmentMode: 'ALL', attachmentNameOperator: 'WILDCARD', attachmentNameValue: '', attachmentTypes: 'csv, txt', readBody: false, bodyFields: [],
  }
}

/** «6=1, 7=2» → [{position: 6, index: 1}, …]; null si algo no es «número=número». */
export function parseBlockColumns(text: string): { position: number; index: number }[] | null {
  const parts = text.split(/[,;]/).map((p) => p.trim()).filter(Boolean)
  if (parts.length === 0) return null
  const out: { position: number; index: number }[] = []
  for (const p of parts) {
    const m = /^(\d+)\s*=\s*(\d+)$/.exec(p)
    if (!m) return null
    out.push({ position: Number(m[1]), index: Number(m[2]) })
  }
  return out
}

export const parseSenders = (text: string) => [...new Set(text.split(/[\n,;]/).map((s) => s.trim().toLowerCase()).filter(Boolean))]
export const parseTypes = (text: string) => [...new Set(text.split(/[\n,;]/).map((s) => s.trim().toLowerCase().replace(/^\./, '')).filter(Boolean))]

export interface EmailIssue {
  path: string
  message: string
}

/** Errores del formulario de correo (ruta relativa a `email.`): lo mínimo que el servidor también exige. */
export function emailIssues(e: EmailValues, hasSavedPassword: boolean, m: EmailMessages): EmailIssue[] {
  const issues: EmailIssue[] = []
  if (!e.host.trim()) issues.push({ path: 'host', message: m.host })
  const port = Number(e.port)
  if (!Number.isInteger(port) || port < 1 || port > 65535) issues.push({ path: 'port', message: m.port })
  if (!e.username.trim()) issues.push({ path: 'username', message: m.username })
  if (!hasSavedPassword && !e.password) issues.push({ path: 'password', message: m.password })
  if (!e.mailbox.trim()) issues.push({ path: 'mailbox', message: m.mailbox })
  if (e.attachmentMode === 'BY_NAME' && !e.attachmentNameValue.trim()) issues.push({ path: 'attachmentNameValue', message: m.nameFilter })
  if (parseSenders(e.allowedSenders).some((s) => !/^(@?[a-z0-9.-]+\.[a-z]{2,}|[^@\s]+@[a-z0-9.-]+\.[a-z]{2,})$/.test(s))) issues.push({ path: 'allowedSenders', message: m.senders })
  if (e.readBody && e.bodyFields.length === 0) issues.push({ path: 'bodyFields', message: m.noFields })
  e.bodyFields.forEach((f, i) => {
    const pos = Number(f.position)
    if (!Number.isInteger(pos) || pos < 1 || pos > 500) issues.push({ path: `bodyFields.${i}.position`, message: m.position })
    if (f.kind === 'LABEL' && !f.label.trim()) issues.push({ path: `bodyFields.${i}.label`, message: m.label })
    if (f.kind === 'REGEX' && !f.pattern.trim()) issues.push({ path: `bodyFields.${i}.pattern`, message: m.pattern })
    if (f.kind === 'BLOCK') {
      if (!f.start.trim() || !f.end.trim()) issues.push({ path: `bodyFields.${i}.start`, message: m.block })
      if (!parseBlockColumns(f.columns)) issues.push({ path: `bodyFields.${i}.columns`, message: m.blockColumns })
    }
  })
  return issues
}

export function emailValuesOf(c: EmailConfig | null | undefined): EmailValues {
  if (!c) return emptyEmail()
  return {
    host: c.host ?? '',
    port: String(c.port ?? 993),
    useSsl: c.useSsl ?? true,
    username: c.username ?? '',
    password: '',
    mailbox: c.mailbox ?? 'INBOX',
    processedFolder: c.processedFolder ?? '',
    subjectOperator: c.subject?.operator ?? 'CONTAINS',
    subjectValue: c.subject?.value ?? '',
    allowedSenders: (c.allowedSenders ?? []).join(', '),
    requireAttachment: c.requireAttachment ?? false,
    attachmentMode: c.attachmentMode ?? 'ALL',
    attachmentNameOperator: c.attachmentName?.operator ?? 'WILDCARD',
    attachmentNameValue: c.attachmentName?.value ?? '',
    attachmentTypes: (c.attachmentTypes ?? ['csv', 'txt']).map((t) => t.replace(/^\./, '')).join(', '),
    readBody: c.readBody ?? false,
    bodyFields: (c.bodyFields ?? []).map((f) => ({
      position: String(f.position ?? ''),
      kind: f.kind ?? 'LABEL',
      label: f.label ?? '',
      pattern: f.pattern ?? '',
      required: f.required ?? false,
      transform: f.transform ?? 'NONE',
      start: f.start ?? '',
      end: f.end ?? '',
      separator: f.separator ?? ' ',
      columns: (f.columns ?? []).map((c) => `${c.position}=${c.index}`).join(', '),
    })),
  }
}

/** El cuerpo del API (sin la contraseña, que va aparte). */
export function buildEmailConfig(e: EmailValues): EmailConfig {
  return {
    host: e.host.trim(),
    port: Number(e.port),
    useSsl: e.useSsl,
    username: e.username.trim(),
    mailbox: e.mailbox.trim(),
    processedFolder: e.processedFolder.trim(),
    subject: e.subjectValue.trim() ? { operator: e.subjectOperator, value: e.subjectValue.trim() } : null,
    allowedSenders: parseSenders(e.allowedSenders),
    requireAttachment: e.requireAttachment,
    attachmentMode: e.attachmentMode,
    attachmentName: e.attachmentMode === 'BY_NAME' ? { operator: e.attachmentNameOperator, value: e.attachmentNameValue.trim() } : null,
    attachmentTypes: parseTypes(e.attachmentTypes),
    readBody: e.readBody,
    bodyFields: e.readBody
      ? e.bodyFields.map((f) => ({
          position: Number(f.position),
          kind: f.kind,
          label: f.kind === 'LABEL' ? f.label.trim() : null,
          pattern: f.kind === 'REGEX' ? f.pattern.trim() : null,
          required: f.required,
          transform: f.transform,
          start: f.kind === 'BLOCK' ? f.start.trim() : null,
          end: f.kind === 'BLOCK' ? f.end.trim() : null,
          separator: f.kind === 'BLOCK' ? f.separator : null,
          columns: f.kind === 'BLOCK' ? parseBlockColumns(f.columns) : null,
        }))
      : [],
  }
}
